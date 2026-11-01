using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Where a dragged action lands in the step list. A list holding one block is the case that went
/// wrong: with the block folded there is one row and nothing below it, and with the block open its
/// empty body used to reach past the block on both sides, so the only place left was "inside".
/// </summary>
public class StepDropTests
{
    [Fact]
    public void A_folded_block_with_an_empty_body_still_offers_a_seam_above_and_below()
    {
        var (above, below) = Ui.Run(() =>
        {
            var window = Open(Block(expanded: false, children: 0), out var list);
            var row = Rows(list).Single();
            var above = window.DropSlotAt(new Point(20, 2), "input.keyPress");
            var below = window.DropSlotAt(new Point(20, row.Bounds.Height - 2), "input.keyPress");
            window.Close();
            return (above, below);
        });

        Assert.Equal(0, above.Slot);
        Assert.Null(above.Into);
        Assert.True(above.Allowed);

        Assert.Equal(1, below.Slot);
        Assert.Null(below.Into);
        Assert.True(below.Allowed);
    }

    [Fact]
    public void A_folded_block_that_holds_steps_takes_a_drop_above_and_below_it()
    {
        var (above, below) = Ui.Run(() =>
        {
            var window = Open(Block(expanded: false, children: 1), out var list);
            var row = Rows(list).Single();
            var above = window.DropSlotAt(new Point(20, 2), "input.keyPress");
            var below = window.DropSlotAt(new Point(20, row.Bounds.Height - 2), "input.keyPress");
            window.Close();
            return (above, below);
        });

        Assert.Equal(0, above.Slot);
        Assert.Null(above.Into);
        Assert.Equal(1, below.Slot);
        Assert.Null(below.Into);
    }

    [Fact]
    public void An_open_block_with_an_empty_body_keeps_the_list_around_it()
    {
        var (top, inside, bottom) = Ui.Run(() =>
        {
            var window = Open(Block(expanded: true, children: 0), out var list);
            var items = Rows(list);

            var top = window.DropSlotAt(new Point(20, 2), "input.keyPress");

            // Between the block's heading and its closing line is its body: that is "inside".
            var head = items[1];
            var body = head.TranslatePoint(new Point(0, 0), list)!.Value.Y + (head.Bounds.Height / 2);
            var inside = window.DropSlotAt(new Point(20, body), "input.keyPress");

            var bottom = window.DropSlotAt(new Point(20, list.Bounds.Height - 4), "input.keyPress");
            window.Close();
            return (top, inside, bottom);
        });

        Assert.Equal(0, top.Slot);
        Assert.Null(top.Into);

        Assert.Equal(-1, inside.Slot);
        Assert.Equal("control.repeat", inside.Into?.Type);

        Assert.Equal(3, bottom.Slot);
        Assert.Null(bottom.Into);
    }

    /// <summary>
    /// The one step in the list is a block with a single child list, empty or not, folded or not.
    /// </summary>
    private static MacroStep Block(bool expanded, int children)
    {
        var block = new MacroStep { Type = "control.repeat", IsExpanded = expanded };
        block.EnsureDeclaredLists();
        for (var index = 0; index < children; index++)
        {
            block.StepLists.First().Steps.Add(new MacroStep { Type = "control.setVariable" });
        }

        return block;
    }

    private static MacroEditorWindow Open(MacroStep step, out ListBox list)
    {
        var macro = new MacroItem { Name = "m" };
        macro.Steps.Add(step);

        var window = new MacroEditorWindow(macro, [macro]);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        list = window.GetVisualDescendants().OfType<ListBox>().First(box => box.Name == "StepList");
        return window;
    }

    private static IReadOnlyList<ListBoxItem> Rows(ListBox list)
        => [.. list.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(item => item.Bounds.Height > 0)
            .OrderBy(item => item.TranslatePoint(new Point(0, 0), list)!.Value.Y)];
}
