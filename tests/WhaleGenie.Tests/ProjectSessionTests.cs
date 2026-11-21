using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The unsaved-work question the shell carries. The main window that used to own this rule is gone,
/// so the session is checked directly: a close that would throw work away asks first, a cancelled
/// answer leaves the window alone, and a window with nothing to lose closes without a dialog.
/// </summary>
public class ProjectSessionTests
{
    [Fact]
    public void Closing_with_unsaved_work_asks_and_only_leaves_when_the_answer_allows_it()
    {
        Ui.Run(() =>
        {
            var workspace = new ProjectWorkspace();
            workspace.AddMacro(new MacroItem { Name = "login" });

            var asked = 0;
            var answer = ConfirmChoice.Cancel;
            var window = new Window();
            var session = new ProjectSession(workspace, () => window,
                () => Task.FromResult(false), _ => { })
            {
                AskToSaveProject = () =>
                {
                    asked++;
                    return Task.FromResult(answer);
                },
            };
            window.Closing += (_, e) => session.OnClosing(e, window);
            window.Show();

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, asked);
            Assert.True(window.IsVisible);
            Assert.Single(workspace.Macros);

            answer = ConfirmChoice.Secondary;
            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, asked);
            Assert.False(window.IsVisible);
            session.Stop();
        });
    }

    [Fact]
    public void A_window_with_nothing_to_lose_closes_without_a_question()
    {
        Ui.Run(() =>
        {
            var asked = 0;
            var window = new Window();
            var session = new ProjectSession(new ProjectWorkspace(), () => window,
                () => Task.FromResult(false), _ => { })
            {
                AskToSaveProject = () =>
                {
                    asked++;
                    return Task.FromResult(ConfirmChoice.Cancel);
                },
            };
            window.Closing += (_, e) => session.OnClosing(e, window);
            window.Show();

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, asked);
            Assert.False(window.IsVisible);
            session.Stop();
        });
    }
}
