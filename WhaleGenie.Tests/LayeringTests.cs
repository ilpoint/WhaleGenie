using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
