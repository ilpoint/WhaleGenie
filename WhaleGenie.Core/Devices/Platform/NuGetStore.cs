using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The packages a C# script is compiled and run with, and where they come from. What the macro
/// names is looked up on the official feed, brought down with everything it needs, and unpacked
/// into one folder per package and version, of which only one version of a package is kept: the
/// folders are what a compiler is pointed at, so a second copy of the same library would be a
/// second answer to the same question.
/// </summary>
internal sealed class NuGetStore(string folder)
{
    /// <summary>Where packages come from: the feed NuGet itself starts from.</summary>
    private const string Feed = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// The framework a package's own set of libraries is picked for. It is this program's own, so
    /// a package that ships one build for .NET and one for .NET Framework gives the first.
    /// </summary>
    private static readonly NuGetFramework Machine = NuGetFramework.Parse("net10.0");

    private readonly string _folder = folder;

    /// <summary>
    /// The dlls a C# script is compiled against and run with, by full path, as the package step
    /// left them. A script with no packages yet has none, and a list that cannot be read is nothing
    /// to reference rather than a reason to fail: what a script needs to compile is this machine's
    /// own assemblies plus whatever is really on disk.
    /// </summary>
    public static IReadOnlyList<string> References(string packages)
    {
        try
        {
            var list = Path.Combine(packages, "references.json");
            if (!File.Exists(list))
            {
                return [];
            }

            var written = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(list)) ?? [];

            return [.. written
                .Select(relative => Path.GetFullPath(Path.Combine(packages, relative)))
                .Where(File.Exists)];
        }
        catch (Exception failure) when (failure is IOException or JsonException
            or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>
    /// The folders the downloaded packages keep their Windows native libraries in. They are put on
    /// the search path of the process that runs a script, which is what a library asked for by name
    /// is found through.
    /// </summary>
    public static IReadOnlyList<string> NativeFolders(string packages)
    {
        var inside = Path.Combine("runtimes", "win-x64", "native");
        try
        {
            if (!Directory.Exists(packages))
            {
                return [];
            }

            return [.. Directory.EnumerateDirectories(packages, "native", SearchOption.AllDirectories)
                .Where(folder => folder.EndsWith(inside, StringComparison.OrdinalIgnoreCase))];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// What a macro wrote, read one package to a line: a name, or a name and the version it has to
    /// be, one word apart. A name without a version is whatever the newest stable one is.
    /// </summary>
    public static IReadOnlyList<Wanted> Written(IReadOnlyList<string> entries)
    {
        var wanted = new List<Wanted>();
        foreach (var entry in entries)
        {
            var words = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
            if (words.Length == 0)
            {
                continue;
            }

            if (words.Length == 1)
            {
                wanted.Add(new Wanted(words[0], null));
                continue;
            }

            if (!NuGetVersion.TryParse(words[1], out var version))
            {
                throw new DeviceActionException("Run.NuGetFailed",
                    $"{words[1]} is not a version number, in \"{entry}\"");
            }

            wanted.Add(new Wanted(words[0], version));
        }

        return wanted;
    }

    /// <summary>
    /// Whether that package is already unpacked — at the version asked for, or at any version when
    /// none was asked for. The answer is read off the folders rather than asked of the feed, so a
    /// step whose packages are all there needs no network at all.
    /// </summary>
    public bool There(Wanted wanted)
    {
        var versions = Folders(wanted.Name);
        return versions.Count != 0
            && (wanted.Version is null || versions.Any(folder => Same(folder, wanted.Version)));
    }

    /// <summary>
    /// Brings down every package that is not there yet, with everything those packages need, and
    /// says what went in. The list of dlls a script is compiled against is written again from what
    /// is on disk when the downloads are done.
    /// </summary>
    public async Task<IReadOnlyList<string>> Fetch(IReadOnlyList<Wanted> asked, ILogger log,
        Action<string> progress, CancellationToken token)
    {
        using var cache = new SourceCacheContext();
        var source = Repository.Factory.GetCoreV3(Feed);
        var chosen = new Dictionary<string, NuGetVersion>(StringComparer.OrdinalIgnoreCase);
        var waiting = new Queue<(string Name, VersionRange Range, bool Newest)>();

        foreach (var wanted in asked)
        {
            waiting.Enqueue((wanted.Name,
                wanted.Version is null ? VersionRange.All : new VersionRange(wanted.Version, true,
                    wanted.Version, true),
                wanted.Version is null));
        }

        var added = new List<string>();
        var fetching = new List<PackageIdentity>();
        while (waiting.Count > 0)
        {
            var (name, range, newest) = waiting.Dequeue();
            if (chosen.TryGetValue(name, out var settled))
            {
                if (range.Satisfies(settled))
                {
                    continue;
                }

                throw new DeviceActionException("Run.NuGetFailed",
                    $"{name} is needed at two versions that cannot both be there: "
                    + $"{settled.ToNormalizedString()} and {range.ToNormalizedString()}");
            }

            var version = await Pick(source, cache, name, range, newest, log, token);
            chosen[name] = version;

            if (There(new Wanted(name, version)))
            {
                // What is already unpacked was brought down with its own dependencies, so there is
                // nothing to work out again and nothing to go and get.
                continue;
            }

            var identity = new PackageIdentity(name, version);
            fetching.Add(identity);
            added.Add($"{identity.Id} {identity.Version.ToNormalizedString()}");
            progress($"{identity.Id} {identity.Version.ToNormalizedString()}");

            foreach (var (needed, neededRange) in await Needs(source, cache, identity, log, token))
            {
                waiting.Enqueue((needed, neededRange, false));
            }
        }

        foreach (var identity in fetching)
        {
            await Unpack(source, cache, identity, log, progress, token);
        }

        WriteReferences();
        return added;
    }

    /// <summary>
    /// The version of a package to take: the one asked for, the newest stable one when the macro
    /// left it out, and for something a package needs rather than something the macro named, the
    /// lowest one that fits — the rule NuGet itself uses, which keeps a package that asks for at
    /// least one version from dragging the newest of everything behind it.
    /// </summary>
    private static async Task<NuGetVersion> Pick(SourceRepository source, SourceCacheContext cache,
        string name, VersionRange range, bool newest, ILogger log, CancellationToken token)
    {
        var metadata = await source.GetResourceAsync<PackageMetadataResource>(token)
            ?? throw new DeviceActionException("Run.NuGetFailed",
                $"{Feed} did not answer with anything that lists packages");
        var known = await metadata.GetMetadataAsync(name, includePrerelease: false,
            includeUnlisted: false, cache, log, token);

        var fits = known
            .Where(package => range.Satisfies(package.Identity.Version))
            .Select(package => package.Identity.Version)
            .ToList();

        if (fits.Count == 0)
        {
            throw new DeviceActionException("Run.NuGetFailed",
                $"there is no {name} {Written(range)} on {Feed}");
        }

        return newest ? fits.Max()! : fits.Min()!;
    }

    /// <summary>A version range as it would be said to a person: the version, or any of them.</summary>
    private static string Written(VersionRange range)
        => range.MinVersion is null && range.MaxVersion is null
            ? "at any version"
            : range.ToNormalizedString();

    /// <summary>What one package says it needs, for the frame closest to this machine's own.</summary>
    private static async Task<IReadOnlyList<(string Name, VersionRange Range)>> Needs(
        SourceRepository source, SourceCacheContext cache, PackageIdentity identity, ILogger log,
        CancellationToken token)
    {
        using var archive = await Opened(source, cache, identity, log, token);
        var groups = archive.GetPackageDependencies().ToList();
        var group = NuGetFrameworkUtility.GetNearest(groups, Machine, item => item.TargetFramework);

        return [.. (group?.Packages ?? []).Select(dependency =>
            (dependency.Id, dependency.VersionRange))];
    }

    /// <summary>
    /// Unpacks one package: the libraries it was built for this machine's framework, and the
    /// Windows native ones, which are put where a process that runs a script searches for them.
    /// </summary>
    private async Task Unpack(SourceRepository source, SourceCacheContext cache,
        PackageIdentity identity, ILogger log, Action<string> progress, CancellationToken token)
    {
        using var archive = await Opened(source, cache, identity, log, token);
        var into = Place(identity);
        Directory.CreateDirectory(into);

        var libraries = NuGetFrameworkUtility.GetNearest(archive.GetLibItems(), Machine,
            item => item.TargetFramework);
        if (libraries is not null)
        {
            var framework = libraries.TargetFramework.GetShortFolderName();
            foreach (var entry in libraries.Items.Where(item =>
                item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                await Take(archive, entry, Path.Combine(into, "lib", framework, Path.GetFileName(entry)),
                    progress, token);
            }
        }

        foreach (var entry in archive.GetFiles().Where(item => Native(item)))
        {
            await Take(archive, entry,
                Path.Combine(into, "runtimes", "win-x64", "native", Path.GetFileName(entry)),
                progress, token);
        }
    }

    /// <summary>A native library of the build Windows on this machine would load.</summary>
    private static bool Native(string entry)
        => entry.StartsWith("runtimes/win-x64/native/", StringComparison.OrdinalIgnoreCase)
           && entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static async Task Take(PackageArchiveReader archive, string entry, string to,
        Action<string> progress, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        await using (var inside = archive.GetStream(entry))
        await using (var file = File.Create(to))
        {
            await inside.CopyToAsync(file, token);
        }

        progress($"{Path.GetFileName(entry)} -> {to}");
    }

    private static async Task<PackageArchiveReader> Opened(SourceRepository source,
        SourceCacheContext cache, PackageIdentity identity, ILogger log, CancellationToken token)
    {
        var finder = await source.GetResourceAsync<FindPackageByIdResource>(token)
            ?? throw new DeviceActionException("Run.NuGetFailed",
                $"{Feed} did not answer with anything that hands packages over");
        var held = new MemoryStream();
        await finder.CopyNupkgToStreamAsync(identity.Id, identity.Version, held, cache, log, token);
        held.Position = 0;
        return new PackageArchiveReader(held);
    }

    /// <summary>
    /// Where one package's one version is unpacked. What a package is called is a name the feed
    /// decides, so the folder is made under that spelling and any folder of the same name in
    /// another spelling is cleared away first.
    /// </summary>
    private string Place(PackageIdentity identity)
    {
        foreach (var name in Folders(identity.Id))
        {
            // One package, one copy: bringing down a newer version takes the place of the older
            // one rather than sitting beside it, because two copies of a library is two answers
            // to what a script means when it names a type.
            Directory.Delete(Path.Combine(_folder, name), recursive: true);
        }

        var version = identity.Version.ToNormalizedString();
        Directory.CreateDirectory(Path.Combine(_folder, identity.Id));
        return Path.Combine(_folder, identity.Id, version);
    }

    /// <summary>
    /// Writes down every library the packages brought, relative to the folder they live in. It is
    /// the list a compiler is pointed at and the list a script's host process loads them from, so
    /// it is written from what is on disk rather than from what this run happened to download.
    /// </summary>
    private void WriteReferences()
    {
        var found = Directory.Exists(_folder)
            ? Directory.EnumerateFiles(_folder, "*.dll", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Select(path => Path.GetRelativePath(_folder, path))
                .Where(relative => relative.Split(Path.DirectorySeparatorChar)
                    .Any(part => string.Equals(part, "lib", StringComparison.OrdinalIgnoreCase)))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "references.json"),
            JsonSerializer.Serialize(found, new JsonSerializerOptions { WriteIndented = true }),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>The folders one package has, whatever spelling of its name they were made under.</summary>
    private IReadOnlyList<string> Folders(string name)
    {
        if (!Directory.Exists(_folder))
        {
            return [];
        }

        return [.. Directory.EnumerateDirectories(_folder)
            .Where(folder => string.Equals(Path.GetFileName(folder), name,
                StringComparison.OrdinalIgnoreCase))];
    }

    private static bool Same(string folder, NuGetVersion version)
        => string.Equals(Path.GetFileName(folder), version.ToNormalizedString(),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>One package a macro asked for, with the version it asked for if it named one.</summary>
    public sealed record Wanted(string Name, NuGetVersion? Version);
}
