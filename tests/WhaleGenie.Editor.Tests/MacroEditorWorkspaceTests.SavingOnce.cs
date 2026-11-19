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

// One save against the project, and the editor staying open after it.
public partial class MacroEditorWorkspaceTests
{
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
}
