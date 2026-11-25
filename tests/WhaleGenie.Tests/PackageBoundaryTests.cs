using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WhaleGenie.Tests;

/// <summary>
/// The architecture boundary around package files: application and editor code may ask the package
/// store, but only the store and the pure format codec may open or save an archive. Without this
/// check the next feature quietly becomes another package writer.
/// </summary>
public class PackageBoundaryTests
{
    [Fact]
    public void Package_archives_are_read_and_written_only_by_the_stores()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("src", "WhaleGenie.App", "Projects", "FilePackageStore.cs"),
            Path.Combine("src", "WhaleGenie.Domain", "Storage", "PublicMacroLibrary.cs"),
        };
        var offenders = SourceFiles()
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("MacroPackage.Load(", StringComparison.Ordinal)
                    || text.Contains("MacroPackage.Save(", StringComparison.Ordinal)
                    || text.Contains("MacroPackage.LoadLibrary(", StringComparison.Ordinal)
                    || text.Contains("MacroPackage.SaveLibrary(", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(RepoPaths.Root, file))
            .Where(path => !allowed.Contains(path))
            .ToList();

        Assert.True(offenders.Count == 0,
            "package codec called outside the store: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Public_library_archives_are_read_and_written_only_by_their_store()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("src", "WhaleGenie.Domain", "Storage", "IPublicLibraryStore.cs"),
        };
        var offenders = SourceFiles()
            .Where(file => File.ReadAllText(file).Contains("PublicMacroLibrary.Load(", StringComparison.Ordinal)
                || File.ReadAllText(file).Contains("PublicMacroLibrary.Save(", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepoPaths.Root, file))
            .Where(path => !allowed.Contains(path))
            .ToList();

        Assert.True(offenders.Count == 0,
            "public library codec called outside the store: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Project_workspace_does_not_write_package_files()
    {
        var offenders = SourceFiles()
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("workspace.SavePackage(", StringComparison.Ordinal)
                    || text.Contains("workspace.OpenPackage(", StringComparison.Ordinal)
                    || text.Contains("project.SavePackage(", StringComparison.Ordinal)
                    || text.Contains("project.OpenPackage(", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(RepoPaths.Root, file))
            .ToList();

        Assert.True(offenders.Count == 0,
            "old workspace package entry survived: " + string.Join(", ", offenders));
    }

    private static IEnumerable<string> SourceFiles()
        => [.. Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "src"), "*.cs",
            SearchOption.AllDirectories)];
}
