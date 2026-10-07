using Viktor.Localization;
using Viktor.ViewModels;

namespace Viktor.Tests;

/// <summary>
/// The window title. It used to read "(Admin)" whatever the process had been started with,
/// which claimed a right the program did not hold and told the user nothing.
/// </summary>
public class MainTitleTests
{
    [Fact]
    public void The_name_alone_does_not_claim_admin()
    {
        Assert.DoesNotContain("Admin", Strings.English["Main.Title"]);
        Assert.DoesNotContain("管理员", Strings.Chinese["Main.Title"]);
    }

    [Fact]
    public void The_title_carries_only_the_badge_it_is_entitled_to()
    {
        var title = new MainViewModel().WindowTitle;
        var name = Strings.Get("Main.Title");

        Assert.StartsWith(name, title);

        // Whatever follows the name is the admin badge and nothing else: either the process
        // really is elevated, or there is no suffix at all.
        var suffix = title[name.Length..];
        Assert.True(suffix.Length == 0 || suffix == Strings.Get("Main.AdminBadge"),
            $"unexpected suffix \"{suffix}\"");
    }

    [Fact]
    public void The_window_shows_the_title_the_view_model_works_out()
    {
        // The title was the designer's own word in the markup, so the name never followed the
        // language and the admin badge never appeared at all — the work above was being done and
        // nobody was reading it.
        var markup = File.ReadAllText(Path.Combine(Repository(), "Viktor", "Views", "MainWindow.axaml"));

        Assert.Contains("Title=\"{Binding WindowTitle}\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_window_is_titled_from_the_string_table()
    {
        // A window whose only title is the text in its markup shows that text whatever language the
        // program is in, and keeps showing it after the program has been given another name. Each
        // window either binds the title to what its view model says or sets it from the table in
        // code, so every markup title here has to be backed by one of the two.
        var views = Path.Combine(Repository(), "Viktor", "Views");
        var unwired = new List<string>();

        foreach (var markup in Directory.EnumerateFiles(views, "*.axaml").Order())
        {
            var xaml = File.ReadAllText(markup);
            if (!xaml.Contains("Title=\"", StringComparison.Ordinal))
            {
                continue;
            }

            var code = File.Exists(markup + ".cs") ? File.ReadAllText(markup + ".cs") : string.Empty;
            if (!code.Contains("Title = ", StringComparison.Ordinal))
            {
                unwired.Add(Path.GetFileName(markup));
            }
        }

        Assert.Empty(unwired);
    }

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "Viktor.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("Viktor.slnx was not found above the test binaries.");
    }
}
