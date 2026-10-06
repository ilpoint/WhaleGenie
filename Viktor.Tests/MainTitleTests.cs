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
}
