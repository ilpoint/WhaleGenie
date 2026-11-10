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
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// What saving keeps: the parent, every submacro, the run library and the open editor.
public partial class MacroEditorWorkspaceTests
{
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
            var list = window.FindControl<TreeView>("StepList")!;
            var transfer = new ActionDragPayload(
                ActionPaletteCardKind.Action, "control.log").Transfer();

            var over = Drag(list, DragDrop.DragOverEvent, transfer);
            Assert.Equal(DragDropEffects.Copy, over.DragEffects);

            Drag(list, DragDrop.DropEvent, transfer);
            Dispatcher.UIThread.RunJobs();
            var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
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
            var list = window.FindControl<TreeView>("StepList")!;

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
            Ui.CloseAndAnswer(window);
        });
    }
}
