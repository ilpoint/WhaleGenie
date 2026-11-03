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
/// Where a dragged action lands in the step tree. The places a tree offers are not row numbers but
/// a list and a position in it, so these cases read the list back: the top and bottom of a folded
/// block, the body of an open one, and the space under the whole tree.
/// </summary>
public class StepDropTests
{
    [Fact]
    public void A_folded_block_with_an_empty_body_still_offers_a_seam_above_and_below()
    {
        var (above, below, steps) = Ui.Run(() =>
        {
            var window = Open(Block(expanded: false, children: 0), out var list, out var editor);
            var row = Rows(list).Single();
            var above = window.DropTargetAt(new Point(20, 2), "input.keyPress");
            var below = window.DropTargetAt(new Point(20, row.Bounds.Height - 2), "input.keyPress");
            window.Close();
            return (above, below, editor.Steps);
        });

        Assert.Same(steps, above.Into);
        Assert.Equal(0, above.Index);
        Assert.True(above.Allowed);

        Assert.Same(steps, below.Into);
        Assert.Equal(1, below.Index);
        Assert.True(below.Allowed);
    }

    [Fact]
    public void A_folded_block_that_holds_steps_takes_a_drop_above_and_below_it()
    {
        var (above, below, steps) = Ui.Run(() =>
        {
            var window = Open(Block(expanded: false, children: 1), out var list, out var editor);
            var row = Rows(list).Single();
            var above = window.DropTargetAt(new Point(20, 2), "input.keyPress");
            var below = window.DropTargetAt(new Point(20, row.Bounds.Height - 2), "input.keyPress");
            window.Close();
            return (above, below, editor.Steps);
        });

        Assert.Same(steps, above.Into);
        Assert.Equal(0, above.Index);
        Assert.Same(steps, below.Into);
        Assert.Equal(1, below.Index);
    }

    [Fact]
    public void An_open_block_with_an_empty_body_keeps_the_tree_around_it()
    {
        var (top, inside, bottom, steps, body) = Ui.Run(() =>
        {
            var block = Block(expanded: true, children: 0);
            var window = Open(block, out var list, out var editor);
            var rows = Rows(list);

            var top = window.DropTargetAt(new Point(20, 2), "input.keyPress");

            // Between the block's own row and its closing line is its body: that is "inside".
            var blockRow = rows[0];
            var body = blockRow.TranslatePoint(
                new Point(0, blockRow.Bounds.Height / 2), list)!.Value.Y;
            var inside = window.DropTargetAt(new Point(20, body), "input.keyPress");

            // Under the whole tree is past the block rather than inside it.
            var bottom = window.DropTargetAt(new Point(20, list.Bounds.Height - 4), "input.keyPress");
            window.Close();
            return (top, inside, bottom, editor.Steps,
                block.StepLists.Single().Steps);
        });

        Assert.Same(steps, top.Into);
        Assert.Equal(0, top.Index);

        Assert.Same(body, inside.Into);
        Assert.Equal(0, inside.Index);

        Assert.Same(steps, bottom.Into);
        Assert.Equal(1, bottom.Index);
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

    private static MacroEditorWindow Open(MacroStep step, out TreeView list,
        out MacroEditorViewModel editor)
    {
        var macro = new MacroItem { Name = "m" };
        macro.Steps.Add(step);

        var window = new MacroEditorWindow(macro, [macro]);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        editor = (MacroEditorViewModel)window.DataContext!;
        list = window.GetVisualDescendants().OfType<TreeView>().First(box => box.Name == "StepList");
        return window;
    }

    /// <summary>The row headers on screen, top to bottom: one per step, section or closing line.</summary>
    private static IReadOnlyList<Panel> Rows(TreeView list)
        => [.. list.GetVisualDescendants().OfType<Panel>()
            .Where(panel => panel.Name == "RowHeader" && panel.Bounds.Height > 0)
            .OrderBy(panel => panel.TranslatePoint(new Point(0, 0), list)!.Value.Y)];
}
