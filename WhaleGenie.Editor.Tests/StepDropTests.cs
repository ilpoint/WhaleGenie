using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Converters;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Where a dragged action lands in the step tree. The places a tree offers are not row numbers but
/// a list and a position in it, so these cases read the list back: the top and bottom of a folded
/// block, the body of an open one, and the space under the whole tree.
///
/// A folded block is the one row whose meaning is easy to get wrong: none of the steps inside it is
/// drawn, so the row cannot promise a place among them. It says before or after itself, like any
/// other row, and the band stands in its own column rather than among steps that are not there.
/// </summary>
public class StepDropTests
{
    /// <summary>
    /// 折叠的块只给块外两缝，体内有几 step 都一样：屏幕上没有一个位置能表示「放进块里」。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_folded_block_offers_only_the_seams_outside_it(int children)
    {
        var (above, below, aboveHint, belowHint, steps, body) = Ui.Run(() =>
        {
            var block = Block(expanded: false, children: children);
            var window = Open(block, out var list, out var editor);
            var row = Rows(list).Single();
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var top = row.TranslatePoint(new Point(0, 0), list)!.Value.Y;

            var above = window.DropTargetAt(new Point(20, top + (row.Bounds.Height * 0.25)),
                "input.keyPress");
            var aboveHint = hint.Text;
            var below = window.DropTargetAt(new Point(20, top + (row.Bounds.Height * 0.85)),
                "input.keyPress");
            var belowHint = hint.Text;

            window.Close();
            return (above, below, aboveHint, belowHint, editor.Steps,
                block.StepLists.Single().Steps);
        });

        Assert.Same(steps, above.Into);
        Assert.Equal(0, above.Index);
        Assert.Equal(Strings.Get("Editor.DropBeforeBlock"), aboveHint);

        Assert.Same(steps, below.Into);
        Assert.Equal(1, below.Index);
        Assert.Equal(Strings.Get("Editor.DropAfterBlock"), belowHint);

        // 体内那几步一步也没动：拖到折叠的块上不是「放进块里」。
        Assert.Equal(children, body.Count);
    }

    /// <summary>
    /// 带的缩进是它真正指向的那一行所在的那一列。折叠块的体内一行都不画，带就不该往体内那一列伸；
    /// 打开之后同一处才该落进体内那一列。
    /// </summary>
    [Fact]
    public void The_band_of_a_block_follows_whether_its_body_is_on_screen()
    {
        var (folded, foldedLeft, opened, openLeft, steps, body) = Ui.Run(() =>
        {
            // 里面那一块折叠，外面那一块打开：折叠块在第 1 列，它体内那一层在第 2 列。
            var inner = Block(expanded: false, children: 1);
            var outer = Block(expanded: true, children: 0);
            outer.StepLists.Single().Steps.Add(inner);

            var window = Open(outer, out var list, out _);
            var marker = window.FindControl<Border>("DropMarker")!;

            var folded = window.DropTargetAt(new Point(20, Middle(InnerRow(list, inner), list)),
                "input.keyPress");
            var foldedLeft = Canvas.GetLeft(marker);

            inner.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            var opened = window.DropTargetAt(new Point(20, Middle(InnerRow(list, inner), list)),
                "input.keyPress");
            var openLeft = Canvas.GetLeft(marker);

            window.Close();

            // 折叠时落点在外层循环的体内（块自己就在这一层），打开时才是它自己的体内。
            return (folded, foldedLeft, opened, openLeft, outer.StepLists.Single().Steps,
                inner.StepLists.Single().Steps);
        });

        // 折叠：整行说自己这一层，带停在块自己的列上——块就站在外层循环的体里，第 2 列。
        Assert.Same(steps, folded.Into);
        Assert.Equal(TreeIndent.Of(2), foldedLeft);

        // 打开：整行说「放进这一块、排在末尾」，带往下一层落进它体内那一列。
        Assert.Same(body, opened.Into);
        Assert.Equal(TreeIndent.Of(3), openLeft);
    }

    /// <summary>
    /// 列表标题也一样：它自己折起来的时候，它下面那一列步骤不画，带就该回到标题自己那一列。
    /// </summary>
    [Fact]
    public void A_folded_list_title_keeps_the_band_in_its_own_column()
    {
        Ui.Run(() =>
        {
            var block = Block(expanded: true, children: 1);
            var window = Open(block, out var list, out _);
            var marker = window.FindControl<Border>("DropMarker")!;
            var title = Rows(list).Single(panel => panel.DataContext is StepRow { IsSection: true });

            window.DropTargetAt(new Point(20, Middle(title, list)), "input.keyPress");
            Assert.Equal(TreeIndent.Of(2), Canvas.GetLeft(marker));

            block.StepLists.Single().IsExpanded = false;
            Dispatcher.UIThread.RunJobs();

            var folded = Rows(list).Single(panel =>
                panel.DataContext is StepRow { IsSection: true });
            window.DropTargetAt(new Point(20, Middle(folded, list)), "input.keyPress");
            Assert.Equal(TreeIndent.Of(1), Canvas.GetLeft(marker));

            window.Close();
        });
    }


    /// <summary>
    /// 折叠之后，原来放循环体的那一段不能再是落点。折叠只把子行的 presenter 藏起来，子行仍留在
    /// 视觉树里、`Bounds` 还是折叠前那一份——按"有没有高度"找行就会把看不见的体行当成屏幕上的位置，
    /// 于是指针走到原来体的位置，插入带又回到已经收起来的那一层。
    /// </summary>
    [Fact]
    public void Folding_a_block_does_not_leave_a_seam_where_its_body_was()
    {
        var (into, index, hint, steps, body) = Ui.Run(() =>
        {
            var block = Block(expanded: true, children: 1);
            var window = Open(block, out var list, out var editor);
            var header = BlockRow(list, block);
            var bottom = header.TranslatePoint(new Point(0, header.Bounds.Height), list)!.Value.Y;

            // 折叠要走行本身：树里的方块绑的是这一行的 `IsExpanded`，直接改步骤上的标记
            // 只会让模型说不展开，屏幕上的行还是开的。
            editor.Rows.First(row => row.IsStep && ReferenceEquals(row.Step, block)).IsExpanded = false;
            Dispatcher.UIThread.RunJobs();

            // 原来体的位置：块行下面一点点。
            var below = window.DropTargetAt(new Point(20, bottom + 12), "input.keyPress");
            var hint = window.FindControl<TextBlock>("DropHintText")!.Text;
            window.Close();
            return (below.Into, below.Index, hint, editor.Steps, block.StepLists.Single().Steps);
        });

        // 折叠的块只给块外两缝：这一下说的是"放在这一块之后"，不是"放进它的体里"。
        Assert.Same(steps, into);
        Assert.Equal(1, index);
        Assert.Equal(Strings.Get("Editor.DropAfterBlock"), hint);

        // 体内那一步谁也没被指着。
        Assert.Single(body);
    }

    /// <summary>The row header of one block, found by the step it stands for.</summary>
    private static Panel BlockRow(TreeView list, MacroStep step)
        => Rows(list).Single(panel =>
            panel.DataContext is StepRow row && row.IsStep && ReferenceEquals(row.Step, step));

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
            var last = rows[^1].TranslatePoint(new Point(0, rows[^1].Bounds.Height), list)!.Value.Y;
            var bottom = window.DropTargetAt(new Point(20, last + 24), "input.keyPress");
            window.Close();
            return (top, inside, bottom, editor.Steps, block.StepLists.Single().Steps);
        });

        Assert.Same(steps, top.Into);
        Assert.Equal(0, top.Index);

        Assert.Same(body, inside.Into);
        Assert.Equal(0, inside.Index);

        Assert.Same(steps, bottom.Into);
        Assert.Equal(1, bottom.Index);
    }

    /// <summary>One row's own centre, in the list's own coordinates.</summary>
    private static double Middle(Panel row, TreeView list)
        => row.TranslatePoint(new Point(0, row.Bounds.Height / 2), list)!.Value.Y;

    private static Panel InnerRow(TreeView list, MacroStep step)
        => Rows(list).Single(panel =>
            panel.DataContext is StepRow row && ReferenceEquals(row.Step, step));

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
