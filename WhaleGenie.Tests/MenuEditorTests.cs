using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

public class MenuEditorTests
{
    [Fact]
    public void A_project_starts_with_one_common_page_and_one_default_context_page()
    {
        var triggers = new ProjectTriggers();

        Assert.Single(triggers.CommonMenuPages());
        Assert.True(triggers.DefaultContextMenuPage().IsDefault);
        Assert.Empty(triggers.ContextMenuPages());
        Assert.Equal(4, ProjectTriggers.MenuColumns);
        Assert.Equal(6, ProjectTriggers.MenuRows);
    }

    [Fact]
    public void Common_pages_stop_at_eight_and_removal_takes_their_cells()
    {
        var triggers = new ProjectTriggers();
        while (triggers.CommonMenuPages().Count < ProjectTriggers.MaxCommonMenuPages)
        {
            triggers.AddCommonMenuPage();
        }

        var ninth = triggers.AddCommonMenuPage();
        Assert.Equal(ProjectTriggers.MaxCommonMenuPages, triggers.CommonMenuPages().Count);
        Assert.Same(triggers.CommonMenuPages()[^1], ninth);

        var page = triggers.CommonMenuPages()[1];
        triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Menu,
            MacroName = "one",
            MenuPageId = page.Id,
            MenuRow = 0,
            MenuColumn = 0,
        });
        triggers.RemoveMenuPage(page.Id);

        Assert.DoesNotContain(triggers.MenuPages, candidate => candidate.Id == page.Id);
        Assert.DoesNotContain(triggers.Entries, entry => entry.MenuPageId == page.Id);
    }

    [Theory]
    [InlineData("F11", true)]
    [InlineData("Ctrl+Alt+M", true)]
    [InlineData("Mouse 4", true)]
    [InlineData("Mouse 5", true)]
    [InlineData("Mouse Middle", false)]
    [InlineData("Mouse Left", false)]
    [InlineData("Wheel Up", false)]
    public void Menu_launchers_accept_keyboard_and_side_buttons_only(string binding, bool allowed)
        => Assert.Equal(allowed, ProjectTriggers.IsAllowedMenuLauncher(binding));

    [Fact]
    public void The_whole_lower_region_uses_the_first_matching_rule_or_the_default()
    {
        var windows = new WindowsForChecks()
            .Add(1, "Notepad", "notepad", "Notepad")
            .Add(2, "Browser", "chrome", "Chrome_WidgetWin_1");
        var macros = new[]
        {
            new MacroItem { Name = "common" },
            new MacroItem { Name = "notepad" },
            new MacroItem { Name = "default" },
        };
        var triggers = new ProjectTriggers();
        var common = triggers.CommonMenuPages()[0];
        var notepad = triggers.AddContextMenuPage(new WindowRule(Title: "Notepad"));
        var fallback = triggers.DefaultContextMenuPage();
        triggers.Entries.Add(Menu(common.Id, macros[0].Name));
        triggers.Entries.Add(Menu(notepad.Id, macros[1].Name));
        triggers.Entries.Add(Menu(fallback.Id, macros[2].Name));

        var layout = triggers.BuildMenuLayout(windows.Open[0], windows, macros);

        Assert.Equal("notepad", Assert.Single(layout.ContextPages[0].Cells).Macro.Name);
        var noMatch = triggers.BuildMenuLayout(null, windows, macros);
        Assert.True(noMatch.ContextPages[0].IsDefault);
        Assert.Equal("default", Assert.Single(noMatch.ContextPages[0].Cells).Macro.Name);
    }

    [Fact]
    public void Menu_problems_block_missing_macros_and_duplicate_cells()
    {
        var triggers = new ProjectTriggers();
        var page = triggers.CommonMenuPages()[0];
        triggers.Entries.Add(Menu(page.Id, "missing"));
        triggers.Entries.Add(Menu(page.Id, "missing"));

        var problems = triggers.MenuProblems([]);

        Assert.Contains(problems, problem => problem.Contains("missing", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("1", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_window_rule_can_have_multiple_independent_pages()
    {
        var windows = new WindowsForChecks().Add(1, "Notepad", "notepad", "Notepad");
        var macro = new MacroItem { Name = "one" };
        var triggers = new ProjectTriggers();
        var rule = new WindowRule(Title: "Notepad");
        var first = triggers.AddContextMenuPage(rule);
        var second = triggers.AddContextMenuPage(rule);
        triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Menu,
            MacroName = macro.Name,
            MenuPageId = first.Id,
            MenuRow = 0,
            MenuColumn = 0,
        });
        triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Menu,
            MacroName = macro.Name,
            MenuPageId = second.Id,
            MenuRow = 0,
            MenuColumn = 1,
        });

        var layout = triggers.BuildMenuLayout(windows.Open[0], windows, [macro]);

        Assert.Equal(2, layout.ContextPages.Count);
        Assert.Equal(2, Assert.Single(triggers.ContextRuleGroups()).Pages.Count);
    }

    [Fact]
    public void The_default_context_can_also_have_multiple_pages()
    {
        var triggers = new ProjectTriggers();
        triggers.AddDefaultContextPage();

        var layout = triggers.BuildMenuLayout(null, new WindowsForChecks(),
            [new MacroItem { Name = "unused" }]);

        Assert.Equal(2, layout.ContextPages.Count);
        Assert.All(layout.ContextPages, page => Assert.True(page.IsDefault));
    }

    private static TriggerEntry Menu(string page, string macro) => new()
    {
        Kind = TriggerKind.Menu,
        MacroName = macro,
        MenuPageId = page,
        MenuRow = 0,
        MenuColumn = 0,
    };
}
