namespace WhaleGenie.Tests;

/// <summary>
/// Dragging a window by its title bar. Every window here draws its own title bar — there are no
/// system decorations left to grab — so each one has to say what a left press on that bar means,
/// and a window that forgets to cannot be moved at all.
/// </summary>
public class TitleBarDragTests
{
    [Fact]
    public void Every_window_with_a_title_bar_is_dragged_by_it()
    {
        var views = Path.Combine(Repository(), "WhaleGenie", "Views");
        var missing = new List<string>();

        foreach (var window in Directory.EnumerateFiles(views, "*.axaml").Order())
        {
            // The row carrying the buttons in the corner is the title bar, which is the row the
            // mark is looked for in as well. The marker is the library's `Chrome` family — one word
            // that covers `Chrome`, `Chrome.Icon`, `Chrome.Icon.Wide` and `Chrome.Labeled`, so a new
            // member of the family does not have to be added here.
            if (!File.ReadAllText(window).Contains("Classes=\"Chrome", StringComparison.Ordinal))
            {
                continue;
            }

            var code = window + ".cs";
            if (!File.Exists(code)
                || !File.ReadAllText(code).Contains("BeginMoveDrag", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(window));
            }
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
