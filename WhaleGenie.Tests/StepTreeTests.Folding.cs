using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Folding: what closes, what an empty block offers, and the arrow a row carries.
public partial class StepTreeTests
{
    [Fact]
    public void Folding_a_block_hides_what_is_inside_it()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var loop = Loop(Step("control.delay"));
            editor.AddStep(loop);

            editor.SetExpanded(loop, false);

            Assert.Equal(["0:Step:control.repeat"], Shape(editor));

            // The block still says it has something in it, so it can be opened again.
            Assert.True(editor.Rows[0].IsBlock);
        });
    }

    [Fact]
    public void An_empty_block_can_be_folded_and_opened_for_its_first_child()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var loop = Step("control.repeat",
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps });
            editor.AddStep(loop);

            Assert.True(editor.Rows[0].IsBlock);
            Assert.Equal(["0:Step:control.repeat", "1:Section:control.repeat"], Shape(editor));

            editor.SetExpanded(loop, false);
            Assert.Equal(["0:Step:control.repeat"], Shape(editor));
            Assert.True(Assert.Single(editor.Rows).IsBlock);

            editor.SetExpanded(loop, true);
            Assert.Equal(["0:Step:control.repeat", "1:Section:control.repeat"], Shape(editor));
        });
    }

    [Fact]
    public void A_conditional_block_gets_its_empty_child_lists_from_the_catalogue()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var conditional = Step("control.if",
                new StepParameter
                {
                    Name = "condition",
                    Kind = ActionParameterKind.Expression,
                    Value = "false",
                });
            editor.AddStep(conditional);

            Assert.True(conditional.IsExpanded);
            Assert.Equal(["then", "else"],
                conditional.StepLists.Select(list => list.Name));
            Assert.Equal(2, editor.Rows.Count(row => row.IsSection));
        });
    }

    [Fact]
    public void The_fold_arrow_works_before_a_block_has_its_first_child()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var loop = Step("control.repeat",
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps });
            editor.AddStep(loop);
            Dispatcher.UIThread.RunJobs();

            // The arrow belongs to the tree now, so what has to hold is the link between it and the
            // step: closing the row closes the block, and opening it again brings the body back.
            var item = Row(window, row => ReferenceEquals(row.Step, loop));
            item.IsExpanded = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(loop.IsExpanded);

            item.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(loop.IsExpanded);
            Assert.Equal(2, editor.Rows.Count);
            Assert.Contains(editor.Rows, row => row.IsSection);
            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void A_branch_list_only_takes_branches()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var choice = Step("control.switch");
            editor.AddStep(choice);

            // The restriction belongs to the place the step is going, not to the dialog: pointing
            // at a switch's case list narrows the catalogue to the one action that fits there.
            var cases = choice.StepLists.Single(list => list.Name == "cases");
            editor.PrepareInsertionAt(cases.Steps, 0);
            Assert.Equal(["control.case"], editor.InsertChoices!.Select(action => action.Key));

            var branch = Step("control.case", Text("values", "one"));
            editor.AddStep(branch);

            Assert.Equal("control.case", Assert.Single(cases.Steps).Type);
            Assert.True(branch.IsExpanded);
            Assert.Single(branch.StepLists);
        });
    }
}
