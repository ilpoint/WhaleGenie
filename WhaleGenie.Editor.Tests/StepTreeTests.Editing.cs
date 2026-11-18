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

// Editing at any depth: undo of a deletion, a macro read back from a file, and the selection kept.
public partial class StepTreeTests
{
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
            var switchStep = Step("control.switch", Text("value", "x"),
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
}
