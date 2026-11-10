using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Coming back to work: an untouched save, the notification area, and an unreadable file.
public partial class RecoveryTests
{
    /// <summary>
    /// Reading a macro in the editor and closing it again is not an edit. The list is what the
    /// project is, and a macro handed back saying what it said before must leave it exactly as it
    /// was: otherwise the work the next run is offered back is work nobody ever wrote, and the
    /// window has been away in the notification area while the editor was up, with nothing on
    /// screen saying anything was unsaved.
    /// </summary>
    [Fact]
    public void A_macro_opened_and_saved_back_untouched_leaves_the_project_saved()
    {
        InOwnFile(path => Ui.RunAsync(async () =>
        {
            var package = Path.Combine(Path.GetDirectoryName(path)!, "macros.wgmacro");

            var window = new MainWindow { DataContext = new MainViewModel() };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var viewModel = (MainViewModel)window.DataContext!;

            viewModel.AddMacro(Named("login"));
            viewModel.SavePackage(package);
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.HasUnsavedChanges, "a saved project counts as unsaved");
            Assert.False(File.Exists(path), "a saved project left a snapshot");

            var editor = window.OpenEditor(viewModel.Macros[0], viewModel.Macros[0], draft: false)!;
            Dispatcher.UIThread.RunJobs();

            var inEditor = (MacroEditorViewModel)editor.DataContext!;
            Assert.False(inEditor.IsDirty, "opening the editor from the list made it unsaved");
            Assert.False(File.Exists(path), "opening the editor from the list left a snapshot");

            var before = viewModel.Macros[0].ToJson().ToJsonString();
            inEditor.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before, viewModel.Macros[0].ToJson().ToJsonString());
            Assert.Equal(before, editor.Result!.ToJson().ToJsonString());
            Assert.False(viewModel.HasUnsavedChanges,
                "the editor handed back a macro nobody changed, and the list calls that a change");
            Assert.False(File.Exists(path), "a snapshot was left by an editor that changed nothing");

            CloseWithUnsavedWork(window);
            return true;
        }));
    }

    /// <summary>
    /// The window is announced again every time it comes back from the notification area, and the
    /// macro editor sends it there for the whole time it is open. What the snapshot holds by then is
    /// the work this run is already showing, so the question about the last run belongs to the start
    /// of this one and must not be asked a second time.
    /// </summary>
    [Fact]
    public void Coming_back_from_the_notification_area_does_not_ask_about_the_last_run_again()
    {
        InOwnFile(path =>
        {
            Ui.RunAsync(async () =>
            {
                RecoveryStore.SaveProject([Named("login")], [], null);

                var asked = 0;
                var window = new MainWindow
                {
                    DataContext = new MainViewModel(),
                    AskToRecover = _ =>
                    {
                        asked++;
                        return Task.FromResult(ConfirmChoice.Cancel);
                    },
                };

                // The way the program wires it: the question goes up once the window is on screen.
                window.Opened += (_, _) =>
                    Dispatcher.UIThread.Post(() => _ = window.OfferRecoveryAsync());
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, asked);

                // The question was put off rather than answered, so the work it is about stays
                // where it is.
                Assert.True(File.Exists(path));

                window.Hide();
                Dispatcher.UIThread.RunJobs();
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(1, asked);

                window.Close();
                Dispatcher.UIThread.RunJobs();
                return true;
            });
        });
    }

    [Fact]
    public void A_snapshot_that_cannot_be_read_is_not_offered()
    {
        InOwnFile(path =>
        {
            File.WriteAllText(path, "this is not a snapshot");

            Assert.Null(RecoveryStore.Load());
        });
    }
}
