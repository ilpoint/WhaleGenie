using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The menu projected for a page: which cells a project puts where, with the icon reduced to the
/// name the wire carries, and the edits the page can make without a native picker.
/// </summary>
public class MenuSurfaceTests
{
    private static MenuSurface Surface(ProjectWorkspace project)
        => new(project, NullDeviceLayer.Instance.Windows,
            new TriggerRuntime(NullDeviceLayer.Instance, project.Variables));

    [Fact]
    public void A_common_page_carries_its_cells_and_the_icon_as_a_name()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one", Description = "the first one" });
        var page = project.Triggers.CommonMenuPages()[0];
        project.Triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "one",
            Spec = new MenuSpec(page.Id, 1, 2, new MenuIconReference(MenuIconKind.Builtin, "Play")),
        });

        var snapshot = Surface(project).Snapshot();

        var pageSnapshot = Assert.Single(snapshot.Common, candidate => candidate.PageId == page.Id);
        var cell = Assert.Single(pageSnapshot.Cells);
        Assert.Equal(1, cell.Row);
        Assert.Equal(2, cell.Column);
        Assert.Equal("one", cell.MacroName);
        Assert.Equal("builtin", cell.IconKind);
        Assert.Equal("Play", cell.IconValue);
        Assert.Equal("the first one", cell.Description);
        Assert.False(string.IsNullOrEmpty(cell.EntryId));
    }

    [Fact]
    public void The_chooser_lists_enabled_macros_in_project_order()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        project.AddMacro(new MacroItem { Name = "two", IsEnabled = false });
        project.AddMacro(new MacroItem { Name = "three" });

        var choices = Surface(project).MacroChoices();

        Assert.Equal(["one", "three"], choices.Select(choice => choice.Name));
        Assert.All(choices, choice => Assert.False(string.IsNullOrEmpty(choice.Preview)));
    }

    [Fact]
    public void An_empty_cell_gets_the_named_macro()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        var page = project.Triggers.CommonMenuPages()[0];

        var snapshot = Surface(project).AssignCell(page.Id, 0, 0, "one");

        var cell = Assert.Single(snapshot.Common.Single(candidate => candidate.PageId == page.Id).Cells);
        Assert.Equal("one", cell.MacroName);
    }

    [Fact]
    public void A_disabled_or_unknown_macro_is_not_assigned()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "off", IsEnabled = false });
        var page = project.Triggers.CommonMenuPages()[0];
        var surface = Surface(project);

        var disabled = surface.AssignCell(page.Id, 0, 0, "off");
        var unknown = surface.AssignCell(page.Id, 0, 0, "missing");

        Assert.Empty(disabled.Common.SelectMany(candidate => candidate.Cells));
        Assert.Empty(unknown.Common.SelectMany(candidate => candidate.Cells));
    }

    [Fact]
    public void A_disabled_macro_is_not_put_on_the_menu()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one", IsEnabled = false });
        var page = project.Triggers.CommonMenuPages()[0];
        project.Triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "one",
            Spec = new MenuSpec(page.Id, 0, 0),
        });

        var snapshot = Surface(project).Snapshot();

        Assert.All(snapshot.Common, candidate => Assert.DoesNotContain(candidate.Cells,
            cell => cell.MacroName == "one"));
    }

    [Fact]
    public void Without_a_foreground_window_the_context_pages_are_the_default_ones()
    {
        var project = new ProjectWorkspace();
        var snapshot = Surface(project).Snapshot();

        Assert.NotEmpty(snapshot.Common);
        Assert.NotEmpty(snapshot.Context);
        Assert.All(snapshot.Context, page => Assert.True(page.IsDefault));
        Assert.All(snapshot.Context, page => Assert.Empty(page.Cells));
    }

    [Fact]
    public void A_page_can_be_added_and_taken_away_again()
    {
        var project = new ProjectWorkspace();
        var surface = Surface(project);
        var before = surface.Snapshot().Common.Count;

        var added = surface.AddPage(null);
        Assert.Equal(before + 1, added.Common.Count);

        var removed = surface.RemovePage(added.Common[^1].PageId);
        Assert.Equal(before, removed.Common.Count);
    }

    [Fact]
    public void A_cell_moves_to_the_place_it_is_dropped_on()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        var first = project.Triggers.CommonMenuPages()[0];
        var entry = new TriggerEntry { MacroName = "one", Spec = new MenuSpec(first.Id, 0, 0) };
        project.Triggers.Entries.Add(entry);

        var moved = Surface(project).MoveCell(entry.Id, first.Id, 2, 3);

        var cell = Assert.Single(moved.Common.SelectMany(page => page.Cells));
        Assert.Equal(2, cell.Row);
        Assert.Equal(3, cell.Column);
    }

    [Fact]
    public void Edit_and_debug_ask_the_shell_for_the_macro()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        var page = project.Triggers.CommonMenuPages()[0];
        var entry = new TriggerEntry { MacroName = "one", Spec = new MenuSpec(page.Id, 0, 0) };
        project.Triggers.Entries.Add(entry);
        var actions = new RecordingActions();
        var surface = Surface(project);
        surface.Actions = actions;

        Assert.True(surface.Edit(entry.Id));
        Assert.True(surface.Debug(entry.Id));
        Assert.Equal(["one", "one"], actions.Opened);
    }

    [Fact]
    public void Setting_an_icon_writes_it_to_the_entry()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        var page = project.Triggers.CommonMenuPages()[0];
        var entry = new TriggerEntry { MacroName = "one", Spec = new MenuSpec(page.Id, 0, 0) };
        project.Triggers.Entries.Add(entry);

        var snapshot = Surface(project).SetIcon(entry.Id, "image", @"C:\icons\one.png");

        var cell = Assert.Single(snapshot.Common.SelectMany(candidate => candidate.Cells));
        Assert.Equal("image", cell.IconKind);
        Assert.EndsWith("one.png", cell.IconValue);
    }

    [Fact]
    public void An_unknown_entry_keeps_the_project_untouched()
    {
        var project = new ProjectWorkspace();
        var before = Surface(project).Snapshot();

        var after = Surface(project).SetIcon("nope", "builtin", "Play");

        Assert.Empty(after.Common.SelectMany(candidate => candidate.Cells));
        Assert.Empty(before.Common.SelectMany(candidate => candidate.Cells));
    }

    [Fact]
    public void Replacing_a_cell_keeps_its_place_and_takes_the_new_macro()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        project.AddMacro(new MacroItem { Name = "two" });
        var page = project.Triggers.CommonMenuPages()[0];
        var entry = new TriggerEntry { MacroName = "one", Spec = new MenuSpec(page.Id, 1, 2) };
        project.Triggers.Entries.Add(entry);

        var snapshot = Surface(project).AssignCell(page.Id, 1, 2, "two");

        var cell = Assert.Single(snapshot.Common.SelectMany(candidate => candidate.Cells));
        Assert.Equal("two", cell.MacroName);
        Assert.Equal(1, cell.Row);
        Assert.Equal(2, cell.Column);
    }

    private sealed class RecordingActions : IMenuSurfaceActions
    {
        public List<string> Opened { get; } = [];

        public bool SettingsOpened { get; private set; }

        public void OpenEditor(string macroName) => Opened.Add(macroName);

        public void OpenRun(string macroName) => Opened.Add(macroName);

        public void OpenSettings() => SettingsOpened = true;
    }
}
