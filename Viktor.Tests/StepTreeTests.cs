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

    private static StepParameter Text(string name, string value)
        => new() { Name = name, Kind = ActionParameterKind.Text, Value = value };

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
    public void A_list_that_only_takes_one_kind_of_step_says_so_where_the_step_is_added()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var branch = Step("control.case", Text("values", "a"), Body("body", Step("control.log")));
            var switchStep = Step("control.switch", Text("value", "$x"),
                new StepParameter { Name = "cases", Kind = ActionParameterKind.Steps, Steps = [branch] });
            var loop = Loop(Step("control.log"));

            editor.AddStep(switchStep);
            editor.AddStep(loop);

            // Nothing is being added, so nothing is being narrowed yet.
            Assert.Null(editor.InsertChoices);

            // A switch's list holds branches and nothing else, so that is all the dialog offers
            // when the step is meant for it. The restriction travels with the place the step is
            // going, not with the dialog.
            editor.AddInsideCommand.Execute(
                editor.Rows.First(row => row.IsHead && row.List!.Name == "cases"));
            Assert.Equal(["control.case"], editor.InsertChoices!.Select(action => action.Key));

            // A block that runs ordinary steps takes anything.
            editor.AddInsideCommand.Execute(
                editor.Rows.First(row => row.IsHead && ReferenceEquals(row.Step, loop)));
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
    public void A_macro_read_back_from_a_file_keeps_the_steps_inside_its_blocks()
    {
        Ui.Run(() =>
        {
            var json = new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = "probe",
                ["steps"] = new System.Text.Json.Nodes.JsonArray(
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["type"] = "control.repeat",
                        ["params"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["times"] = 2,
                            ["body"] = new System.Text.Json.Nodes.JsonArray(
                                new System.Text.Json.Nodes.JsonObject
                                {
                                    ["type"] = "control.delay",
                                    ["params"] = new System.Text.Json.Nodes.JsonObject { ["ms"] = 250 },
                                }),
                        },
                    }),
            };

            var loaded = MacroItem.FromJson(json);
            Assert.Equal("control.delay", loaded.Steps[0].StepLists.Single().Steps[0].Type);
        });
    }

    [Fact]
    public void The_steps_inside_a_block_can_be_edited_at_any_depth()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var deep = Step("control.delay");
            var middle = Loop(deep);
            var outer = Loop(middle);
            editor.AddStep(outer);

            var branch = Step("control.case", Text("values", "a"), Body("body", Step("control.log")));
            var switchStep = Step("control.switch", Text("value", "$x"),
                new StepParameter { Name = "cases", Kind = ActionParameterKind.Steps, Steps = [branch] });
            editor.AddStep(switchStep);

            foreach (var target in new[] { deep, Inside(branch)[0] })
            {
                editor.SetSelection([target]);
                MacroStep? asked = null;
                void Asked(MacroStep step) => asked = step;
                editor.EditStepRequested += Asked;
                editor.EditSelectedCommand.Execute(null);
                editor.EditStepRequested -= Asked;
                Assert.Same(target, asked);
            }

            editor.SetSelection([deep]);
            editor.ReplaceStep(deep, Step("control.log"));
            Assert.Equal(["control.log"], Inside(middle).Select(step => step.Type));
        });
    }

    [Fact]
    public void The_buttons_on_a_blocks_rows_do_what_they_say()
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

            // The plus on the block's title line asks for a step to go in there.
            Click(window, RowButton(window, row => row.IsHead));
            Dispatcher.UIThread.RunJobs();
            var picker = Assert.Single(window.OwnedWindows, owned => owned is AddActionWindow);
            picker.Close(null);
            Dispatcher.UIThread.RunJobs();

            // The fold arrow closes the block. A click is a press and a release on the same button,
            // and nothing on the way may take that button's pointer capture off it: a button that
            // loses the capture on the way up never reports a click at all.
            Assert.True(loop.IsExpanded);
            Click(window, RowButton(window, row => row.IsStep));
            Dispatcher.UIThread.RunJobs();
            Assert.False(loop.IsExpanded);
        });
    }

    [Fact]
    public void Pressing_the_empty_part_of_the_list_lets_the_selection_go()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var leaf = Step("control.delay");
            editor.AddStep(leaf);
            Dispatcher.UIThread.RunJobs();

            // Pick the step the way a user does, so the list really is holding a highlight of its
            // own: clearing only the editor's copy would leave the row looking picked.
            var list = window.GetVisualDescendants().OfType<ListBox>().First();
            Click(window, window.GetVisualDescendants().OfType<ListBoxItem>().First());
            Dispatcher.UIThread.RunJobs();
            Assert.Same(leaf, Assert.Single(editor.SelectedSteps));
            Assert.Single(list.SelectedItems!);

            // Below the last row there is nothing to pick, so a press there means "none of these".
            var empty = list.TranslatePoint(
                new Point(list.Bounds.Width / 2, list.Bounds.Height - 4), window) ?? default;
            window.MouseDown(empty, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(empty, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(editor.SelectedSteps);
            Assert.Empty(list.SelectedItems!);
        });
    }

    /// <summary>The visible button a row carries, such as the fold arrow or the one that adds.</summary>
    private static Button RowButton(Window window, Func<StepRow, bool> match)
        => window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Bounds.Width > 0
                && button.DataContext is StepRow row && match(row));

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

            // The plus on the inner block's title line is what says "a step goes in here".
            var head = editor.Rows.First(row => row.IsHead && ReferenceEquals(row.Step, inner));
            editor.AddInsideCommand.Execute(head);

            // The dialog that opens is about that list, so what it returns lands inside the inner
            // block rather than beside it.
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
            var item = window.GetVisualDescendants().OfType<ListBoxItem>()
                .First(box => ReferenceEquals(box.DataContext, inner));
            var point = item.TranslatePoint(
                new Point(item.Bounds.Width * 0.3, item.Bounds.Height / 2), window) ?? default;

            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left);
            // The real thing has a gap between the two clicks, so anything the first one queued has
            // run by the time the second arrives.
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(60);
            Dispatcher.UIThread.RunJobs();
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(Inside(loop)[0], Assert.Single(editor.SelectedSteps));
            Assert.Contains(window.OwnedWindows, owned => owned is AddActionWindow);

            // The dialog has to be about the step that was double-clicked, and what it returns has
            // to land back inside the block — not on the block that holds it.
            var dialog = window.OwnedWindows.OfType<AddActionWindow>().Single();
            var dialogModel = (AddActionViewModel)dialog.DataContext!;
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

            // Probe: does a press on the step inside the block pick that step?
            var inner = editor.Rows.First(row => row.IsStep && ReferenceEquals(row.Step, Inside(loop)[0]));
            var innerItem = window.GetVisualDescendants().OfType<ListBoxItem>()
                .First(box => ReferenceEquals(box.DataContext, inner));
            var innerPoint = innerItem.TranslatePoint(
                new Point(innerItem.Bounds.Width * 0.3, innerItem.Bounds.Height / 2), window) ?? default;
            window.MouseDown(innerPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(innerPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(Inside(loop)[0], Assert.Single(editor.SelectedSteps));

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

    [Fact]
    public void A_mouse_drag_onto_the_middle_of_a_block_puts_the_step_inside_it()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var inside = Step("control.log");
            var loop = Loop(inside);
            var outside = Step("control.delay");
            editor.AddStep(loop);
            editor.AddStep(outside);
            Dispatcher.UIThread.RunJobs();

            var rows = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            var from = Waypoint(window, rows[4], 0.5, 0.5);
            var to = Waypoint(window, rows[0], 0.5, 0.5);
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([inside, outside], Inside(loop));
            Assert.Same(loop, Assert.Single(editor.Steps));
        });
    }
}
