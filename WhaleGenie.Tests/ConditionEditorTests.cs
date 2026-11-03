using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// A block's condition, read as rows and as a tree over one model. What matters is that both views
/// say the same thing: the condition is still one piece of expression text, and switching views
/// never changes it.
/// </summary>
public class ConditionEditorTests
{
    private static ConditionEditorViewModel Editor(string text)
    {
        ConditionEditorViewModel? editor = null;
        var changed = 0;
        editor = new ConditionEditorViewModel(text, () => changed++);
        return editor;
    }

    [Fact]
    public void A_flat_condition_opens_as_one_row_per_comparison()
    {
        Ui.Run(() =>
        {
            var editor = Editor("$a == 1 and $b > 2");

            Assert.False(editor.IsRaw);
            Assert.False(editor.Nests);
            Assert.Equal(2, editor.Rows.Count);
            Assert.Equal("$a", editor.Rows[0].Left);
            Assert.Equal(ConditionCompare.Equal, editor.Rows[0].Compare);
            Assert.Equal("1", editor.Rows[0].Right);
            Assert.Equal(ConditionCompare.Greater, editor.Rows[1].Compare);
            Assert.Equal("$a == 1 and $b > 2", editor.Text);
        });
    }

    [Fact]
    public void A_condition_that_nests_opens_as_a_group_and_says_so()
    {
        Ui.Run(() =>
        {
            var editor = Editor("($a == 1 or $b == 2) and $c == 3");

            Assert.False(editor.IsRaw);
            Assert.True(editor.Nests);
            Assert.Equal(2, editor.Rows.Count);
            Assert.True(editor.Rows[0].IsGroup);
            Assert.True(editor.Rows[0].Any);
            Assert.Equal(2, editor.Rows[0].Children.Count);
            Assert.Equal("($a == 1 or $b == 2) and $c == 3", editor.Text);
        });
    }

    [Fact]
    public void An_any_group_is_written_with_or_and_an_all_group_with_and()
    {
        Ui.Run(() =>
        {
            var editor = Editor(string.Empty);
            editor.AddRow();
            editor.AddRow();
            editor.Rows[0].Left = "$a";
            editor.Rows[0].Right = "1";
            editor.Rows[1].Left = "$b";
            editor.Rows[1].Right = "2";

            Assert.Equal("$a == 1 and $b == 2", editor.Text);

            editor.OperatorIndex = 1;
            Assert.Equal("$a == 1 or $b == 2", editor.Text);
        });
    }

    [Fact]
    public void A_comparison_without_a_right_hand_side_writes_nothing_for_that_side()
    {
        Ui.Run(() =>
        {
            var editor = Editor(string.Empty);
            editor.AddRow();
            editor.Rows[0].Left = "$found";
            editor.Rows[0].Compare = ConditionCompare.Holds;

            Assert.False(editor.Rows[0].NeedsValue);
            Assert.Equal("$found", editor.Text);

            editor.Rows[0].Compare = ConditionCompare.Fails;
            Assert.Equal("not $found", editor.Text);
        });
    }

    [Fact]
    public void A_half_filled_row_is_left_out_rather_than_written_broken()
    {
        Ui.Run(() =>
        {
            var editor = Editor(string.Empty);
            editor.AddRow();
            editor.Rows[0].Left = "$a";
            editor.Rows[0].Right = "1";
            editor.AddRow();
            editor.Rows[1].Left = "$b";

            // The second comparison has nothing to compare with, so it is not written; the step
            // then says it has a condition, which it does.
            Assert.Equal("$a == 1", editor.Text);
        });
    }

    [Fact]
    public void Text_that_does_not_read_stays_as_it_was_written()
    {
        Ui.Run(() =>
        {
            var editor = Editor("$a +");

            Assert.True(editor.IsRaw);
            Assert.Equal("$a +", editor.Text);

            // Fixing it in the raw box brings the rows back without touching what was typed.
            editor.RawText = "$a == 1";
            Assert.Equal("$a == 1", editor.Text);
        });
    }

    [Fact]
    public void Reading_the_same_condition_twice_gives_the_same_rows_back()
    {
        Ui.Run(() =>
        {
            var editor = Editor("$a == 1 and not $b and $c >= 3");
            var first = editor.Text;

            editor.Read(first);

            Assert.Equal(first, editor.Text);
            Assert.Equal(3, editor.Rows.Count);
            Assert.Equal(ConditionCompare.Fails, editor.Rows[1].Compare);
            Assert.Equal(ConditionCompare.AtLeast, editor.Rows[2].Compare);
        });
    }

    [Fact]
    public void Switching_views_does_not_change_the_condition()
    {
        Ui.Run(() =>
        {
            var editor = Editor("$a == 1 and $b == 2");
            var before = editor.Text;

            editor.Show(tree: true);
            Assert.True(editor.ShowTree);
            Assert.False(editor.ShowTable);

            editor.Rows[0].Right = "9";
            Assert.Equal("$a == 9 and $b == 2", editor.Text);
            Assert.NotEqual(before, editor.Text);

            editor.Show(tree: false);
            Assert.Equal("$a == 9 and $b == 2", editor.Text);
        });
    }

    [Fact]
    public void Removing_a_row_takes_it_out_of_the_condition()
    {
        Ui.Run(() =>
        {
            var editor = Editor("$a == 1 and $b == 2");
            editor.Remove(editor.Rows[0]);

            Assert.Single(editor.Rows);
            Assert.Equal("$b == 2", editor.Text);
        });
    }

    [Fact]
    public void A_group_can_hold_a_group()
    {
        Ui.Run(() =>
        {
            var editor = Editor("$a == 1");
            editor.AddGroup();
            var group = editor.Rows[1];
            editor.AddRow(group);
            editor.AddRow(group);
            group.Children[0].Left = "$b";
            group.Children[0].Right = "2";
            group.Children[1].Left = "$c";
            group.Children[1].Right = "3";

            Assert.True(editor.Nests);
            Assert.Equal("$a == 1 and ($b == 2 or $c == 3)", editor.Text);
        });
    }

    /// <summary>The step's form shows the same model, and what it writes is what the step holds.</summary>
    [Fact]
    public void The_form_edits_the_steps_own_condition()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("found"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var form = (AddActionViewModel)window.DataContext!;
            form.SelectAction("control.if");
            Dispatcher.UIThread.RunJobs();

            var parameter = form.Parameters.First(item => item.Definition.Name == "condition");
            Assert.True(parameter.IsCondition);
            var editor = parameter.ConditionEditor!;
            Assert.False(editor.IsRaw);

            editor.AddRow();
            editor.Rows[0].Left = "$found";
            editor.Rows[0].Compare = ConditionCompare.Holds;

            Assert.Equal("$found", parameter.Text);

            // A step read back from a file hands its condition in the same way.
            parameter.Text = "$count > 3";
            Assert.False(editor.IsRaw);
            Assert.Equal("$count", Assert.Single(editor.Rows).Left);
            window.Close();
        });
    }

    /// <summary>One row is wide enough for two boxes and the comparison between them.</summary>
    [Fact]
    public void A_row_draws_both_sides_and_the_comparison()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [],
                "control.if");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var form = (AddActionViewModel)window.DataContext!;
            var parameter = form.Parameters.Single(item => item.Definition.Name == "condition");
            var editor = parameter.ConditionEditor!;
            editor.AddRow();
            editor.Rows[0].Left = "$found";
            editor.Rows[0].Right = "3";
            editor.Show(tree: false);
            Dispatcher.UIThread.RunJobs();

            var row = window.GetVisualDescendants().OfType<Grid>()
                .First(grid => grid.Classes.Contains("ConditionRow") && grid.IsEffectivelyVisible);
            var boxes = row.GetVisualDescendants().OfType<AutoCompleteBox>()
                .Where(box => box.IsEffectivelyVisible)
                .ToList();
            Assert.Equal(2, boxes.Count);
            Assert.All(boxes, box => Assert.True(box.Bounds.Width > 60,
                $"一侧只有 {box.Bounds.Width:0} 宽"));

            var compare = row.GetVisualDescendants().OfType<ComboBox>()
                .First(box => box.IsEffectivelyVisible);
            Assert.True(compare.Bounds.Width >= 100, $"比较词只有 {compare.Bounds.Width:0} 宽");
            window.Close();
        });
    }

    /// <summary>The editor draws both views, and the switch at the top is what changes which one.</summary>
    [Fact]
    public void Both_views_are_drawn_from_one_model()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("found"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var form = (AddActionViewModel)window.DataContext!;
            form.SelectAction("control.if");
            Dispatcher.UIThread.RunJobs();

            var view = window.GetVisualDescendants().OfType<ConditionEditorView>().First();
            var model = (ConditionEditorViewModel)view.DataContext!;
            Assert.True(model.ShowTable);

            model.Show(tree: true);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(view.GetVisualDescendants().OfType<TreeView>(),
                tree => tree.IsEffectivelyVisible);
            window.Close();
        });
    }
}
