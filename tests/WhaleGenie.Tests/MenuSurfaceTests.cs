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
    [Fact]
    public void A_common_page_carries_its_cells_and_the_icon_as_a_name()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        var page = project.Triggers.CommonMenuPages()[0];
        project.Triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "one",
            Spec = new MenuSpec(page.Id, 1, 2, new MenuIconReference(MenuIconKind.Builtin, "Play")),
        });

        var snapshot = new MenuSurface(project, NullDeviceLayer.Instance.Windows).Snapshot();

        var pageSnapshot = Assert.Single(snapshot.Common, candidate => candidate.PageId == page.Id);
        var cell = Assert.Single(pageSnapshot.Cells);
        Assert.Equal(1, cell.Row);
        Assert.Equal(2, cell.Column);
        Assert.Equal("one", cell.MacroName);
        Assert.Equal("builtin", cell.IconKind);
        Assert.Equal("Play", cell.IconValue);
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

        var snapshot = new MenuSurface(project, NullDeviceLayer.Instance.Windows).Snapshot();

        Assert.All(snapshot.Common, candidate => Assert.DoesNotContain(candidate.Cells,
            cell => cell.MacroName == "one"));
    }

    [Fact]
    public void Without_a_foreground_window_the_context_pages_are_the_default_ones()
    {
        var project = new ProjectWorkspace();
        var snapshot = new MenuSurface(project, NullDeviceLayer.Instance.Windows).Snapshot();

        Assert.NotEmpty(snapshot.Common);
        Assert.NotEmpty(snapshot.Context);
        Assert.All(snapshot.Context, page => Assert.True(page.IsDefault));
        Assert.All(snapshot.Context, page => Assert.Empty(page.Cells));
    }
}
