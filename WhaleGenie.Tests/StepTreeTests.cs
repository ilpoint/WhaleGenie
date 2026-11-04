using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

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
            var branch = Step("control.case", Text("values", "a"),
                Body("body", Step("control.log")));
            var choice = Step("control.switch",
                new StepParameter
                {
                    Name = "cases",
                    Kind = ActionParameterKind.Steps,
                    Steps = [branch],
                },
                new StepParameter { Name = "otherwise", Kind = ActionParameterKind.Steps });

            var editor = new MacroEditorViewModel();
            editor.AddStep(choice);

            // Two lists would read as one run of steps without a name each, so both get a row.
            Assert.Equal(["cases", "otherwise"],
                editor.Rows.Where(row => row.IsSection && row.Depth == 1)
                    .Select(row => row.List!.Name));
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
    public void The_fold_arrow_is_the_only_control_a_row_carries()
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

            // A row has no buttons of its own: the arrow belongs to the tree, and what goes inside
            // a block is a drop on the block's row or a step added while the block is picked.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Panel>()
                .Where(panel => panel.Name == "RowHeader")
                .SelectMany(panel => panel.GetVisualDescendants().OfType<Button>()),
                button => button.IsEffectivelyVisible);

            var item = Row(window, row => ReferenceEquals(row.Step, loop));
            Assert.True(loop.IsExpanded);
            item.IsExpanded = false;
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
            var list = window.FindControl<TreeView>("StepList")!;
            Click(window, window.FindControl<TreeView>("StepList")!.GetVisualDescendants().OfType<TreeViewItem>().First());
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

    [Fact]
    public void Selecting_every_step_keeps_the_list_where_it_is()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            macro.Steps.AddRange(Enumerable.Range(0, 80).Select(_ => Step("control.delay")));
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var list = window.FindControl<TreeView>("StepList")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);

            scroll.Offset = new Vector(0, 0);
            list.Focus();
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(editor.Steps.Count, editor.SelectedSteps.Count);
            Assert.Equal(0, scroll.Offset.Y);
        });
    }

    [Fact]
    public void Undoing_a_deletion_keeps_the_list_where_it_is()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            macro.Steps.AddRange(Enumerable.Range(0, 80).Select(_ => Step("control.delay")));
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var list = window.FindControl<TreeView>("StepList")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Vector(0, 0);
            list.Focus();
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            editor.DeleteSelectedCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var offsets = new List<double>();
            scroll.ScrollChanged += (_, _) => offsets.Add(scroll.Offset.Y);
            editor.UndoCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(editor.Steps.Count, editor.SelectedSteps.Count);
            Assert.All(offsets, offset => Assert.Equal(0, offset));
            Assert.Equal(0, scroll.Offset.Y);
        });
    }

    /// <summary>The visible button a row carries, such as the one that adds a step to a block.</summary>
    private static Button RowButton(Window window, Func<StepRow, bool> match)
        => window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Bounds.Width > 0
                && button.DataContext is StepRow row && match(row));

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

    [Fact]
    public void A_section_title_picks_the_block_it_belongs_to()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var choice = Step("control.switch",
                new StepParameter { Name = "cases", Kind = ActionParameterKind.Steps },
                new StepParameter { Name = "otherwise", Kind = ActionParameterKind.Steps });
            editor.AddStep(choice);
            Dispatcher.UIThread.RunJobs();

            // A section title stands for the whole block: picking it picks the block, and the
            // highlight is put back on the block's own row because that is the row commands act on.
            var title = editor.Rows.First(row => row.IsSection);
            var item = window.FindControl<TreeView>("StepList")!.GetVisualDescendants().OfType<TreeViewItem>()
                .First(box => ReferenceEquals(box.DataContext, title));
            var point = item.TranslatePoint(new Point(item.Bounds.Width * 0.1, item.Bounds.Height / 2), window)
                ?? default;

            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(choice, Assert.Single(editor.SelectedSteps));

            var list = window.FindControl<TreeView>("StepList")!;
            Assert.Contains(list.SelectedItems!.OfType<StepRow>(),
                row => row.IsStep && ReferenceEquals(row.Step, choice));
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

            // The loop's row, its body row and the step inside it, plus the step before it.
            var rows = window.FindControl<TreeView>("StepList")!.GetVisualDescendants().OfType<TreeViewItem>().ToList();
            Assert.Equal(4, rows.Count);

            var from = Waypoint(window, rows[0], 0.5, 0.5);

            // Below the whole tree is past everything, which is where this drag ends.
            var list = window.FindControl<TreeView>("StepList")!;
            var to = list.TranslatePoint(new Point(200, list.Bounds.Height - 4), window)!.Value;
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

            // The middle of the block's own row, not of the container it shares with its children.
            var header = Header(window, row => row.IsStep && ReferenceEquals(row.Step, loop));
            var from = Waypoint(window, Header(window, row => ReferenceEquals(row.Step, outside)), 0.5, 0.5);
            var to = Waypoint(window, header, 0.5, 0.5);
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([inside, outside], Inside(loop));
            Assert.Same(loop, Assert.Single(editor.Steps));
        });
    }

    /// <summary>
    /// 步骤行上写着这一步的输入往哪儿送。手点没问题、写进宏却不动，是写宏最容易踩的坑，而三种送法
    /// 的代价各不一样：前台要求窗口在最前，后台消息要求窗口名字对，驱动级要求装了驱动、开着服务端。
    /// 所以它像步骤名一样写在行上，不用打开这一步就能看见。
    /// </summary>
    [Fact]
    public void A_step_row_says_where_its_input_goes()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            editor.AddStep(Step("input.keyPress"));
            editor.AddStep(Step("input.keyPress", InputMode("background")));
            editor.AddStep(Step("control.log"));

            var rows = editor.Rows.Where(row => row.IsStep).ToList();

            // 目录里那一格默认就是前台，所以没写这个字段的按键步骤也是前台；记一行日志的动作
            // 根本没有这一格，行上什么也不画。
            Assert.Equal(Strings.Get("Editor.InputForeground"), rows[0].InputMark);
            Assert.Equal(Strings.Get("Editor.InputBackground"), rows[1].InputMark);
            Assert.Equal(Strings.Get("Editor.InputDriver"),
                Step("input.keyPress", InputMode("driver")).InputMark);
            Assert.False(rows[2].HasInputMark);
            Assert.Empty(rows[2].InputMark);
        });
    }

    /// <summary>The mark is only worth having if the list draws it.</summary>
    [Fact]
    public void The_step_list_draws_the_input_mark()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "marks" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(Step("input.keyPress", InputMode("driver")));
            Dispatcher.UIThread.RunJobs();

            var drawn = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(block => block.Text)
                .ToList();

            Assert.Contains(Strings.Get("Editor.InputDriver"), drawn);
        });
    }

    private static StepParameter InputMode(string value)
        => new() { Name = "inputMode", Kind = ActionParameterKind.Text, Value = value };
}
