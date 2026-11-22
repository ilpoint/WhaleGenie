using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Editing;

namespace WhaleGenie.Tests;

/// <summary>
/// What the project does with the macro an editor hands back. The editor can close after the list
/// has changed underneath it, and a save can arrive with the same macro twice; both answers belong
/// here, not in the window that happens to be open at the time.
/// </summary>
public class ProjectMacroCommitTests
{
    [Fact]
    public void A_macro_that_left_the_list_is_not_written_back()
    {
        var target = new MacroItem { Name = "login" };
        var (project, commit, _) = SetUp(target);
        project.RemoveMacro(target);
        project.IsDirty = false;

        Assert.False(commit.Apply(new MacroDraft(target, [], []), []));

        Assert.Empty(project.Macros);
        Assert.False(project.IsDirty, "a refused macro marked the project changed");
    }

    [Fact]
    public void An_unchanged_macro_is_not_a_project_change()
    {
        var target = new MacroItem { Name = "login" };
        var (project, commit, stopped) = SetUp(target);
        project.IsDirty = false;

        Assert.True(commit.Apply(new MacroDraft(target,
            project.Triggers.ForMacro(target.Name), project.Triggers.InterfaceLayouts), []));

        Assert.Same(target, Assert.Single(project.Macros));
        Assert.Empty(stopped);
        Assert.False(project.IsDirty, "the same macro was written again");
    }

    [Fact]
    public void A_changed_macro_replaces_the_one_that_was_there()
    {
        var target = new MacroItem { Name = "login" };
        var (project, commit, stopped) = SetUp(target);
        var edited = new MacroItem { Name = "login-renamed" };
        project.IsDirty = false;

        Assert.True(commit.Apply(new MacroDraft(edited, [], []), []));

        Assert.Equal(["login"], stopped);
        Assert.Same(edited, Assert.Single(project.Macros));
        Assert.Same(edited, commit.Target);
        Assert.True(project.IsDirty);
    }

    [Fact]
    public void A_macro_created_from_the_menu_view_is_placed_there()
    {
        var (project, commit, _) = SetUp(view: MacroView.Menu);
        var macro = new MacroItem { Name = "from-menu" };
        project.IsDirty = false;

        Assert.True(commit.Apply(new MacroDraft(macro, [], []), []));

        Assert.Same(macro, commit.Target);
        Assert.Same(macro, Assert.Single(project.Macros));
        var cell = Assert.Single(project.Triggers.Entries);
        Assert.Equal(TriggerKind.Menu, cell.Kind);
        Assert.Equal("from-menu", cell.MacroName);
        Assert.True(project.IsDirty);
    }

    [Fact]
    public void A_macro_lifted_during_the_session_joins_only_once()
    {
        var (project, commit, _) = SetUp();
        var child = new MacroItem { Name = "child" };
        var parent = new MacroItem { Name = "parent" };
        var draft = new MacroDraft(parent, [], []);

        Assert.True(commit.Apply(draft, [child]));
        Assert.True(commit.Apply(draft, [child]));

        Assert.Equal(2, project.Macros.Count);
        Assert.Single(project.Macros, macro => macro.Name == "child");
        Assert.Single(project.Macros, macro => macro.Name == "parent");
    }

    [Fact]
    public void Interface_layouts_from_the_editor_are_stored_with_the_project()
    {
        var rule = new WindowRule(Title: "Game");
        var layout = new InterfaceLayout(rule, 10, 20, "Ctrl+Alt+U");
        var macro = new MacroItem { Name = "with-surface" };
        var (project, commit, _) = SetUp();

        // The panel's place belongs to the project already; the editor's draft only carries the
        // hotkey it was shown with, and storing that must not move the panel back to its default.
        project.Triggers.StoreLayout(rule, 10, 20);

        Assert.True(commit.Apply(new MacroDraft(macro, [], [layout]), []));

        var stored = Assert.Single(project.Triggers.InterfaceLayouts);
        Assert.Equal("Ctrl+Alt+U", stored.ToggleHotkey);
        Assert.Equal(10, stored.OffsetX);
        Assert.Equal(20, stored.OffsetY);
    }

    [Fact]
    public void A_changed_trigger_entry_makes_the_macro_worth_replacing()
    {
        var target = new MacroItem { Name = "login" };
        var (project, commit, stopped) = SetUp(target);
        var entry = new TriggerEntry { MacroName = target.Name, Spec = new KeySpec("F8") };
        project.IsDirty = false;

        Assert.True(commit.Apply(new MacroDraft(target, [entry], []), []));

        Assert.Equal(["login"], stopped);
        Assert.True(project.IsDirty);
        Assert.Equal("F8", Assert.IsType<KeySpec>(Assert.Single(project.Triggers.Entries).Spec).Binding);
    }

    private static (MainViewModel Project, ProjectMacroCommit Commit, List<string> Stopped) SetUp(
        MacroItem? target = null, MacroView view = MacroView.All)
    {
        var project = new MainViewModel();
        if (target is not null)
        {
            project.AddMacro(target);
        }

        var stopped = new List<string>();
        var commit = new ProjectMacroCommit(project.Workspace, target, view,
            macro => stopped.Add(macro.Name), () => { }, () => { });
        return (project, commit, stopped);
    }
}
