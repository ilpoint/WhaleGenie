namespace WhaleGenie.Tests;

/// <summary>
/// The two buttons along the bottom of the main window are a pair — start/stop the macros, and add
/// a new one — so their badges are drawn the same way. The new-macro one used to be a filled white
/// square with a plus in it, which read as a different kind of control from the outlined one beside
/// it. The markup is what says so, so that is what is read here.
/// </summary>
public class MainWindowIconTests
{
    [Fact]
    public void The_add_button_wears_the_same_badge_as_the_run_button()
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", "MainWindow.axaml"));

        var add = Block(xaml, "Name=\"AddMacroButton\"");
        Assert.Contains("PathIcon", add, StringComparison.Ordinal);

        // The outlined badge: a white border with a white glyph, rather than a filled white box.
        Assert.Contains("BorderBrush=\"White\" BorderThickness=\"2\"", add, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"+\"", add, StringComparison.Ordinal);
    }

    /// <summary>The markup of the button whose definition starts at the given text.</summary>
    private static string Block(string xaml, string marker)
    {
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the main window no longer has {marker}");

        var end = xaml.IndexOf("</Button>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"{marker} is not closed");
        return xaml[start..end];
    }

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
