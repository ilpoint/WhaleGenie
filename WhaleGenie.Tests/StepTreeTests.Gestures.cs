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

// What a click on a row does: its own value row, the fold box, the strip beside it and the edit button.
public partial class StepTreeTests
{

    /// <summary>The visible button a row carries, such as the one that adds a step to a block.</summary>
    private static Button RowButton(Window window, Func<StepRow, bool> match)
        => window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Bounds.Width > 0
                && button.DataContext is StepRow row && match(row));

    [Fact]
    public void A_branchs_values_are_edited_on_its_own_row()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var choice = Step("control.switch",
                Text("switchValue", "mode"),
                new StepParameter
                {
                    Name = "cases",
                    Kind = ActionParameterKind.Steps,
                    Steps =
                    [
                        Step("control.case", Text("values", "1"),
                            Body("body", Step("control.log"))),
                    ],
                },
                new StepParameter { Name = "otherwise", Kind = ActionParameterKind.Steps });
            editor.AddStep(choice);
            Dispatcher.UIThread.RunJobs();

            var branch = editor.Rows.First(row => row.IsCase);
            var chip = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.IsEffectivelyVisible
                    && ReferenceEquals(button.DataContext, branch));

            Click(window, chip);
            Dispatcher.UIThread.RunJobs();

            // A branch is one line of values and the steps that run for them, so its values are
            // typed where they are read — no dialog for a field that fits on the row.
            Assert.True(branch.IsEditingValues);
            Assert.Empty(window.OwnedWindows.OfType<ActionEditorWindow>());
            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void Double_clicking_the_fold_box_folds_the_block_instead_of_opening_it()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var loop = Loop(Step("control.delay"));
            editor.AddStep(loop);
            Dispatcher.UIThread.RunJobs();

            var box = window.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => button.Name == "PART_ExpandCollapseChevron"
                    && button.IsEffectivelyVisible
                    && ReferenceEquals((button.DataContext as StepRow)?.Step, loop));
            DoubleClick(window, box.TranslatePoint(
                new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window) ?? default);

            // The box folds; it does not ask to edit the block it belongs to.
            Assert.Empty(window.OwnedWindows.OfType<ActionEditorWindow>());
            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void Double_clicking_the_strip_beside_a_row_does_not_open_it()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var step = Step("control.delay");
            editor.AddStep(step);
            Dispatcher.UIThread.RunJobs();

            // The strip the guides are drawn in: inside the row's item, left of its content.
            var item = window.GetVisualDescendants().OfType<TreeViewItem>()
                .Single(box => box.DataContext is StepRow row && ReferenceEquals(row.Step, step));
            DoubleClick(window, item.TranslatePoint(new Point(3, item.Bounds.Height / 2), window) ?? default);

            Assert.Empty(window.OwnedWindows.OfType<ActionEditorWindow>());
            Ui.CloseAndAnswer(window);
        });
    }

    /// <summary>Two clicks with the queue emptied between them, the way a real double click arrives.</summary>
    private static void DoubleClick(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>One row's own header, which is where a drop seam is measured.</summary>
    private static Panel Header(Window window, Func<StepRow, bool> match)
        => window.GetVisualDescendants().OfType<Panel>()
            .Single(panel => panel.Name == "RowHeader"
                && panel.DataContext is StepRow row && match(row));

    /// <summary>The tree's own container for one row, which is what carries the fold arrow.</summary>
    private static TreeViewItem Row(Window window, Func<StepRow, bool> match)
        => window.GetVisualDescendants().OfType<TreeViewItem>()
            .First(item => item.DataContext is StepRow row && match(row));

    private static void Click(Window window, Visual target)
    {
        var at = Waypoint(window, target, 0.5, 0.5);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(at, MouseButton.Left);
    }

    [Fact]
    public void The_edit_button_turns_on_for_a_step_inside_a_block()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var button = window.GetVisualDescendants().OfType<Button>()
                .First(candidate => candidate.DataContext is MacroAction { Key: "edit" });
            Assert.False(button.IsEffectivelyEnabled);

            var loop = Loop(Step("control.delay"));
            editor.AddStep(loop);
            Dispatcher.UIThread.RunJobs();

            editor.SetSelection([Inside(loop)[0]]);
            Dispatcher.UIThread.RunJobs();

            Assert.True(button.IsEffectivelyEnabled);
        });
    }

    [Fact]
    public void A_step_can_be_added_to_a_block_that_sits_inside_another_block()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var inner = Loop();
            var outer = Loop(inner);
            editor.AddStep(outer);

            // Picking the inner block says "the step goes in here", and inside a block that runs
            // one list that can only mean the end of it.
            editor.SetSelection([inner]);
            editor.NewStepCommand.Execute(null);
            editor.AddStep(Step("control.log"));

            Assert.Equal(["control.log"], Inside(inner).Select(step => step.Type));
            Assert.Equal(["control.repeat"], editor.Steps.Select(step => step.Type));
        });
    }

    [Fact]
    public void Double_clicking_a_step_inside_a_block_edits_that_step()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var loop = Loop(Step("input.keyPress", Text("key", "F5")));
            editor.AddStep(loop);
            Dispatcher.UIThread.RunJobs();

            var inner = editor.Rows.First(row => row.IsStep && ReferenceEquals(row.Step, Inside(loop)[0]));
            var header = Header(window, row => ReferenceEquals(row, inner));
            var point = header.TranslatePoint(
                new Point(header.Bounds.Width * 0.3, header.Bounds.Height / 2), window) ?? default;

            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left);
            // The real thing has a gap between the two clicks, so anything the first one queued has
            // run by the time the second arrives. That gap is a pause, not a wait: pumping the
            // queue empties it without spending wall-clock time, which on a loaded machine grows
            // past the double-click window and turns the two clicks into two single ones.
            Dispatcher.UIThread.RunJobs();
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(Inside(loop)[0], Assert.Single(editor.SelectedSteps));
            // Opening a step must not also fold the block it was found in.
            Assert.True(loop.IsExpanded);
            // The modal action window has to be about the step that was double-clicked, and what
            // it returns has to land back inside the block — not on the block that holds it.
            var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
            var view = action.View;
            var dialogModel = (AddActionViewModel)view.DataContext!;
            Assert.True(dialogModel.IsEditing, "the dialog opened as a new step, not as an edit");
            Assert.Equal("input.keyPress", dialogModel.SelectedDefinition!.Key);

            // What the step holds has to be on screen, or "editing" it would quietly write the
            // defaults back over it.
            Assert.Equal("F5", dialogModel.Parameters.First(parameter => parameter.Definition.Name == "key").Text);
            dialogModel.Parameters.First(parameter => parameter.Definition.Name == "key").Text = "F6";
            Assert.True(dialogModel.CanSave, dialogModel.ValidationMessage);
            dialogModel.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var saved = Inside(loop)[0];
            Assert.Equal("F6", saved.Parameters.First(parameter => parameter.Name == "key").Value);
        });
    }
}
