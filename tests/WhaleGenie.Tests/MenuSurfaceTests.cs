using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The menu projected for a page: which cells a project puts where, with the icon reduced to the
/// name the wire carries. Paging and search are not here — they are the page's own state.
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
    public async Task Choosing_an_icon_writes_it_to_the_entry()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        var page = project.Triggers.CommonMenuPages()[0];
        var entry = new TriggerEntry { MacroName = "one", Spec = new MenuSpec(page.Id, 0, 0) };
        project.Triggers.Entries.Add(entry);
        var surface = Surface(project);
        surface.Actions = new RecordingActions { Icon = ("builtin", "Play") };

        var snapshot = await surface.ChooseIconAsync(entry.Id);

        var cell = Assert.Single(snapshot.Common.SelectMany(candidate => candidate.Cells));
        Assert.Equal("Play", cell.IconValue);
        Assert.Equal("builtin", cell.IconKind);
    }

    [Fact]
    public async Task Replacing_a_cell_keeps_its_place_and_takes_the_new_macro()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        project.AddMacro(new MacroItem { Name = "two" });
        var page = project.Triggers.CommonMenuPages()[0];
        var entry = new TriggerEntry { MacroName = "one", Spec = new MenuSpec(page.Id, 1, 2) };
        project.Triggers.Entries.Add(entry);
        var surface = Surface(project);
        surface.Actions = new RecordingActions { Macro = "two" };

        var snapshot = await surface.ReplaceAsync(entry.Id);

        var cell = Assert.Single(snapshot.Common.SelectMany(candidate => candidate.Cells));
        Assert.Equal("two", cell.MacroName);
        Assert.Equal(1, cell.Row);
        Assert.Equal(2, cell.Column);
    }

    private sealed class RecordingActions : IMenuSurfaceActions
    {
        public List<string> Opened { get; } = [];

        public (string Kind, string Value)? Icon { get; init; }

        public void OpenEditor(string macroName) => Opened.Add(macroName);

        public void OpenRun(string macroName) => Opened.Add(macroName);

        public bool SettingsOpened { get; private set; }

        public void OpenSettings() => SettingsOpened = true;

        public string? Macro { get; init; }

        public Task<(string Kind, string Value)?> PickIconAsync()
            => Task.FromResult(Icon);

        public Task<string?> PickMacroAsync() => Task.FromResult(Macro);
    }
}
