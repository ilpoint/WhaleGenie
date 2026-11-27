using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using Xunit;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// A window rule's identity. The comparison is written into the project under one canonical
/// spelling; an entry saved by the older trigger field used a capitalised one, and the two must
/// still be the same group or the panel's place is left behind when the shared form saves it.
/// </summary>
public class WindowRuleGroupTests
{
    [Fact]
    public void One_rule_written_in_either_spelling_is_one_group()
    {
        var older = new WindowRule(Title: "Game", How: "TitleContains");
        var shared = new WindowRule(Title: "Game", How: WindowHow.TitleContains);

        Assert.Equal(ProjectTriggers.GroupKey(older), ProjectTriggers.GroupKey(shared));
    }

    [Fact]
    public void A_layout_saved_under_the_older_spelling_is_found_by_the_shared_form()
    {
        var triggers = new ProjectTriggers();
        triggers.StoreLayout(new WindowRule(Title: "Game", How: "TitleContains"), 30, 70);

        var layout = triggers.LayoutFor(new WindowRule(Title: "Game",
            How: WindowHow.TitleContains));

        Assert.Equal((30, 70), (layout.OffsetX, layout.OffsetY));
    }

    /// <summary>
    /// A file can hold the same rule twice — once under the older spelling and once from the
    /// shared form. The newer row is the place the panel was actually left at, and the next save
    /// writes one row rather than adding a third.
    /// </summary>
    [Fact]
    public void A_duplicated_layout_uses_the_newest_row_and_is_collapsed_on_the_next_save()
    {
        var triggers = new ProjectTriggers();
        var rule = new WindowRule(Title: "Game", How: WindowHow.TitleContains);
        triggers.InterfaceLayouts.Add(new InterfaceLayout(
            new WindowRule(Title: "Game", How: "TitleContains"), 16, 64, "Ctrl+F8"));
        triggers.InterfaceLayouts.Add(new InterfaceLayout(rule, 200, 90));

        var layout = triggers.LayoutFor(rule);
        Assert.Equal((200, 90), (layout.OffsetX, layout.OffsetY));
        // The newest row has no key, but the older one still does: the person never asked for the
        // key to be dropped, so the effective layout keeps it.
        Assert.Equal("Ctrl+F8", layout.ToggleHotkey);

        triggers.StoreLayout(rule, 30, 70);

        var stored = Assert.Single(triggers.InterfaceLayouts);
        Assert.Equal((30, 70), (stored.OffsetX, stored.OffsetY));
        Assert.Equal("Ctrl+F8", stored.ToggleHotkey);
    }
}
