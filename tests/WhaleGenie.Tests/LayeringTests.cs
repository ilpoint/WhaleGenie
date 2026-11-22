using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

namespace WhaleGenie.Tests;

/// <summary>
/// Which half of the program may know about the other. The engine holds every way of talking to
/// this machine behind its own interfaces, and the windows only ever hold those: the one place that
/// decides which Windows implementation to use is the composition root, with the tray beside it. A
/// window that reached past that — making a screen device, a UI Automation device or a recorder of
/// its own — would need changing the day a second implementation, or a check's stand-in, appears,
/// and would be the one place where what runs is not what the layer says.
/// </summary>
public class LayeringTests
{
    /// <summary>
    /// The two files that are the composition root: they name the Windows implementations because
    /// deciding which ones to use is the whole of their job.
    /// </summary>
    private static readonly string[] Assembling = ["App.axaml.cs", "Program.cs", "SharedDevices.cs"];

    [Fact]
    public void The_interface_names_no_Windows_implementation()
    {
        var offenders = new List<string>();

        foreach (var file in Sources())
        {
            if (Assembling.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            if (File.ReadAllText(file).Contains("WhaleGenie.Platform.Windows", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(RepoPaths.Root, file));
            }
        }

        Assert.True(offenders.Count == 0,
            "these files name a Windows implementation instead of the interface they were handed: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void The_application_layer_does_not_reference_a_window_toolkit()
    {
        var referenced = typeof(WhaleGenie.AppServices).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(referenced, name =>
            name.StartsWith("Avalonia", StringComparison.Ordinal));
    }

    [Fact]
    public void The_editing_project_is_free_of_the_window_and_the_program()
    {
        var referenced = Assembly.Load("WhaleGenie.Editing")
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(referenced, name =>
            name.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain("WhaleGenie", referenced);
    }

    [Fact]
    public void The_engine_does_not_reference_the_Windows_adapters()
    {
        var referenced = typeof(WhaleGenie.Core.Execution.MacroRunner).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToList();

        Assert.DoesNotContain("WhaleGenie.Platform.Windows", referenced);
    }

    [Fact]
    public void The_document_and_the_server_do_not_reference_the_window()
    {
        static string[] Referenced(Type type)
            => [.. type.Assembly.GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

        var document = Referenced(typeof(WhaleGenie.Models.ProjectWorkspace));
        var server = Referenced(typeof(WhaleGenie.Mcp.McpServerHost));

        Assert.DoesNotContain("WhaleGenie", document);
        Assert.DoesNotContain("WhaleGenie", server);

        foreach (var name in document.Concat(server))
        {
            Assert.False(name.StartsWith("Avalonia", StringComparison.Ordinal),
                $"the document or the MCP server references {name}");
        }
    }

    [Fact]
    public void The_engine_names_no_MCP_server_or_AI_library()
    {
        var offenders = new List<string>();
        foreach (var file in CoreSources())
        {
            var text = File.ReadAllText(file);
            if (text.Contains("ModelContextProtocol", StringComparison.Ordinal)
                || text.Contains("Microsoft.AspNetCore", StringComparison.Ordinal)
                || text.Contains("Microsoft.Extensions.AI", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(RepoPaths.Root, file));
            }
        }

        Assert.True(offenders.Count == 0,
            "the engine must stay free of MCP and AI libraries: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The engine is a library other programs are meant to use, so what it drags in has to be worth
    /// dragging: the base library and the one CSV parser it needs for a table step. Anything else —
    /// a Windows adapter, a graphics toolkit, an AI client — is a dependency of the program, not of
    /// the engine, and belongs where it is used rather than behind every caller of a macro.
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
    /// quietly tie the library to one machine — the attribute is how a caller can tell, so its
    /// absence is the fact being checked rather than a build setting somewhere.
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
    /// Taking a picture of the screen is a Windows job built on the engine's contracts, not the
    /// other way round: the shapes it works in live in the engine, and it must not know about the
    /// program's adapters. The other direction — the engine naming this project — is checked at the
    /// source level below, because a compiled reference cannot see what was removed.
    /// </summary>
    [Fact]
    public void The_capture_library_is_built_on_the_engine()
    {
        var capture = Assembly.Load("WhaleGenie.GraphicsCapture");
        var referenced = capture.GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToList();

        Assert.Contains("WhaleGenie.Core", referenced);
        Assert.DoesNotContain("WhaleGenie.Platform.Windows", referenced);
    }

    /// <summary>
    /// No file of the engine may name the capture project, in a using or in its project file: the
    /// engine was once built on it for the sake of five value types, and that is what made a
    /// headless run carry Direct3D.
    /// </summary>
    [Fact]
    public void The_engine_names_no_capture_project()
    {
        var offenders = CoreSources()
            .Where(file => File.ReadAllText(file)
                .Contains("WhaleGenie.GraphicsCapture", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepoPaths.Root, file))
            .ToList();

        Assert.True(offenders.Count == 0,
            "the engine names the capture project: " + string.Join(", ", offenders));
    }

    private static Assembly Engine() => typeof(WhaleGenie.Core.Execution.MacroRunner).Assembly;

    private static IEnumerable<string> CoreSources()
        => Directory.EnumerateFiles(RepoPaths.Source("WhaleGenie.Core"), "*.*",
                SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal));

    /// <summary>
    /// Every source file of the program the user runs, window and application layer both: the
    /// engine's are free to name a Windows implementation, these two are not.
    /// </summary>
    private static IEnumerable<string> Sources()
        => new[] { "WhaleGenie", "WhaleGenie.App" }
            .SelectMany(project => Directory.EnumerateFiles(RepoPaths.Source(project), "*.*",
                SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal));
}
