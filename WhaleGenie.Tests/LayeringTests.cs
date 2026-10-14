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
    private static readonly string[] Assembling = ["App.axaml.cs", "SharedDevices.cs"];

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

            if (File.ReadAllText(file).Contains("WhaleGenie.Core.Devices.Platform", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(Repository(), file));
            }
        }

        Assert.True(offenders.Count == 0,
            "these files name a Windows implementation instead of the interface they were handed: "
            + string.Join(", ", offenders));
    }

    /// <summary>Every source file of the program the user runs — the engine's are free to name them.</summary>
    private static IEnumerable<string> Sources()
        => Directory.EnumerateFiles(Path.Combine(Repository(), "WhaleGenie"), "*.*",
                SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal));

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
