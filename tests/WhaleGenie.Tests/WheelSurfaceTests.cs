using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>The wheel as the page receives it: the circles it draws and the tones of its positions.</summary>
public class WheelSurfaceTests
{
    [Fact]
    public void A_filled_position_reaches_the_page_with_its_ring_and_label()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        project.Triggers.Wheel.Global.Inner[0].Macro = "one";

        var snapshot = Frame(project, new WheelOptions());

        Assert.Equal(420, snapshot.Diameter);
        Assert.Equal(840, snapshot.CanvasSize);
        var slot = Assert.Single(snapshot.Slots, candidate => candidate.Ring == "inner" && candidate.Index == 0);
        Assert.Equal("one", slot.Label);
        Assert.Equal("global", slot.Tone);
    }

    [Fact]
    public void An_empty_outer_circle_is_hidden_when_the_setting_asks_for_it()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        project.Triggers.Wheel.Global.Inner[0].Macro = "one";

        var one = Frame(project, new WheelOptions { HideEmptyOuter = true });
        Assert.Equal("inner", Assert.Single(one.Rings).Ring);

        var two = Frame(project, new WheelOptions { HideEmptyOuter = false });
        Assert.Equal(new[] { "inner", "outer" }, two.Rings.Select(ring => ring.Ring));
        Assert.All(two.Rings, ring => Assert.Equal(8, ring.Count));
    }

    [Fact]
    public void A_wide_outer_circle_has_sixteen_positions()
    {
        var project = new ProjectWorkspace();
        var options = new WheelOptions { WideOuter = true, HideEmptyOuter = false };

        var snapshot = Frame(project, options);

        Assert.Equal(16, snapshot.Rings.Single(ring => ring.Ring == "outer").Count);
    }

    [Fact]
    public void A_third_circle_with_a_macro_is_drawn()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        project.Triggers.Wheel.Global.Third[0].Macro = "one";
        var options = new WheelOptions { ThirdCapacity = 8, HideEmptyOuter = true };

        var snapshot = Frame(project, options);

        Assert.Equal(3, snapshot.Rings.Count);
        Assert.Equal(8, snapshot.Rings[2].Count);
        Assert.Equal("third", snapshot.Rings[2].Ring);
    }

    [Fact]
    public void A_program_position_is_marked_as_overridden()
    {
        var macro = new MacroItem { Name = "one" };
        var layout = new WheelRuntimeLayout(
            [new WheelRuntimeSlot(new WheelSlotRef(WheelRing.Inner, 0), "one", macro, null, true)],
            "Scene", WheelSource.Merged, WideOuter: false);
        var snapshot = WheelSurface.Of(layout, new WheelOptions { HideEmptyOuter = false });

        var slot = Assert.Single(snapshot.Slots, candidate => candidate.Ring == "inner" && candidate.Index == 0);
        Assert.Equal("overridden", slot.Tone);
    }

    [Fact]
    public void The_centre_reads_close_when_the_pointer_is_inside_it()
    {
        var snapshot = Frame(new ProjectWorkspace(), new WheelOptions());

        Assert.Equal(Strings.Get("Wheel.Close"), snapshot.CenterText);
    }

    [Fact]
    public void The_centre_can_name_the_aimed_macro()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        project.Triggers.Wheel.Global.Outer[0].Macro = "one";

        var snapshot = Frame(project, new WheelOptions { CenterName = true, HideEmptyOuter = false },
            new SurfaceWheelAim("outer", 0, 40, -20));

        Assert.Equal("one", snapshot.CenterText);
        Assert.Equal(40, snapshot.Aim?.X);
        Assert.Equal(-20, snapshot.Aim?.Y);
    }

    private static SurfaceWheelSnapshot Frame(ProjectWorkspace project, WheelOptions options,
        SurfaceWheelAim? aim = null)
    {
        var layout = project.Triggers.Wheel.Resolve(null, WheelSource.Global, options.WideOuter,
            [.. project.Macros], options.ThirdCapacity, options.HideEmptyOuter);
        return WheelSurface.Of(layout, options, aim);
    }
}
