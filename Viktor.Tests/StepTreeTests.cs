using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Viktor.Models;
using Viktor.ViewModels;
using Viktor.Views;

namespace Viktor.Tests;

/// <summary>
/// The step list shows the macro the way it is written: a block opens its steps underneath,
/// indented, and closes them with a line of its own. Reading is the point of it — a macro whose
/// shape is hidden is a macro nobody can check — so most of these cases read the rows back, and
/// the rest make sure editing works at any depth.
/// </summary>
public class StepTreeTests
{
    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    private static StepParameter Body(string name, params MacroStep[] steps)
        => new() { Name = name, Kind = ActionParameterKind.Steps, Steps = [.. steps] };

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
                "1:Head:control.repeat",
                "2:Step:control.delay",
                "2:Step:control.delay",
                "0:Foot:control.repeat",
            ], Shape(editor));
        });
    }

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
            Assert.True(Assert.Single(editor.Rows).CanFold);
        });
    }

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
            editor.MoveSelectionTo(editor.Rows.Count);

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
            editor.MoveSelectionTo(editor.Rows.First(row => row.IsHead).RowIndex);

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
            editor.MoveSelectionTo(editor.Rows.First(row => row.IsHead).RowIndex);

            // A loop holding itself could never finish, so the drop is simply not taken.
            Assert.Same(loop, Assert.Single(editor.Steps));
            Assert.Single(Inside(loop));
        });
    }

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
    public void The_button_on_a_block_title_adds_a_step_at_the_end_of_that_list()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var inside = Step("control.delay");
            var loop = Loop(inside);
            editor.AddStep(loop);

            editor.AddInsideCommand.Execute(editor.Rows.First(row => row.IsHead));
            var added = Step("control.log");
            editor.AddStep(added);

            Assert.Equal([inside, added], Inside(loop));
        });
    }

    [Fact]
    public void Undo_brings_back_a_step_deleted_from_inside_a_block()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var inside = Step("control.delay");
            var loop = Loop(inside);
            editor.AddStep(loop);

            editor.SetSelection([inside]);
            editor.DeleteSelectedCommand.Execute(null);
            editor.UndoCommand.Execute(null);

            // Undo hands the list fresh copies, so what is read back is the shape and the pick.
            var restored = Assert.Single(editor.Steps);
            Assert.Equal("control.delay", Assert.Single(Inside(restored)).Type);
            Assert.Same(Inside(restored)[0], Assert.Single(editor.SelectedSteps));
        });
    }

    [Fact]
    public void Picking_a_block_title_picks_the_block_and_highlights_its_own_row()
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

            var title = editor.Rows.First(row => row.IsHead);
            var item = window.GetVisualDescendants().OfType<ListBoxItem>()
                .First(box => ReferenceEquals(box.DataContext, title));
            var point = item.TranslatePoint(new Point(item.Bounds.Width * 0.1, item.Bounds.Height / 2), window)
                ?? default;

            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(loop, Assert.Single(editor.SelectedSteps));

            var list = window.GetVisualDescendants().OfType<ListBox>().First();
            Assert.Contains(list.SelectedItems!.OfType<StepRow>(),
                row => row.IsStep && ReferenceEquals(row.Step, loop));
        });
    }

    [Fact]
    public void A_mouse_drag_can_carry_a_step_past_a_whole_block()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var first = Step("control.delay");
            var loop = Loop(Step("control.log"));
            editor.AddStep(first);
            editor.AddStep(loop);
            Dispatcher.UIThread.RunJobs();

            // The loop's title, the step inside it and the line closing it are all rows a drop
            // can be aimed at, so the block is five rows tall.
            var rows = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            Assert.Equal(5, rows.Count);

            var from = Waypoint(window, rows[0], 0.5, 0.5);
            var to = Waypoint(window, rows[4], 0.5, 0.9);
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([loop, first], editor.Steps);
            Assert.Single(Inside(loop));
        });
    }

    /// <summary>A point inside a row, as a fraction of its width and height, in window space.</summary>
    private static Point Waypoint(Window window, Visual row, double x, double y)
        => row.TranslatePoint(new Point(row.Bounds.Width * x, row.Bounds.Height * y), window) ?? default;
}
