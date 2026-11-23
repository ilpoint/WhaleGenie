using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>The interface panel as the page receives it: the members that can actually show.</summary>
public class PanelSurfaceTests
{
    [Fact]
    public void A_group_lists_its_visible_members()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        project.Triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "one",
            Spec = new InterfaceSpec(WindowRule.Empty),
        });

        var key = project.Triggers.InterfaceGroups()
            .First(group => group.Entries.Any()).Key;
        var snapshot = Surface(project).Snapshot(key, horizontal: false, collapsed: false);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal("one", item.Label);
        Assert.Equal("visible", item.State);
    }

    [Fact]
    public void Without_a_group_the_panel_is_empty_but_titled()
    {
        var snapshot = Surface(new ProjectWorkspace())
            .Snapshot("missing", horizontal: false, collapsed: false);

        Assert.Empty(snapshot.Items);
        Assert.Equal("界面宏", snapshot.Title);
    }

    [Fact]
    public void Running_a_name_that_is_not_there_does_nothing()
        => Assert.False(Surface(new ProjectWorkspace()).Run("missing"));

    private static PanelSurface Surface(ProjectWorkspace project)
        => new(project, new TriggerRuntime(NullDeviceLayer.Instance, project.Variables));
}
