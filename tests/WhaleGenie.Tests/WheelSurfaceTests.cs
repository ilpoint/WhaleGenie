using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>The wheel as the page receives it: the ring names and the labels that carry a macro.</summary>
public class WheelSurfaceTests
{
    [Fact]
    public void A_filled_position_reaches_the_page_with_its_ring_and_label()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        project.Triggers.Wheel.Global.Inner[0].Macro = "one";

        var snapshot = new WheelSurface(project).Snapshot();

        Assert.Equal(420, snapshot.Diameter);
        Assert.Equal(2, snapshot.Rings);
        var slot = Assert.Single(snapshot.Slots, candidate => candidate.Ring == "inner" && candidate.Index == 0);
        Assert.Equal("one", slot.Label);
    }

    [Fact]
    public void An_aim_frame_carries_the_ring_and_the_position()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        project.Triggers.Wheel.Global.Outer[3].Macro = "one";

        var snapshot = new WheelSurface(project).Snapshot(new SurfaceWheelAim("outer", 3));

        Assert.NotNull(snapshot.Aim);
        Assert.Equal("outer", snapshot.Aim.Ring);
        Assert.Equal(3, snapshot.Aim.Index);
    }

    [Fact]
    public void An_empty_wheel_has_no_positions()
    {
        var snapshot = new WheelSurface(new ProjectWorkspace()).Snapshot();

        Assert.Empty(snapshot.Slots);
    }
}
