using System.Linq;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The target window the device layer is tried against: controls at fixed places and a colour that
/// does not change, so a run can be checked against what it did rather than against what the
/// desktop happened to look like. It ships in the checks, not in the program.
/// </summary>
public class RangeWindowTests
{
    /// <summary>The window says where everything is, and putting it back takes the presses away.</summary>
    [Fact]
    public void The_range_window_reports_what_it_shows_and_resets()
        => Ui.Run(() =>
    {
        var window = new RangeWindow();
        window.Show();

        var before = JsonDocument.Parse(JsonSerializer.Serialize(window.Facts())).RootElement;
        Assert.True(before.GetProperty("available").GetBoolean());
        Assert.Equal(0, before.GetProperty("presses").GetInt32());
        Assert.Equal("#3366CC", before.GetProperty("colour").GetString());

        var targets = before.GetProperty("targets").EnumerateArray()
            .Select(target => target.GetProperty("id").GetString()).ToList();
        Assert.Equal(
            ["rangeCount", "rangeInput", "rangeFlag", "rangeColour", "rangeDanger"],
            targets);

        Press(window, "rangeCount");
        Press(window, "rangeCount");
        Press(window, "rangeDanger");
        Assert.Equal(2, window.Presses);
        Assert.Equal(1, window.DangerPresses);

        window.Reset();
        Assert.Equal(0, window.Presses);
        Assert.Equal(0, window.DangerPresses);
        Assert.Equal(string.Empty, window.Field);
        Assert.False(window.Flag);

        window.Close();
        Assert.Null(RangeWindow.Current);
    });

    /// <summary>Two controls sit where the window says they do, which is what a click aims at.</summary>
    [Fact]
    public void Every_control_sits_where_the_window_says()
        => Ui.Run(() =>
    {
        var window = new RangeWindow();
        window.Show();

        var facts = JsonDocument.Parse(JsonSerializer.Serialize(window.Facts())).RootElement;
        var count = facts.GetProperty("targets").EnumerateArray()
            .Single(target => target.GetProperty("id").GetString() == "rangeCount");
        Assert.Equal(RangeWindow.CountX, count.GetProperty("x").GetInt32());
        Assert.Equal(RangeWindow.CountY, count.GetProperty("y").GetInt32());
        Assert.Equal(RangeWindow.CountWidth, count.GetProperty("width").GetInt32());

        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(candidate => AutomationProperties.GetAutomationId(candidate) == "rangeCount");
        Assert.Equal(RangeWindow.CountWidth, (int)button.Bounds.Width);
        Assert.Equal(RangeWindow.CountY, (int)button.Bounds.Top);

        window.Close();
    });

    private static void Press(RangeWindow window, string id)
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(candidate => AutomationProperties.GetAutomationId(candidate) == id);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
