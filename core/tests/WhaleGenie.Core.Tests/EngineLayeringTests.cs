using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What this library is allowed to be, checked in the checkout the library is changed in rather than
/// only in the program that consumes it. The engine ships as a package on its own: a Windows
/// adapter, a graphics toolkit or an AI client behind it would follow every caller of a macro
/// around, and a second visible friend would let something outside reach past the public surface.
/// The program keeps the same guards for the layer above; these are the engine's own, so a change is
/// refused here before it is pulled in.
/// </summary>
public class EngineLayeringTests
{
    private static Assembly Engine() => typeof(WhaleGenie.Core.Execution.MacroRunner).Assembly;

    /// <summary>
    /// The engine is a library other programs are meant to use, so what it drags in has to be worth
    /// dragging: the base library and the one CSV parser a table step needs. Anything else is a
    /// dependency of the program, not of the engine.
    /// </summary>
    [Fact]
    public void The_engine_references_only_the_base_library_and_its_csv_parser()
    {
        var allowed = new[] { "CsvHelper", "netstandard", "mscorlib" };

        var unexpected = Engine().GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal))
            .Where(name => !allowed.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unexpected.Count == 0,
            "the engine references more than the base library and CsvHelper: "
            + string.Join(", ", unexpected));
    }

    /// <summary>
    /// The engine is plain .NET. A platform in its target framework would say otherwise and would
    /// quietly tie the library to one machine, so the absence of the attribute is the fact being
    /// checked rather than a build setting somewhere.
    /// </summary>
    [Fact]
    public void The_engine_targets_plain_dotnet()
    {
        var platform = Engine().GetCustomAttribute<TargetPlatformAttribute>();

        Assert.True(platform is null,
            $"the engine targets a platform: {platform?.PlatformName ?? "unknown"}");
    }

    /// <summary>
    /// The engine's own checks look inside it; nobody else does. A second name here means something
    /// outside the engine is reaching past the public surface, and the answer is to make that thing
    /// public — or to stop reaching — rather than to hand out another key.
    /// </summary>
    [Fact]
    public void Only_the_engine_checks_see_the_engine_internals()
    {
        var friends = Engine().GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName.Split(',')[0].Trim())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["WhaleGenie.Core.Tests"], friends);
    }

    /// <summary>
    /// The engine must not name the Windows adapters, in a using or in a project reference: the
    /// compiled reference would already fail, so this checks the side that a removed line cannot be
    /// seen to have been removed from.
    /// </summary>
    [Fact]
    public void The_engine_does_not_reference_the_Windows_adapters()
    {
        var referenced = Engine().GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToList();

        Assert.DoesNotContain("WhaleGenie.Platform.Windows", referenced);
    }

    /// <summary>
    /// No file of the engine may name the capture project. The engine was once built on it for the
    /// sake of five value types, and that is what made a headless run carry Direct3D.
    /// </summary>
    [Fact]
    public void The_engine_names_no_capture_project()
    {
        var offenders = CoreSources()
            .Where(file => File.ReadAllText(file)
                .Contains("WhaleGenie.GraphicsCapture", StringComparison.Ordinal))
            .Select(file => System.IO.Path.GetRelativePath(RepoPaths.Root, file))
            .ToList();

        Assert.True(offenders.Count == 0,
            "the engine names the capture project: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The engine has no business with a protocol server or a model client: those belong to the
    /// program, and every caller of a macro would inherit them.
    /// </summary>
    [Fact]
    public void The_engine_names_no_MCP_server_or_AI_library()
    {
        var offenders = CoreSources()
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("ModelContextProtocol", StringComparison.Ordinal)
                    || text.Contains("Microsoft.AspNetCore", StringComparison.Ordinal)
                    || text.Contains("Microsoft.Extensions.AI", StringComparison.Ordinal);
            })
            .Select(file => System.IO.Path.GetRelativePath(RepoPaths.Root, file))
            .ToList();

        Assert.True(offenders.Count == 0,
            "the engine must stay free of MCP and AI libraries: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Taking a picture of the screen is a Windows job built on the engine's contracts, not the
    /// other way round: the capture project references the engine and the program's adapters name
    /// neither the engine's implementation nor each other.
    /// </summary>
    [Fact]
    public void The_capture_library_is_built_on_the_engine()
    {
        var project = File.ReadAllText(
            System.IO.Path.Combine(RepoPaths.Source("WhaleGenie.GraphicsCapture"),
                "WhaleGenie.GraphicsCapture.csproj"));

        Assert.Contains("WhaleGenie.Core.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("WhaleGenie.Platform.Windows.csproj", project, StringComparison.Ordinal);

        var offenders = Directory.EnumerateFiles(
                RepoPaths.Source("WhaleGenie.GraphicsCapture"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file)
                .Contains("WhaleGenie.Platform.Windows", StringComparison.Ordinal))
            .Select(file => System.IO.Path.GetRelativePath(RepoPaths.Root, file))
            .ToList();

        Assert.True(offenders.Count == 0,
            "the capture library names a program adapter: " + string.Join(", ", offenders));
    }

    private static IEnumerable<string> CoreSources()
        => Directory.EnumerateFiles(RepoPaths.Source("WhaleGenie.Core"), "*.*",
                SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
