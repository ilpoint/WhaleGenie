using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Dragging a step in the list: where it lands, and what a drop that changes nothing does.
public partial class MacroEditorTests
{
    [Fact]
    public void Dropping_a_step_further_down_reorders_the_list()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();
            var first = new MacroStep { Type = "control.delay" };
            var second = new MacroStep { Type = "control.delay" };
            viewModel.AddStep(first);
            viewModel.AddStep(second);

            viewModel.SetSelection([first]);
            viewModel.MoveSelectionTo(viewModel.Steps, 2);

            Assert.Equal(2, viewModel.Steps.Count);
            Assert.Same(second, viewModel.Steps[0]);
            Assert.Same(first, viewModel.Steps[1]);

            // The dragged step stays picked, so it can be moved again without another click.
            Assert.Same(first, Assert.Single(viewModel.SelectedSteps));
        });
    }

    [Fact]
    public void Dropping_a_step_where_it_already_was_changes_nothing()
    {
        Ui.Run(() =>
        {
            var (_, viewModel) = Open();
            var first = new MacroStep { Type = "control.delay" };
            var second = new MacroStep { Type = "control.log" };
            viewModel.AddStep(first);
            viewModel.AddStep(second);

            viewModel.SetSelection([second]);
            viewModel.MoveSelectionTo(viewModel.Steps, 1);

            Assert.Equal(["control.delay", "control.log"],
                viewModel.Steps.Select(step => step.Type));

            // A drag that lands where it started must not fill the undo history. One undo
            // therefore reaches past it, to the step that was added before the drag. What is
            // read back is the kind of step rather than the object itself: the history keeps
            // its own copies, so undoing hands the list a fresh step with the same contents.
            viewModel.UndoCommand.Execute(null);
            Assert.Equal("control.delay", Assert.Single(viewModel.Steps).Type);
        });
    }

    [Fact]
    public void A_mouse_drag_carries_a_step_past_the_one_below_it()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            var first = new MacroStep { Type = "control.delay" };
            var second = new MacroStep { Type = "control.delay" };
            viewModel.AddStep(first);
            viewModel.AddStep(second);
            Dispatcher.UIThread.RunJobs();

            var rows = window.FindControl<TreeView>("StepList")!.GetVisualDescendants().OfType<TreeViewItem>().ToList();
            Assert.Equal(2, rows.Count);

            // Grab the first row by its middle and let go past the middle of the second,
            // which is where a drop "after it" is asked for.
            var from = Waypoint(window, rows[0], 0.5, 0.5);
            var to = Waypoint(window, rows[1], 0.5, 0.9);

            // The pressed button has to travel with the move: a drag is only a drag while the
            // pointer reports the button still down.
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(second, viewModel.Steps[0]);
            Assert.Same(first, viewModel.Steps[1]);
        });
    }
}
