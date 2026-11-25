using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Editor;
using WhaleGenie.Models;
using WhaleGenie.Projects;
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
    public void Saving_the_open_macro_before_leaving_closes_without_a_project_question()
    {
        Ui.Run(() =>
        {
            var workspace = new ProjectWorkspace();
            var editor = new OpenMacros { Dirty = true };
            var askedProject = 0;
            var window = new Window();
            var session = new ProjectSession(workspace, () => window,
                () => Task.FromResult(true), _ => { }, editor)
            {
                AskToSaveEditor = () => Task.FromResult(ConfirmChoice.Primary),
                AskToSaveProject = () =>
                {
                    askedProject++;
                    return Task.FromResult(ConfirmChoice.Cancel);
                },
            };
            window.Closing += (_, e) => session.OnClosing(e, window);
            window.Show();

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, editor.Saved);
            Assert.Equal(0, askedProject);
            Assert.False(window.IsVisible);
            session.Stop();
        });
    }

    [Fact]
    public void Cancelling_the_macro_question_keeps_the_window_open()
    {
        Ui.Run(() =>
        {
            var editor = new OpenMacros { Dirty = true };
            var window = new Window();
            var session = new ProjectSession(new ProjectWorkspace(), () => window,
                () => Task.FromResult(true), _ => { }, editor)
            {
                AskToSaveEditor = () => Task.FromResult(ConfirmChoice.Cancel),
            };
            window.Closing += (_, e) => session.OnClosing(e, window);
            window.Show();

            window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, editor.Saved);
            Assert.Equal(0, editor.Discarded);
            Assert.True(window.IsVisible);
            session.Stop();
        });
    }

    [Fact]
    public void A_dirty_macro_counts_as_work_a_close_would_lose()
    {
        var editor = new OpenMacros { Dirty = true };
        var session = new ProjectSession(new ProjectWorkspace(), () => null,
            () => Task.FromResult(false), _ => { }, editor);

        Assert.True(session.HasUnsavedWork);
        editor.Dirty = false;
        Assert.False(session.HasUnsavedWork);
    }

    [Fact]
    public async Task The_native_close_question_saves_the_open_macro()
    {
        var editor = new OpenMacros { Dirty = true };
        var session = new ProjectSession(new ProjectWorkspace(), () => null,
            () => Task.FromResult(true), _ => { }, editor)
        {
            AskToSaveEditor = () => Task.FromResult(ConfirmChoice.Primary),
        };

        Assert.True(session.HasUnsavedEditorWork);
        Assert.True(await session.AskToLeaveEditorAsync());
        Assert.Equal(1, editor.Saved);
        Assert.False(session.HasUnsavedEditorWork);
    }

    [Fact]
    public async Task The_page_question_is_used_before_the_native_one()
    {
        var editor = new OpenMacros { Dirty = true };
        var prompt = new LeavePrompt { Answer = true, OnAnswer = () => editor.Dirty = false };
        var nativeAsked = 0;
        var session = new ProjectSession(new ProjectWorkspace(), () => null,
            () => Task.FromResult(true), _ => { }, editor, editorPrompt: prompt)
        {
            AskToSaveEditor = () =>
            {
                nativeAsked++;
                return Task.FromResult(ConfirmChoice.Cancel);
            },
        };

        Assert.True(await session.AskToLeaveEditorAsync());
        Assert.Equal(1, prompt.Asked);
        Assert.Equal(0, nativeAsked);
        Assert.Equal(["open"], prompt.Documents);
    }

    [Fact]
    public async Task The_page_cancel_keeps_the_window()
    {
        var editor = new OpenMacros { Dirty = true };
        var prompt = new LeavePrompt { Answer = false };
        var nativeAsked = 0;
        var session = new ProjectSession(new ProjectWorkspace(), () => null,
            () => Task.FromResult(true), _ => { }, editor, editorPrompt: prompt)
        {
            AskToSaveEditor = () =>
            {
                nativeAsked++;
                return Task.FromResult(ConfirmChoice.Primary);
            },
        };

        Assert.False(await session.AskToLeaveEditorAsync());
        Assert.Equal(0, nativeAsked);
        Assert.True(session.HasUnsavedEditorWork);
    }

    [Fact]
    public async Task No_page_answer_falls_back_to_the_native_question()
    {
        var editor = new OpenMacros { Dirty = true };
        var prompt = new LeavePrompt { Answer = null };
        var nativeAsked = 0;
        var session = new ProjectSession(new ProjectWorkspace(), () => null,
            () => Task.FromResult(true), _ => { }, editor, editorPrompt: prompt)
        {
            AskToSaveEditor = () =>
            {
                nativeAsked++;
                return Task.FromResult(ConfirmChoice.Primary);
            },
        };

        Assert.True(await session.AskToLeaveEditorAsync());
        Assert.Equal(1, nativeAsked);
        Assert.Equal(1, editor.Saved);
    }

    [Fact]
    public async Task A_page_that_says_leave_over_a_still_dirty_macro_keeps_the_window()
    {
        var editor = new OpenMacros { Dirty = true };
        var prompt = new LeavePrompt { Answer = true };
        var session = new ProjectSession(new ProjectWorkspace(), () => null,
            () => Task.FromResult(true), _ => { }, editor, editorPrompt: prompt);

        Assert.False(await session.AskToLeaveEditorAsync());
        Assert.True(session.HasUnsavedEditorWork);
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

    [Fact]
    public async Task A_project_change_resolves_editor_documents_before_replacing()
    {
        var editor = new OpenMacros { Dirty = true };
        var session = new ProjectSession(new ProjectWorkspace(), () => null,
            () => Task.FromResult(true), _ => { }, editor)
        {
            AskToSaveEditor = () => Task.FromResult(ConfirmChoice.Primary),
        };

        Assert.True(await session.PrepareProjectChangeAsync(ProjectChangeKind.Open));
        Assert.Equal(1, editor.Saved);
        Assert.Equal(1, editor.Closed);
        Assert.False(editor.Dirty);
    }

    [Fact]
    public async Task Cancelling_the_project_question_keeps_the_current_project()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "unsaved" });
        var session = new ProjectSession(workspace, () => null,
            () => Task.FromResult(true), _ => { })
        {
            AskToSaveProject = () => Task.FromResult(ConfirmChoice.Cancel),
        };

        Assert.False(await session.PrepareProjectChangeAsync(ProjectChangeKind.Open));
        Assert.Single(workspace.Macros);
    }

    [Fact]
    public async Task Discarding_the_project_lets_an_open_go_ahead()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "unsaved" });
        var session = new ProjectSession(workspace, () => null,
            () => Task.FromResult(false), _ => { })
        {
            AskToSaveProject = () => Task.FromResult(ConfirmChoice.Secondary),
        };

        Assert.True(await session.PrepareProjectChangeAsync(ProjectChangeKind.Open));
    }

    /// <summary>A page prompt a check can answer without a browser.</summary>
    private sealed class LeavePrompt : IEditorLeavePrompt
    {
        public bool? Answer { get; init; }

        public Action? OnAnswer { get; init; }

        public int Asked { get; private set; }

        public IReadOnlyList<string> Documents { get; private set; } = [];

        public Task<bool?> AskAsync(IReadOnlyList<string> documents)
        {
            Asked++;
            Documents = documents;
            OnAnswer?.Invoke();
            return Task.FromResult(Answer);
        }
    }

    /// <summary>Macros whose save result a check can decide.</summary>
    private sealed class OpenMacros : IEditorDocuments
    {
        public bool Dirty { get; set; }

        public int Saved { get; private set; }

        public int Discarded { get; private set; }

        public int Closed { get; private set; }

        public bool HasUnsavedWork => Dirty;

        public bool HasOpenDocuments => Dirty;

        public IReadOnlyList<string> UnsavedDocuments => Dirty ? ["open"] : [];

        public Task<EditorSaveAllResult> SaveAllAsync()
        {
            Saved++;
            Dirty = false;
            return Task.FromResult(new EditorSaveAllResult(true, []));
        }

        public Task DiscardAllAsync()
        {
            Discarded++;
            Dirty = false;
            return Task.CompletedTask;
        }

        public Task CloseAllAsync()
        {
            Closed++;
            Dirty = false;
            return Task.CompletedTask;
        }
    }
}
