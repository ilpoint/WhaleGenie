using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

// A save that arrives after somebody else wrote the project, and the work that has to survive it.
public partial class MacroEditorWorkspaceTests
{
    [Fact]
    public void Replacing_a_macro_that_is_no_longer_listed_writes_nothing()
    {
        var workspace = new ProjectWorkspace();
        var gone = new MacroItem { Name = "gone" };

        Assert.False(workspace.ReplaceMacro(gone, new MacroItem { Name = "edited" }));
        Assert.Empty(workspace.Macros);
        Assert.False(workspace.IsDirty);
        Assert.Equal(0, workspace.Revision);
    }

    [Fact]
    public void Saving_after_the_project_changed_keeps_the_work_and_writes_nothing()
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
            model.Name = "mine";
            model.AddStep(Step("control.log"));
            Dispatcher.UIThread.RunJobs();

            model.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var committed = Assert.Single(mainModel.Macros);
            Assert.NotNull(editor.Result);

            // A draft applied from elsewhere replaces the list's instances, which is what MCP does:
            // the macro the editor was opened on is no longer the one the project holds.
            var theirs = MacroItem.FromJson(committed.ToJson());
            theirs.Name = "theirs";
            mainModel.Workspace.ApplyDraft([theirs], new ProjectTriggers(), []);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("theirs", Assert.Single(mainModel.Macros).Name);

            model.AddStep(Step("control.delay"));
            Dispatcher.UIThread.RunJobs();
            model.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Nothing was written over the version the project had, and the window kept the work
            // rather than closing on a snapshot nobody took.
            Assert.True(editor.IsVisible);
            Assert.Null(editor.Result);
            Assert.Equal("theirs", Assert.Single(mainModel.Macros).Name);

            var settings = editor.FindControl<MacroSettingsView>("MacroSettings")!;
            Assert.False(settings.FindControl<TextBlock>("SavedNotice")!.IsVisible);
            Assert.True(settings.FindControl<TextBlock>("CommitRejectedNotice")!.IsVisible);

            Ui.CloseAndDiscard(editor);
            Ui.CloseAndDiscard(main);
        });
    }
}
