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

// Moving a step: out of a block, inside one, by drag, and the drop a block refuses.
public partial class StepTreeTests
{
    [Fact]
    public void Folding_a_block_the_selection_was_inside_moves_the_selection_onto_it()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var inside = Step("control.delay");
            var loop = Loop(inside);
            editor.AddStep(loop);
            editor.SetSelection([inside]);

            editor.SetExpanded(loop, false);

            // Steps that are off screen must not be the ones a delete would act on.
            Assert.Same(loop, Assert.Single(editor.SelectedSteps));
        });
    }

    [Fact]
    public void A_step_inside_a_block_is_taken_out_of_that_block()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var inside = Step("control.delay");
            var loop = Loop(inside, Step("control.log"));
            editor.AddStep(loop);

            editor.SetSelection([inside]);
            editor.DeleteSelectedCommand.Execute(null);

            Assert.Equal(["control.log"], Inside(loop).Select(step => step.Type));
            Assert.Same(loop, Assert.Single(editor.Steps));
        });
    }

    [Fact]
    public void Moving_a_step_inside_a_block_leaves_the_rest_of_the_macro_alone()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var first = Step("control.delay");
            var second = Step("control.log");
            var loop = Loop(first, second);
            editor.AddStep(Step("system.volume"));
            editor.AddStep(loop);

            editor.SetSelection([second]);
            editor.MoveSelectedUpCommand.Execute(null);

            Assert.Equal([second, first], Inside(loop));
            Assert.Equal(2, editor.Steps.Count);
        });
    }

    [Fact]
    public void A_drag_can_carry_a_step_out_of_a_block()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var inside = Step("control.delay");
            var loop = Loop(inside);
            editor.AddStep(loop);

            editor.SetSelection([inside]);
            editor.MoveSelectionTo(editor.Steps, editor.Steps.Count);

            Assert.Empty(Inside(loop));
            Assert.Equal([loop, inside], editor.Steps);
        });
    }

    [Fact]
    public void A_drag_can_carry_a_step_into_a_block()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var outside = Step("control.delay");
            var kept = Step("control.log");
            var loop = Loop(kept);
            editor.AddStep(outside);
            editor.AddStep(loop);

            editor.SetSelection([outside]);
            editor.MoveSelectionTo(Inside(loop), 0);

            Assert.Equal([outside, kept], Inside(loop));
            Assert.Same(loop, Assert.Single(editor.Steps));
        });
    }

    [Fact]
    public void A_block_cannot_be_dropped_into_itself()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var loop = Loop(Step("control.delay"));
            editor.AddStep(loop);

            editor.SetSelection([loop]);
            editor.MoveSelectionTo(Inside(loop), 0);

            // A loop holding itself could never finish, so the drop is simply not taken.
            Assert.Same(loop, Assert.Single(editor.Steps));
            Assert.Single(Inside(loop));
            Assert.Equal(Strings.Get("Editor.PlaceIntoItself"), editor.Notice);
        });
    }
}
