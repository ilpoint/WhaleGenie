using System;
using System.IO;
using System.Linq;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The colours a window paints its own surfaces with belong in the theme palette, not in a view.
/// A literal dark grey here is a panel that stays dark when the light theme is chosen.
/// </summary>
public class ThemeAuditTests
{
    private static readonly string[] DarkSurfaceColours =
    [
        "#1F1F1F", "#292929", "#202020", "#2E2E2E", "#3A3A3A", "#404040",
        "#2A2A2A", "#333333", "#232323", "#141414", "#1E1E1E", "#3C3C3C",
    ];

    [Fact]
    public void Views_do_not_carry_their_own_dark_surfaces()
    {
        var offenders = Directory
            .EnumerateFiles(RepoPaths.Source("WhaleGenie", "Views"), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (Path: path, Line: line, Number: index + 1)))
            .Where(item => DarkSurfaceColours.Any(colour =>
                item.Line.Contains(colour, StringComparison.OrdinalIgnoreCase)))
            .Select(item => $"{Path.GetFileName(item.Path)}:{item.Number}: {item.Line.Trim()}")
            .ToList();

        Assert.Empty(offenders);
    }
}
