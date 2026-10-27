using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class MacroEditorWorkspaceTests
{
    [Fact]
    public void Child_definitions_get_their_own_document_page()
    {
        Ui.Run(() =>
        {
            var child = new MacroItem { Name = "child" };
            child.Steps.Add(Step("control.log"));
            var parent = new MacroItem { Name = "parent" };
            parent.SubMacros.Add(child);
            var window = new MacroEditorWindow(parent, [parent]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var root = Assert.IsType<MacroEditorViewModel>(window.DataContext);
            Assert.Equal(2, root.Documents.Count);
            Assert.Equal("child", root.Documents[1].Editor.Name);
            Assert.True(root.Documents[1].Editor.IsSubMacro);

            var tabs = window.FindControl<ListBox>("DocumentTabs")!;
            tabs.SelectedItem = root.Documents[1];
            Dispatcher.UIThread.RunJobs();
            Assert.Same(root.Documents[1].Editor, window.DataContext);
        });
    }

    [Fact]
    public void A_submacro_groups_only_calls_made_directly_by_the_active_page()
    {
        var child = new MacroItem { Name = "child" };
        var parent = new MacroItem { Name = "parent" };
        parent.Steps.Add(Call("child"));
        parent.SubMacros.Add(child);

        var editor = new MacroEditorViewModel();
        editor.LoadFrom(parent);
        var palette = new ActionPaletteViewModel();
        palette.SetSources(ActionCatalog.RunnableActions, editor.SubMacros, []);
        palette.SelectDock(ActionPaletteDock.SubMacros);

        var card = Assert.Single(palette.Cards);
        Assert.Equal(ActionPaletteCardKind.SubMacro, card.Kind);
        Assert.Equal("child", card.Title);
        Assert.False(card.HasCategoryLabel);
        Assert.IsType<SolidColorBrush>(card.Accent);
    }

    [Fact]
    public void A_drop_target_keeps_its_exact_slot_and_can_be_cancelled()
    {
        var editor = new MacroEditorViewModel();
        editor.AddStep(Step("control.delay"));
        editor.AddStep(Step("control.delay"));
        editor.PrepareInsertionAt(1, null);

        editor.AddStepHere(Step("control.log"));

        Assert.Equal(["control.delay", "control.log", "control.delay"],
            editor.Steps.Select(step => step.Type));

        editor.PrepareInsertionAt(0, null);
        editor.ClearPendingInsertion();
        editor.AddStepHere(Step("control.log"));
        Assert.Equal("control.log", editor.Steps[^1].Type);
    }

    [Fact]
    public void Renaming_a_submacro_updates_calls_to_it()
    {
        Ui.Run(() =>
        {
            var child = new MacroItem { Name = "child" };
            var parent = new MacroItem { Name = "parent" };
            parent.Steps.Add(Call("child"));
            parent.SubMacros.Add(child);
            var window = new MacroEditorWindow(parent, [parent]);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var root = (MacroEditorViewModel)window.DataContext!;

            root.Documents[1].Editor.Name = "renamed";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("renamed", Assert.Single(root.Steps).Text("macro"));
            window.Close();
        });
    }

    [Fact]
    public void Saving_any_page_keeps_the_parent_and_every_submacro()
    {
        Ui.Run(() =>
        {
            var child = new MacroItem { Name = "child" };
            child.Steps.Add(Step("control.delay"));
            var parent = new MacroItem { Name = "parent" };
            parent.SubMacros.Add(child);
            var window = new MacroEditorWindow(parent, [parent]);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var root = (MacroEditorViewModel)window.DataContext!;

            var tabs = window.FindControl<ListBox>("DocumentTabs")!;
            tabs.SelectedItem = root.Documents[1];
            Dispatcher.UIThread.RunJobs();
            var childEditor = (MacroEditorViewModel)window.DataContext!;
            childEditor.AddStep(Step("control.log"));
            root.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("parent", window.Result!.Name);
            var savedChild = Assert.Single(window.Result.SubMacros);
            Assert.Equal(["control.delay", "control.log"],
                savedChild.Steps.Select(step => step.Type));
        });
    }

    [Fact]
    public void The_run_library_contains_the_parent_and_its_submacros()
    {
        Ui.Run(() =>
        {
            var child = new MacroItem { Name = "child" };
            child.Steps.Add(Step("control.log"));
            var parent = new MacroItem { Name = "parent" };
            parent.SubMacros.Add(child);
            var window = new MacroEditorWindow(parent, [parent]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var names = window.BuildLibrary().Names;
            Assert.Contains("parent", names);
            Assert.Contains("child", names);
            window.Close();
        });
    }

    [Fact]
    public void An_action_card_has_a_private_drag_format()
    {
        var transfer = new ActionDragPayload(
            ActionPaletteCardKind.PublicMacro, "public-id", 9).Transfer();
        var read = ActionDragPayload.Read(transfer);

        Assert.NotNull(read);
        Assert.Equal(ActionPaletteCardKind.PublicMacro, read.Kind);
        Assert.Equal("public-id", read.Key);
        Assert.Equal(9, read.Version);
    }

    [Fact]
    public void Dropping_an_action_opens_the_editor_and_cancel_leaves_the_list_alone()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var editor = (MacroEditorViewModel)window.DataContext!;
            var list = window.FindControl<ListBox>("StepList")!;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            var over = Drag(list, DragDrop.DragOverEvent, transfer);
            Assert.Equal(DragDropEffects.Copy, over.DragEffects);

            Drag(list, DragDrop.DropEvent, transfer);
            Dispatcher.UIThread.RunJobs();
            var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
            Assert.False(action.StaysOpen);
            var actionView = Assert.IsType<ActionEditorView>(action.Content);
            Assert.False(actionView.FindControl<Grid>("PickerColumn")!.IsVisible);
            var form = (AddActionViewModel)action.DataContext!;
            form.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(editor.Steps);
            window.Close();
        });
    }

    [Fact]
    public void Dropping_an_action_inserts_it_only_when_the_form_is_saved()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var editor = (MacroEditorViewModel)window.DataContext!;
            var list = window.FindControl<ListBox>("StepList")!;

            Drag(list, DragDrop.DropEvent,
                new ActionDragPayload(ActionPaletteCardKind.Action, "control.log").Transfer());
            Dispatcher.UIThread.RunJobs();
            var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
            var form = (AddActionViewModel)action.DataContext!;
            Assert.True(form.CanSave, form.ValidationMessage);
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("control.log", Assert.Single(editor.Steps).Type);
            window.Close();
        });
    }

    [Fact]
    public void Saving_keeps_the_editor_open_and_updates_the_project_once()
    {
        Ui.Run(() =>
        {
            var mainModel = new MainViewModel();
            var main = new MainWindow
            {
                DataContext = mainModel,
                AskToSaveProject = () => Task.FromResult(ConfirmChoice.Secondary),
            };
            main.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = main.OpenEditor(null, null, draft: false)!;
            Dispatcher.UIThread.RunJobs();
            var model = (MacroEditorViewModel)editor.DataContext!;
            model.Name = "saved macro";
            model.AddStep(Step("control.log"));
            Dispatcher.UIThread.RunJobs();

            model.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(editor.IsVisible);
            var saved = Assert.Single(mainModel.Macros);
            Assert.Equal("saved macro", saved.Name);
            Assert.Equal("control.log", Assert.Single(saved.Steps).Type);
            Assert.True(editor.FindControl<TextBlock>("SavedNotice")!.IsVisible);

            model.AddStep(Step("control.delay"));
            Dispatcher.UIThread.RunJobs();
            Assert.False(editor.FindControl<TextBlock>("SavedNotice")!.IsVisible);

            model.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(mainModel.Macros);
            Assert.Equal(2, mainModel.Macros[0].Steps.Count);

            editor.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(mainModel.Macros);

            main.Close();
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public void Setting_a_variable_uses_an_existing_global_without_creating_one()
    {
        Ui.Run(() =>
        {
            var name = "existingGlobal";
            var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
                Guid.NewGuid().ToString("N"));
            using var persistence = PersistentVariables.UsePathForTests(
                Path.Combine(folder, "persistent-variables.json"));
            var existing = new VariableDefinition
            {
                Name = name,
                Scope = VariableScope.Global,
                Type = "text",
            };
            VariableCatalog.Globals.Add(existing);
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var root = (MacroEditorViewModel)window.DataContext!;
            var list = window.FindControl<ListBox>("StepList")!;
            Drag(list, DragDrop.DropEvent,
                new ActionDragPayload(ActionPaletteCardKind.Action,
                    "control.setVariable").Transfer());
            Dispatcher.UIThread.RunJobs();

            var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
            var form = (AddActionViewModel)action.DataContext!;
            form.Parameters.Single(parameter => parameter.Definition.Name == "name").Text = name;
            var scope = form.Parameters.Single(parameter => parameter.Definition.Name == "scope");
            scope.Option = scope.Choices.Single(option => option.Value == "global");
            form.Parameters.Single(parameter => parameter.Definition.Name == "value").Text = "one";
            Assert.True(form.CanSave, form.ValidationMessage);
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            var savedStep = Assert.Single(root.Steps);
            Assert.Equal(name, savedStep.Text("name"));
            Assert.Equal("global", savedStep.Text("scope"));
            Assert.Single(VariableCatalog.Globals, variable => variable.Name == name);
            Assert.Contains(root.VariableCenter.Wall.Shown,
                choice => choice.Name == name);

            PersistentVariables.Flush();
            VariableCatalog.Globals.Remove(existing);
            SharedVariables.Current.Remove(name);
            window.Close(null);
        });
    }

    [Fact]
    public void Setting_a_variable_can_create_a_persistent_global_in_the_same_save()
    {
        Ui.Run(() =>
        {
            var name = "2createdInsideAction";
            var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
                Guid.NewGuid().ToString("N"));
            using var persistence = PersistentVariables.UsePathForTests(
                Path.Combine(folder, "persistent-variables.json"));
            var previous = VariableCatalog.Globals.ToList();
            try
            {
                VariableCatalog.Globals.Clear();
                SharedVariables.Current.Remove(name);
                var macro = new MacroItem { Name = "probe" };
                var window = new MacroEditorWindow(macro, [macro]);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var root = (MacroEditorViewModel)window.DataContext!;
                var list = window.FindControl<ListBox>("StepList")!;
                Drag(list, DragDrop.DropEvent,
                    new ActionDragPayload(ActionPaletteCardKind.Action,
                        "control.setVariable").Transfer());
                Dispatcher.UIThread.RunJobs();

                var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
                var form = (AddActionViewModel)action.DataContext!;
                form.Parameters.Single(parameter => parameter.Definition.Name == "name").Text = name;
                var scope = form.Parameters.Single(parameter => parameter.Definition.Name == "scope");
                scope.Option = scope.Choices.Single(option => option.Value == "global");
                form.Parameters.Single(parameter => parameter.Definition.Name == "value").Text =
                    "$count + 1";
                Dispatcher.UIThread.RunJobs();

                Assert.True(form.ShowsGlobalPersistence);
                Assert.NotNull(form.GlobalVariableDraft);
                form.GlobalVariableDraft.Type = "number";
                form.GlobalVariableDraft.DefaultValue = "2";
                form.GlobalVariableDraft.PersistValue = true;
                Assert.True(form.Parameters
                    .Single(parameter => parameter.Definition.Name == "value").IsExpression);
                Assert.True(form.CanSave, form.ValidationMessage);
                form.SaveCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                var saved = Assert.Single(VariableCatalog.Globals,
                    variable => variable.Name == name);
                Assert.Equal("number", saved.Type);
                Assert.Equal("2", saved.DefaultValue);
                Assert.True(saved.PersistValue);
                Assert.True(SharedVariables.Current.TryGet(name, out var current));
                Assert.Equal(2, current.AsNumber());

                PersistentVariables.Flush();
                Assert.True(PersistentVariables.TryGet(name, out _));
                window.Close(null);
            }
            finally
            {
                VariableCatalog.Globals.Clear();
                foreach (var global in previous)
                {
                    VariableCatalog.Globals.Add(global);
                }

                SharedVariables.Current.Remove(name);
            }
        });
    }

    [Fact]
    public void Cancelling_a_set_variable_step_does_not_create_its_global()
    {
        Ui.Run(() =>
        {
            var name = "cancelledGlobal";
            var previous = VariableCatalog.Globals.ToList();
            VariableCatalog.Globals.Clear();
            try
            {
                var macro = new MacroItem { Name = "probe" };
                var window = new MacroEditorWindow(macro, [macro]);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var list = window.FindControl<ListBox>("StepList")!;
                Drag(list, DragDrop.DropEvent,
                    new ActionDragPayload(ActionPaletteCardKind.Action,
                        "control.setVariable").Transfer());
                Dispatcher.UIThread.RunJobs();

                var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
                var form = (AddActionViewModel)action.DataContext!;
                form.Parameters.Single(parameter => parameter.Definition.Name == "name").Text = name;
                var scope = form.Parameters.Single(parameter => parameter.Definition.Name == "scope");
                scope.Option = scope.Choices.Single(option => option.Value == "global");
                form.GlobalVariableDraft!.PersistValue = true;
                form.CancelCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                Assert.DoesNotContain(VariableCatalog.Globals,
                    variable => variable.Name == name);
                window.Close(null);
            }
            finally
            {
                VariableCatalog.Globals.Clear();
                foreach (var global in previous)
                {
                    VariableCatalog.Globals.Add(global);
                }
            }
        });
    }

    /// <summary>
    /// A loop whose body is still empty is one place, not four: its own row, the title of the body
    /// and the line that closes it all put the action in the same slot, so the band does not jump
    /// between four heights while the pointer moves down an empty body.
    /// </summary>
    /// <summary>
    /// Every pixel of an empty block answers the same way, walked one pixel at a time: the gaps
    /// between its rows belong to the row above, not to nothing, so the band does not flicker as
    /// the pointer goes down.
    /// </summary>
    [Fact]
    public void An_empty_block_body_answers_the_same_way_at_every_pixel()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(Step("control.repeat",
                new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Value = "2" },
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps }));
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<ListBox>("StepList")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var marker = window.FindControl<Border>("DropMarker")!;
            var block = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.DataContext is StepRow { IsHead: false, IsFoot: false }
                    row && row.Step.Type == "control.repeat");
            var foot = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.DataContext is StepRow { IsFoot: true });
            var top = block.TranslatePoint(new Point(0, 0), list)!.Value.Y;
            var bottom = foot.TranslatePoint(new Point(0, foot.Bounds.Height), list)!.Value.Y;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            var inside = new List<(string? Hint, double? Marker)>();
            for (var y = top; y < bottom; y += 1)
            {
                DragAt(list, DragDrop.DragOverEvent, transfer, new Point(60, y));
                inside.Add((hint.Text, Canvas.GetTop(marker)));
            }

            Assert.True(inside.Count > 20, $"only {inside.Count} pixels sampled");
            Assert.All(inside, entry => Assert.Equal(Strings.Get("Editor.DropIntoEmptyList"), entry.Hint));
            Assert.Single(inside.Select(entry => entry.Marker).Distinct());

            window.Close();
        });
    }

    /// <summary>
    /// The same walk with the loop sitting after another step, which is how one is usually added:
    /// nothing between the step above and the closing line of the loop may answer "before the
    /// block".
    /// </summary>
    [Fact]
    public void An_empty_block_body_after_a_step_never_answers_before_the_block()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(Step("control.delay",
                new StepParameter { Name = "ms", Kind = ActionParameterKind.Number, Value = "5" }));
            editor.AddStep(Step("control.repeat",
                new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Value = "2" },
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps }));
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<ListBox>("StepList")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var block = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.DataContext is StepRow { IsHead: false, IsFoot: false }
                    row && row.Step.Type == "control.repeat");
            var foot = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.DataContext is StepRow { IsFoot: true });
            var top = block.TranslatePoint(new Point(0, 0), list)!.Value.Y;
            var bottom = foot.TranslatePoint(new Point(0, foot.Bounds.Height), list)!.Value.Y;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            var seen = new List<string>();
            for (var y = top; y < bottom; y += 1)
            {
                DragAt(list, DragDrop.DragOverEvent, transfer, new Point(60, y));
                seen.Add($"{y:0}:{hint.Text}");
            }

            Assert.DoesNotContain(seen, line => line.EndsWith("插入到这一块之前", StringComparison.Ordinal));

            window.Close();
        });
    }

    [Fact]
    public void An_empty_block_body_is_a_single_drop_target()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(Step("control.repeat",
                new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Value = "2" },
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps }));
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<ListBox>("StepList")!;
            var marker = window.FindControl<Border>("DropMarker")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var block = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.DataContext is StepRow { IsHead: false, IsFoot: false }
                    row && row.Step.Type == "control.repeat");
            var foot = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.DataContext is StepRow { IsFoot: true });
            var top = block.TranslatePoint(new Point(0, 0), list)!.Value.Y;
            var bottom = foot.TranslatePoint(new Point(0, foot.Bounds.Height), list)!.Value.Y;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            // Every two pixels from the title of the block to the line that closes it, so a gap
            // between two of those rows cannot hide a second answer.
            var seen = new List<(string? Hint, double? Marker)>();
            for (var y = top; y < bottom; y += 2)
            {
                DragAt(list, DragDrop.DragOverEvent, transfer, new Point(60, y));
                seen.Add((hint.Text, Canvas.GetTop(marker)));
            }

            Assert.True(seen.Count > 4);
            Assert.All(seen, entry => Assert.Equal(Strings.Get("Editor.DropIntoEmptyList"), entry.Hint));
            Assert.Single(seen.Select(entry => entry.Marker).Distinct());

            window.Close();
        });
    }

    /// <summary>
    /// Once the body holds a step, the heights of the loop row mean different things again — before
    /// the block, or inside it at the end — and the band says which.
    /// </summary>
    [Fact]
    public void A_block_row_with_a_step_inside_still_offers_before_and_inside()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(Step("control.repeat",
                new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Value = "2" },
                new StepParameter
                {
                    Name = "body",
                    Kind = ActionParameterKind.Steps,
                    Steps = [Step("control.delay",
                        new StepParameter { Name = "ms", Kind = ActionParameterKind.Number, Value = "1" })],
                }));
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<ListBox>("StepList")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var block = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.DataContext is StepRow { IsHead: false, IsFoot: false }
                    row && row.Step.Type == "control.repeat");
            var top = block.TranslatePoint(new Point(0, 0), list)!.Value.Y;
            var bottom = block.TranslatePoint(new Point(0, block.Bounds.Height), list)!.Value.Y;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            DragAt(list, DragDrop.DragOverEvent, transfer, new Point(60, top + 3));
            Assert.Equal(Strings.Get("Editor.DropBeforeBlock"), hint.Text);

            DragAt(list, DragDrop.DragOverEvent, transfer, new Point(60, (top + bottom) / 2));
            Assert.Equal(Strings.Get("Editor.DropAtBlockEnd"), hint.Text);

            window.Close();
        });
    }

    [Fact]
    public void The_drop_band_says_where_the_action_will_land()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var list = window.FindControl<ListBox>("StepList")!;
            var marker = window.FindControl<Border>("DropMarker")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            DragAt(list, DragDrop.DragOverEvent, transfer, new Point(40, 40));
            Assert.True(marker.IsVisible);
            Assert.True(marker.Height >= 64);
            Assert.Equal(Strings.Get("Editor.DropIntoEmptyList"), hint.Text);

            DragAt(list, DragDrop.DragLeaveEvent, transfer, new Point(40, 40));
            Assert.False(marker.IsVisible);

            var loop = Step("control.repeat",
                new StepParameter
                {
                    Name = "times",
                    Kind = ActionParameterKind.Number,
                    Value = "2",
                },
                new StepParameter
                {
                    Name = "body",
                    Kind = ActionParameterKind.Steps,
                    Steps = [Step("control.log")],
                });
            var model = (MacroEditorViewModel)window.DataContext!;
            model.AddStep(loop);
            Dispatcher.UIThread.RunJobs();

            var block = list.GetVisualDescendants().OfType<ListBoxItem>()
                .First(item => item.DataContext is StepRow { IsStep: true, CanFold: true });
            var center = block.TranslatePoint(
                new Point(block.Bounds.Width / 2, block.Bounds.Height / 2), list);
            Assert.NotNull(center);
            DragAt(list, DragDrop.DragOverEvent, transfer, center!.Value);

            Assert.True(marker.IsVisible);
            Assert.Equal(Strings.Get("Editor.DropAtBlockEnd"), hint.Text);
            Assert.True(Canvas.GetLeft(marker) > 0);

            window.Close(null);
        });
    }

    [Fact]
    public void A_drop_below_an_expanded_block_appends_to_the_top_level()
    {
        Ui.Run(() =>
        {
            var child = Step("control.log");
            var loop = Step("control.repeat",
                new StepParameter
                {
                    Name = "times",
                    Kind = ActionParameterKind.Number,
                    Value = "2",
                },
                new StepParameter
                {
                    Name = "body",
                    Kind = ActionParameterKind.Steps,
                    Steps = [child],
                });
            loop.IsExpanded = true;
            var macro = new MacroItem { Name = "probe" };
            macro.Steps.Add(loop);

            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var list = window.FindControl<ListBox>("StepList")!;
            var foot = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.DataContext is StepRow
                {
                    IsFoot: true,
                    Step: var step,
                } && ReferenceEquals(step, loop));
            var top = foot.TranslatePoint(new Point(0, 0), list)!.Value.Y;
            var point = new Point(80, top + foot.Bounds.Height - 2);
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.delay").Transfer();

            DragAt(list, DragDrop.DropEvent, transfer, point);
            Dispatcher.UIThread.RunJobs();
            var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
            var form = (AddActionViewModel)action.DataContext!;
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["control.repeat", "control.delay"],
                editor.Steps.Select(step => step.Type));
            Assert.Equal("control.log",
                Assert.Single(loop.StepLists.Single().Steps).Type);

            window.Close(null);
        });
    }

    [Fact]
    public void Dragging_an_ordinary_action_over_cases_is_refused_before_the_form_opens()
    {
        Ui.Run(() =>
        {
            var switchStep = Step("control.switch",
                new StepParameter
                {
                    Name = "cases",
                    Kind = ActionParameterKind.Steps,
                },
                new StepParameter
                {
                    Name = "otherwise",
                    Kind = ActionParameterKind.Steps,
                });
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(switchStep);
            Dispatcher.UIThread.RunJobs();
            var list = window.FindControl<ListBox>("StepList")!;
            var marker = window.FindControl<Border>("DropMarker")!;
            var hint = window.FindControl<TextBlock>("DropHintText")!;
            var casesHead = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.DataContext is StepRow
                {
                    IsHead: true,
                    List: { Name: "cases" },
                });
            var point = casesHead.TranslatePoint(
                new Point(casesHead.Bounds.Width / 2, casesHead.Bounds.Height / 2), list)!.Value;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            var over = DragAt(list, DragDrop.DragOverEvent, transfer, point);
            Assert.Equal(DragDropEffects.None, over.DragEffects);
            Assert.True(marker.IsVisible);
            Assert.Contains("Rejected", marker.Classes);
            Assert.Equal(Strings.Format("Editor.PlaceTakesOnly",
                ActionCatalog.Find("control.case")!.LocalName), hint.Text);

            DragAt(list, DragDrop.DropEvent, transfer, point);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(window.OwnedWindows.OfType<ActionEditorWindow>());
            Assert.Empty(switchStep.StepLists.Single(parameter => parameter.Name == "cases").Steps);
            Assert.True(editor.HasPlacementNotice);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == editor.PlacementNotice);
            window.Close(null);
        });
    }

    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    private static MacroStep Call(string macro) => Step("control.runMacro",
        new StepParameter
        {
            Name = "macro",
            Kind = ActionParameterKind.Macro,
            Value = macro,
        });

    private static DragEventArgs Drag(Control target, RoutedEvent<DragEventArgs> what,
        DataTransfer transfer)
        => DragAt(target, what, transfer, new Point(8, 8));

    private static DragEventArgs DragAt(Control target, RoutedEvent<DragEventArgs> what,
        DataTransfer transfer, Point point)
    {
        var args = new DragEventArgs(what, transfer, target, point,
            KeyModifiers.None);
        target.RaiseEvent(args);
        return args;
    }
}
