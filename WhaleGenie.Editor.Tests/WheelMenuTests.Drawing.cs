using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Drawing the wheel: the centre, the guide line, the rings and the preview.
public partial class WheelMenuTests
{
    [Fact]
    public void The_centre_says_close_and_the_invisible_circle_shows_only_the_name_or_note()
    {
        Ui.Run(() =>
        {
            var rules = new WheelMenuRules();
            rules.Global.At(Top).Macro = "copy";
            var extended = new WheelSlotRef(WheelRing.Extended, 2);
            rules.Global.At(extended).Macro = "paste";
            rules.Global.At(extended).Note = "粘贴纯文本";
            var layout = rules.Resolve(null, WheelSource.Global, false,
                [
                    new MacroItem { Name = "copy", IsEnabled = true },
                    new MacroItem
                    {
                        Name = "paste",
                        IsEnabled = true,
                        Description = "说明不该显示",
                    },
                ]);

            // The same action without a note is named by the macro's name, never by its
            // description: that is prose and does not fit the one line in the centre.
            var named = new WheelMenuRules();
            named.Global.At(extended).Macro = "paste";
            var namedLayout = named.Resolve(null, WheelSource.Global, false,
                [new MacroItem { Name = "paste", IsEnabled = true, Description = "说明不该显示" }]);

            var window = new WheelMenuWindow(layout, new WheelOptions(),
                new PixelPoint(400, 400), preview: true);

            Assert.Equal(Strings.Get("Wheel.Close"), window.CenterHint);
            window.Hover(Top);
            Assert.NotEqual(Strings.Get("Wheel.Close"), window.CenterHint);

            window.Hover(extended);
            Assert.Equal("粘贴纯文本", window.CenterHint);
            Assert.DoesNotContain("划一下", window.CenterHint);

            window.SetLayout(namedLayout);
            Assert.Equal("paste", window.CenterHint);
            Assert.DoesNotContain("说明不该显示", window.CenterHint);

            window.Hover(null);
            Assert.Equal(Strings.Get("Wheel.Close"), window.CenterHint);
            window.Aim(600, 400);
            window.Close();
        });
    }

    [Fact]
    public void The_guide_line_stops_at_the_window_edge_when_the_pointer_is_far_outside()
    {
        var centre = new Point(210, 210);
        Assert.Equal(new Point(300, 210),
            WheelCanvas.ClampToSquare(centre, new Point(300, 210), 420));
        Assert.Equal(420, WheelCanvas.ClampToSquare(centre, new Point(900, 210), 420).X, 5);
        var diagonal = WheelCanvas.ClampToSquare(centre, new Point(-500, -500), 420);
        Assert.InRange(diagonal.X, 0, 420);
        Assert.InRange(diagonal.Y, 0, 420);
    }

    [Fact]
    public void A_preview_wheel_renders_without_any_global_hook()
    {
        Ui.Run(() =>
        {
            var rules = new WheelMenuRules();
            rules.Global.At(Top).Macro = "copy";
            var layout = rules.Resolve(null, WheelSource.Global, false,
                [new MacroItem { Name = "copy", IsEnabled = true }]);
            var window = new WheelMenuWindow(layout, new WheelOptions(),
                new PixelPoint(400, 400), preview: true);

            Assert.Equal(840, window.Width);
            Assert.True(window.Center.X >= 0);
            Assert.NotNull(window.SlotAt(Top)?.Macro);
            window.Hover(Right);
            window.Close();
        });
    }

    [Fact]
    public void A_third_circle_adds_positions_and_the_geometry_follows_it()
    {
        var classic = WheelGeometry.For(420, false);
        Assert.Equal(0, classic.ThirdCount);
        Assert.Equal(new WheelSlotRef(WheelRing.Outer, 2),
            WheelGeometry.At(160, 0, classic));

        var third = WheelGeometry.For(420, false, 8);
        Assert.Equal(8, third.ThirdCount);
        Assert.True(third.HasThird);
        Assert.Equal(new WheelSlotRef(WheelRing.Inner, 0),
            WheelGeometry.At(0, -60, third));
        Assert.Equal(new WheelSlotRef(WheelRing.Outer, 2),
            WheelGeometry.At(110, 0, third));
        Assert.Equal(new WheelSlotRef(WheelRing.Third, 2),
            WheelGeometry.At(170, 0, third));
        Assert.Equal(new WheelSlotRef(WheelRing.Extended, 2),
            WheelGeometry.At(400, 0, third));

        var hidden = WheelGeometry.For(420, false, 8, rings: 1);
        Assert.False(hidden.HasOuter);
        Assert.False(hidden.HasThird);
        Assert.Equal(new WheelSlotRef(WheelRing.Inner, 2),
            WheelGeometry.At(160, 0, hidden));
        Assert.Equal(new WheelSlotRef(WheelRing.Extended, 2),
            WheelGeometry.At(250, 0, hidden));
    }

    [Fact]
    public void An_empty_outer_circle_is_not_drawn_when_it_carries_no_action()
    {
        var rules = new WheelMenuRules();
        rules.Global.At(Top).Macro = "copy";
        var layout = rules.Resolve(null, WheelSource.Global, false,
            [new MacroItem { Name = "copy", IsEnabled = true }],
            thirdCapacity: 8, hideEmptyOuter: true);

        Assert.Equal(8, layout.ThirdCapacity);
        Assert.True(layout.HideEmptyOuter);
        Assert.DoesNotContain(layout.Slots, slot =>
            slot.Reference.Ring == WheelRing.Third && slot.Macro is not null);
    }

    [Fact]
    public void The_three_sources_answer_the_same_position_differently()
    {
        var rules = new WheelMenuRules();
        rules.Global.At(Top).Macro = "copy";
        var scene = rules.AddScene(new WindowRule(Process: "notepad",
            How: WindowHow.ProcessEquals));
        scene.At(Top).Macro = "cut";
        var macros = new[]
        {
            new MacroItem { Name = "copy", IsEnabled = true },
            new MacroItem { Name = "cut", IsEnabled = true },
        };

        Assert.Equal("copy", rules.Resolve(scene, WheelSource.Global, false, macros)
            .At(Top)?.Macro?.Name);
        Assert.Equal("cut", rules.Resolve(scene, WheelSource.Merged, false, macros)
            .At(Top)?.Macro?.Name);
        Assert.Equal("cut", rules.Resolve(scene, WheelSource.Program, false, macros)
            .At(Top)?.Macro?.Name);
    }
}
