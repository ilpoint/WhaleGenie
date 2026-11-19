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

// The pages of the editor: a submacro page, the calls it groups, and renaming it.
public partial class MacroEditorWorkspaceTests
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
    public void Closing_a_submacro_tab_forgets_the_tab_and_keeps_the_submacro()
    {
        Ui.Run(() =>
        {
            var child = new MacroItem { Name = "child" };
            var parent = new MacroItem { Name = "parent" };
            parent.SubMacros.Add(child);
            var window = new MacroEditorWindow(parent, [parent]);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var root = (MacroEditorViewModel)window.DataContext!;
            var tabs = window.FindControl<ListBox>("DocumentTabs")!;
            var page = root.Documents[1];
            var close = tabs.GetVisualDescendants().OfType<Button>()
                .First(button => button.Name == "CloseDocumentButton"
                    && ReferenceEquals(button.DataContext, page));

            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // The tab goes, the submacro stays: it is still part of the macro, and the page is
            // what a save reads.
            Assert.Same(root, window.DataContext);
            Assert.Single(root.Documents);
            root.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("child", Assert.Single(window.Result!.Macro.SubMacros).Name);

            Ui.CloseAndAnswer(window);
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
        editor.LoadFrom(parent, []);
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
        editor.PrepareInsertionAt(editor.Steps, 1);

        editor.AddStepHere(Step("control.log"));

        Assert.Equal(["control.delay", "control.log", "control.delay"],
            editor.Steps.Select(step => step.Type));

        editor.PrepareInsertionAt(editor.Steps, 0);
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
            Ui.CloseAndAnswer(window);
        });
    }
}
