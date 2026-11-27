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
    public void A_group_lists_its_members_in_the_project_macro_order()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "一" });
        project.AddMacro(new MacroItem { Name = "二" });
        project.AddMacro(new MacroItem { Name = "三" });
        foreach (var name in new[] { "三", "二", "一" })
        {
            project.Triggers.Entries.Add(new TriggerEntry
            {
                MacroName = name,
                Spec = new InterfaceSpec(WindowRule.Empty),
            });
        }

        var key = project.Triggers.InterfaceGroups().First().Key;
        var snapshot = Surface(project).Snapshot(key, horizontal: false, collapsed: false);

        Assert.Equal(["一", "二", "三"], snapshot.Items.Select(item => item.Label));
    }

    [Fact]
    public void The_member_order_follows_the_macro_list_and_enabled_first_setting()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "A", IsEnabled = false });
        project.AddMacro(new MacroItem { Name = "B" });
        project.AddMacro(new MacroItem { Name = "C", IsEnabled = false });
        project.AddMacro(new MacroItem { Name = "D" });
        foreach (var name in new[] { "D", "A", "C", "B" })
        {
            project.Triggers.Entries.Add(new TriggerEntry
            {
                MacroName = name,
                Spec = new InterfaceSpec(WindowRule.Empty),
            });
        }

        var group = project.Triggers.InterfaceGroups().First();
        var projectOrder = InterfaceMemberResolver.Resolve(group, [.. project.Macros]);
        var enabledFirst = InterfaceMemberResolver.Resolve(group, [.. project.Macros], true);

        Assert.Equal(["A", "B", "C", "D"], projectOrder.Select(member => member.Name));
        Assert.Equal(["B", "D", "A", "C"], enabledFirst.Select(member => member.Name));
    }

    [Fact]
    public void Without_a_group_the_panel_is_empty_but_titled()
    {
        var snapshot = Surface(new ProjectWorkspace())
            .Snapshot("missing", horizontal: false, collapsed: false);

        Assert.Empty(snapshot.Items);
        Assert.Equal("界面宏", snapshot.Title);
    }

    /// <summary>The host owns the window's size, so folding a group has to tell it the shape moved.</summary>
    [Fact]
    public void Folding_a_group_announces_the_new_shape()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        project.Triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "one",
            Spec = new InterfaceSpec(WindowRule.Empty),
        });
        var surface = Surface(project);
        var key = project.Triggers.InterfaceGroups().First().Key;
        var announced = 0;
        surface.ViewChanged += () => announced++;

        surface.View(key, collapsed: true, horizontal: null);

        Assert.Equal(1, announced);
    }

    [Fact]
    public void A_group_remembers_how_it_was_folded_and_turned()
    {
        var project = new ProjectWorkspace();
        var key = project.Triggers.InterfaceGroups()
            .Select(group => group.Key)
            .FirstOrDefault() ?? "global";
        var surface = Surface(project);

        var folded = surface.View(key, collapsed: true, horizontal: null);
        Assert.True(folded.Collapsed);

        var turned = surface.View(key, collapsed: null, horizontal: true);
        Assert.True(turned.Collapsed);
        Assert.True(turned.Horizontal);

        var again = surface.Snapshot(key, horizontal: false, collapsed: false);
        Assert.True(again.Collapsed);
        Assert.True(again.Horizontal);
    }

    [Fact]
    public void Running_a_name_that_is_not_there_does_nothing()
        => Assert.False(Surface(new ProjectWorkspace()).Run("missing"));

    private static PanelSurface Surface(ProjectWorkspace project)
        => new(project, new TriggerRuntime(NullDeviceLayer.Instance, project.Variables));
}
