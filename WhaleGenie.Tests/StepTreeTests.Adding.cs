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

// Adding a step: below the picked row, inside a block or a switch, and what a list will take.
public partial class StepTreeTests
{
    [Fact]
    public void A_new_step_lands_below_the_picked_one_in_the_same_block()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var inside = Step("control.delay");
            var loop = Loop(inside);
            editor.AddStep(loop);

            editor.SetSelection([inside]);
            editor.NewStepCommand.Execute(null);
            var added = Step("control.log");
            editor.AddStep(added);

            Assert.Equal([inside, added], Inside(loop));
            Assert.Same(added, Assert.Single(editor.SelectedSteps));
        });
    }

    [Fact]
    public void Picking_a_block_puts_a_new_step_inside_it()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var inside = Step("control.delay");
            var loop = Loop(inside);
            editor.AddStep(loop);

            // Picking the loop says where the next step goes: it runs one list, so "inside it" is
            // the only place that could mean.
            editor.SetSelection([loop]);
            editor.NewStepCommand.Execute(null);
            var added = Step("control.log");
            editor.AddStep(added);

            Assert.Equal([inside, added], Inside(loop));
        });
    }

    [Fact]
    public void Picking_a_switch_and_adding_a_step_adds_a_branch()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var choice = Step("control.switch",
                Text("switchValue", "mode"),
                new StepParameter { Name = "cases", Kind = ActionParameterKind.Steps },
                new StepParameter { Name = "otherwise", Kind = ActionParameterKind.Steps });
            editor.AddStep(choice);

            // A switch has two lists, so "inside it" would not say which — but the step a person
            // means when they pick a switch and add one is the next branch, and the palette there
            // offers only that.
            editor.SetSelection([choice]);
            editor.NewStepCommand.Execute(null);
            Assert.Equal(["control.case"], editor.InsertChoices!.Select(action => action.Key));

            var branch = Step("control.case", Text("values", "1"));
            editor.AddStep(branch);

            var cases = choice.StepLists.Single(list => list.Name == "cases");
            Assert.Same(branch, Assert.Single(cases.Steps));
        });
    }

    [Fact]
    public void A_list_that_only_takes_one_kind_of_step_says_so_where_the_step_is_added()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var branch = Step("control.case", Text("values", "a"), Body("body", Step("control.log")));
            var switchStep = Step("control.switch", Text("value", "x"),
                new StepParameter { Name = "cases", Kind = ActionParameterKind.Steps, Steps = [branch] });
            var loop = Loop(Step("control.log"));

            editor.AddStep(switchStep);
            editor.AddStep(loop);

            // Nothing is being added, so nothing is being narrowed yet.
            Assert.Null(editor.InsertChoices);

            // A switch's list holds branches and nothing else, so that is all the dialog offers
            // when the step is meant for it. The restriction travels with the place the step is
            // going, not with the dialog.
            var cases = switchStep.StepLists.Single(list => list.Name == "cases");
            editor.PrepareInsertionAt(cases.Steps, 0);
            Assert.Equal(["control.case"], editor.InsertChoices!.Select(action => action.Key));

            // A block that runs ordinary steps takes anything.
            editor.PrepareInsertionAt(loop.StepLists.Single().Steps, 0);
            Assert.Null(editor.InsertChoices);
        });
    }

    [Fact]
    public void A_step_dropped_onto_a_block_lands_inside_it()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var inside = Step("control.log");
            var loop = Loop(inside);
            var outside = Step("control.delay");
            editor.AddStep(loop);
            editor.AddStep(outside);

            editor.SetSelection([outside]);
            editor.MoveSelectionInto(loop);

            Assert.Equal([inside, outside], Inside(loop));
            Assert.Same(loop, Assert.Single(editor.Steps));
        });
    }
}
