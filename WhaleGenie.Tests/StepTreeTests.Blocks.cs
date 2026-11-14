using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// A block as it is read: indentation, the body a block holds, the sentence a flow step says, and try.
public partial class StepTreeTests
{
    [Fact]
    public void Every_level_sits_one_indent_step_to_the_right_of_the_one_above_it()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var loop = Loop(Loop(Step("control.log")));
            editor.AddStep(loop);
            Dispatcher.UIThread.RunJobs();

            // The tree indents by one step per level and the guides are placed from the same number,
            // so the two cannot drift apart: a level 16 pixels in while the line sits 26 out is a
            // line that misses the box it is drawn under.
            var list = window.FindControl<TreeView>("StepList")!;
            var lefts = window.GetVisualDescendants().OfType<Panel>()
                .Where(panel => panel.Name == "RowHeader" && panel.DataContext is StepRow)
                .Select(panel => (Row: (StepRow)panel.DataContext!,
                    X: panel.TranslatePoint(new Point(0, 0), list)!.Value.X))
                .GroupBy(pair => pair.Row.Depth)
                .ToDictionary(group => group.Key, group => group.First().X);

            Assert.Equal(StepRow.IndentWidth, lefts[1] - lefts[0]);
            Assert.Equal(StepRow.IndentWidth, lefts[2] - lefts[1]);

            // A row that holds steps drops a line from its own chevron, so the column carrying its
            // children starts at the block rather than at the first child.
            var block = editor.Rows.First(row => row.IsStep && ReferenceEquals(row.Step, loop));
            Assert.Contains(block.Guides, line => line.Tail);

            Ui.CloseAndAnswer(window);
        });
    }

    private static MacroStep Loop(params MacroStep[] steps)
        => Step("control.repeat", Body("body", steps));

    private static IList<MacroStep> Inside(MacroStep block)
        => block.StepLists.Single().Steps;

    /// <summary>Every row as "depth:what:which action", so the whole shape is one string.</summary>
    private static IReadOnlyList<string> Shape(MacroEditorViewModel editor)
        => [.. editor.Rows.Select(row => $"{row.Depth}:{row.Kind}:{row.Step.Type}")];

    [Fact]
    public void A_block_shows_the_steps_inside_it()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            editor.AddStep(Step("control.log"));
            editor.AddStep(Loop(Step("control.delay"), Step("control.delay")));

            Assert.Equal(
            [
                "0:Step:control.log",
                "0:Step:control.repeat",
                "1:Section:control.repeat",
                "2:Step:control.delay",
                "2:Step:control.delay",
            ], Shape(editor));
        });
    }

    [Fact]
    public void Every_block_shows_the_body_its_steps_go_in()
    {
        Ui.Run(() =>
        {
            var loop = Loop(Step("control.log"));
            var editor = new MacroEditorViewModel();
            editor.AddStep(loop);

            // One list or several, a block always names the body it runs: that row is what says
            // where its steps start, and it is the thing a person drags onto.
            var body = Assert.Single(editor.Rows, row => row.IsSection);
            Assert.Same(loop.StepLists.Single(), body.List);
            Assert.Equal(Strings.Format("Common.StepCount", 1), body.HeadCount);
            Assert.Equal(1, body.Depth);
        });
    }

    [Fact]
    public void A_block_that_runs_several_lists_names_each_one()
    {
        Ui.Run(() =>
        {
            var keep = Step("control.try",
                new StepParameter
                {
                    Name = "body",
                    Kind = ActionParameterKind.Steps,
                    Steps = [Step("control.log")],
                },
                new StepParameter { Name = "catch", Kind = ActionParameterKind.Steps },
                new StepParameter { Name = "finally", Kind = ActionParameterKind.Steps });

            var editor = new MacroEditorViewModel();
            editor.AddStep(keep);

            // Three lists would read as one run of steps without a name each, so each gets a row.
            Assert.Equal(["body", "catch", "finally"],
                editor.Rows.Where(row => row.IsSection && row.Depth == 1)
                    .Select(row => row.List!.Name));
        });
    }

    [Fact]
    public void A_conditional_block_says_its_condition_and_names_both_lists()
    {
        Ui.Run(() =>
        {
            var choice = Step("control.if", Text("condition", "$a == 1"));
            var editor = new MacroEditorViewModel();
            editor.AddStep(choice);

            // The row stands for the sentence the step is. "control.if · 条件 = …" is the
            // catalogue's way of naming things, not the reader's, and the row already says which
            // action it is by its name and icon.
            // 值按读出来的样子写：名字用角标括起来，不写前缀 —— 那一行是给人念的，不是给机器读的。
            Assert.Equal("「a」 == 1", choice.Detail);

            // "满足时 / 否则" rather than "满足时步骤 / 不满足时步骤": where a list sits already says
            // it is a list, and the short word is the one a person says out loud.
            Assert.Equal(["满足时", "否则"],
                editor.Rows.Where(row => row.IsSection).Select(row => row.HeadLabel));
        });
    }
    [Fact]
    public void A_try_block_says_how_much_sits_in_each_of_its_lists()
    {
        Ui.Run(() =>
        {
            var kept = Step("control.try",
                Body("body", Step("control.log"), Step("control.delay")),
                Body("catch", Step("control.log")),
                Body("finally"),
                new StepParameter
                {
                    Name = "returns",
                    Kind = ActionParameterKind.Returns,
                    Rows = [StepParameterRow.Of("name", Formula.Name("err")), StepParameterRow.Of("name", Formula.Name("why"))],
                });

            // The three lists are the block; the row says how many steps each holds, which is the
            // only thing about them that is visible without opening them.
            Assert.Equal(
                Strings.Format("Editor.FlowWithResults",
                    Strings.Format("Editor.FlowTry", 2, 1, 0),
                    Strings.Format("Editor.FlowReturns", 2)),
                kept.Detail);
        });
    }
}
