namespace WhaleGenie.Tests;

/// <summary>
/// The mark every window carries in its title bar. It used to be the three letters of the old
/// name in an orange square; now it is the whale the program is named after.
/// </summary>
public class TitleBarMarkTests
{
    [Fact]
    public void Every_window_with_a_title_bar_shows_the_whale()
    {
        var views = Path.Combine(Repository(), "WhaleGenie", "Views");
        var missing = new List<string>();

        foreach (var window in Directory.EnumerateFiles(views, "*.axaml").Order())
        {
            var markup = File.ReadAllText(window);

            // The row that carries the close button is the title bar, so that is where the mark
            // belongs — every one of them, not only the main window.
            if (!markup.Contains("TitleBarButton", StringComparison.Ordinal))
            {
                continue;
            }

            if (!markup.Contains("/Assets/WhaleGenie.png", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(window));
            }

            // The letters are gone for good rather than left in the windows nobody looked at.
            Assert.DoesNotContain("VIK", markup, StringComparison.Ordinal);
        }

        Assert.Empty(missing);
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
