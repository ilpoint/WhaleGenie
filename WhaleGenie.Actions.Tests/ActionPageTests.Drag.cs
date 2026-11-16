using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// How a drag is broken up, the pointer switch and how a step lands.
public partial class ActionPageTests
{
    /// <summary>
    /// How far a drag is broken up, and how the pointer travels, are tuning; the two ends of the
    /// drag are what the step is.
    /// </summary>
    [Fact]
    public void How_a_drag_is_broken_up_is_tuning()
    {
        var drag = Ui.Run(() => ActionCatalog.Find("input.mouseDrag")!);
        var advanced = On(drag, viewModel => viewModel.AdvancedRows);

        Assert.Contains("dragSteps", advanced);
        Assert.Contains("style", advanced);

        // Where the drag starts and ends is what the step is, so it is on the basic page along with
        // the button it presses.
        var basic = On(drag, viewModel => viewModel.Rows);
        Assert.Contains("startX", basic);
        Assert.Contains("startY", basic);
        Assert.Contains("endX", basic);
        Assert.Contains("endY", basic);
    }

    /// <summary>
    /// The switch that takes a mouse step's place from the pointer is part of what the step is — the
    /// place it acts on — so it is read on the basic page, and it stands at the end of the line of
    /// the field it belongs to rather than taking a line of its own.
    /// </summary>
    [Theory]
    // 它坐在「点哪儿」那一行的末尾：点不点在指针当前位置，问的是位置，不是按哪个键。
    [InlineData("input.mouseClick", "x")]
    [InlineData("input.mouseDoubleClick", "x")]
    [InlineData("input.mouseDown", "x")]
    [InlineData("input.mouseUp", "x")]
    [InlineData("input.mouseScroll", "x")]
    public void The_switch_for_working_at_the_pointer_rides_the_line_it_belongs_to(
        string action, string line)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(action);
            var basic = viewModel.Rows.SelectMany(Names).ToList();

            Assert.Contains("useCursor", basic);
            Assert.Equal("useCursor", viewModel.Rows
                .First(row => row.First.Definition.Name == line).Third?.Definition.Name);
        });
    }
}
