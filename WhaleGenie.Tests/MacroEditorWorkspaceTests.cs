using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using WhaleGenie.Core.Execution;
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
    public void Dropping_an_action_opens_the_modal_and_cancel_leaves_the_list_alone()
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
    {
        var args = new DragEventArgs(what, transfer, target, new Point(8, 8),
            KeyModifiers.None);
        target.RaiseEvent(args);
        return args;
    }
}
