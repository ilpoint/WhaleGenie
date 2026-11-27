using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Editor;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Runtime;

/// <summary>
/// The part of a window that belongs to the project rather than to its drawing: the crash draft,
/// the question before the program stops with unsaved work, and the offer to bring back what a
/// stopped run left behind. A window owns one of these; the session owns the rules.
/// </summary>
/// <remarks>
/// It lives beside the window instead of inside it so the same rules can be used by the web shell
/// after the main window is gone. The window supplies only what it alone can do: where the save
/// picker goes and how the question is drawn. The rest — what counts as unsaved, when a draft is
/// written, and which answer lets the close through — is here.
/// </remarks>
public sealed partial class ProjectSession
{
    private readonly ProjectWorkspace _workspace;

    private readonly Func<Window?> _owner;

    private readonly Func<Task<bool>> _save;

    private readonly Action<RecoveryContents> _restore;

    /// <summary>The open macro documents; their unsaved work is part of what a close would lose.</summary>
    private readonly IEditorDocuments? _editor;

    /// <summary>The one writer of the package, or null when this shell has none.</summary>
    private readonly IProjectSaver? _saver;

    /// <summary>The page's question about open macros; null when this shell has no page prompt.</summary>
    private readonly IEditorLeavePrompt? _editorPrompt;

    private RecoveryDraft? _recovery;

    private bool _allowClose;

    private bool _allowHide;

    private bool _asking;

    private bool _offered;

    public ProjectSession(ProjectWorkspace workspace, Func<Window?> owner, Func<Task<bool>> save,
        Action<RecoveryContents> restore, IEditorDocuments? editor = null,
        IProjectSaver? saver = null, IEditorLeavePrompt? editorPrompt = null)
    {
        _workspace = workspace;
        _owner = owner;
        _save = save;
        _restore = restore;
        _editor = editor;
        _saver = saver;
        _editorPrompt = editorPrompt;
        AskToSaveProject = DefaultAskToSave;
        AskToSaveEditor = DefaultAskToSaveEditor;
        ShowBlockedEditor = DefaultShowBlockedEditor;
        AskToRecover = DefaultAskToRecover;
    }

    /// <summary>The notification icon that turns a plain close into the program leaving.</summary>
    public AppTray? Tray { get; set; }

    /// <summary>The question about unsaved work; a check replaces it without a dialog.</summary>
    public Func<Task<ConfirmChoice>> AskToSaveProject { get; set; }

    /// <summary>The question about open macro documents, replaceable for the same reason.</summary>
    public Func<Task<ConfirmChoice>> AskToSaveEditor { get; set; }

    /// <summary>What a save-all that was refused is reported with.</summary>
    public Func<EditorSaveAllResult, Task> ShowBlockedEditor { get; set; }

    /// <summary>The offer to restore a stopped run's work; replaceable for the same reason.</summary>
    public Func<RecoveryContents, Task<ConfirmChoice>> AskToRecover { get; set; }

    /// <summary>Whether an open macro document holds edits the project has not received yet.</summary>
    public bool HasUnsavedEditorWork => _editor is { HasUnsavedWork: true };

    /// <summary>
    /// Whether stopping the program would throw away something the user made: the project has
    /// changes, or an open macro document has edits it has not handed to the project yet.
    /// </summary>
    public bool HasUnsavedWork => (_workspace.IsDirty
            && (_workspace.Macros.Count > 0 || _workspace.CurrentPath is not null))
        || _editor is { HasUnsavedWork: true };

    /// <summary>Keeps the crash draft in step with the project, and remembers what it wrote.</summary>
    public void Watch()
    {
        _recovery = new RecoveryDraft(() => HasUnsavedWork,
            () => RecoveryStore.SaveProject([.. _workspace.Macros], _workspace.Triggers,
                [.. _workspace.Variables.Globals], _workspace.CurrentPath),
            RecoveryStore.ClearProject);

        _workspace.PropertyChanged += OnWorkspacePropertyChanged;
        _workspace.Changed += OnWorkspaceChanged;
        _workspace.Replaced += OnWorkspaceReplaced;
        _recovery.Follow();
    }

    /// <summary>Lets the draft follow a change the window made without raising a project change.</summary>
    public void Follow() => _recovery?.Follow();

    /// <summary>Stops the draft; the window is going away.</summary>
    public void Stop()
    {
        _workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        _workspace.Changed -= OnWorkspaceChanged;
        _workspace.Replaced -= OnWorkspaceReplaced;
        _recovery?.Stop();
        _recovery = null;
    }

    /// <summary>
    /// Offers back whatever the last run left unsaved. The list comes up empty, so this is the one
    /// moment the snapshot can be read: the work it holds is not in any package yet. It is asked
    /// once for the whole run.
    /// </summary>
    public async Task OfferRecoveryAsync()
    {
        if (_offered)
        {
            return;
        }

        _offered = true;

        if (RecoveryStore.Load() is not { } contents)
        {
            return;
        }

        switch (await AskToRecover(contents))
        {
            case ConfirmChoice.Primary:
                _restore(contents);
                break;
            case ConfirmChoice.Secondary:
                // The work is not wanted, so it goes for good rather than being asked about again
                // on the next run.
                RecoveryStore.Clear();
                break;
            default:
                // Put off, not answered: the snapshot stays, so the next run asks again.
                break;
        }
    }

    /// <summary>
    /// Asks about the project before the program stops. A plain close is not the moment for this:
    /// the window only goes out of the way, and everything in it is still there to be saved later.
    /// </summary>
    public void OnClosing(WindowClosingEventArgs e, Window window)
    {
        if (_asking && !_allowClose && !_allowHide)
        {
            e.Cancel = true;
            return;
        }

        if (Leaving)
        {
            if (!_allowClose && HasUnsavedWork)
            {
                // The answer is given before the base call, because the base call is what raises
                // the Closing event and the way out reads the answer there.
                e.Cancel = true;

                // The question itself goes up one turn later: the dialog is a window of its own, and
                // opening one while this one is being closed is not a position to be in.
                Dispatcher.UIThread.Post(() => _ = PromptToSaveProjectAsync(window));
            }

            return;
        }

        // A plain close only hides the main window. The editor documents belong to the separate
        // workbench now; hiding the main shell leaves that window and its documents where they
        // are. The workbench has its own close question for them.
    }

    private bool Leaving => Tray is null || Tray.IsLeaving;

    private async Task PromptToSaveProjectAsync(Window window)
    {
        if (_asking)
        {
            return;
        }

        _asking = true;
        try
        {
            // The window is usually away in the notification area, and a question nobody can see
            // is no question at all: it comes back so the answer can be given.
            if (!window.IsVisible)
            {
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
            }

            // An open macro is asked about first: it belongs to the project but the project has
            // not received it yet, and saving it is what makes the project question below mean
            // something. "Not saved" closes the document instead of committing a version the
            // person said they did not want.
            if (_editor is { HasUnsavedWork: true }
                && !await AskToLeaveEditorAsync())
            {
                Tray?.CalledOff();
                return;
            }

            // With automatic saving on, the project the editor just wrote into is put on disk
            // without another question. A failed write stays unsaved and falls through to the
            // ordinary project question, which can report the failure and try again.
            if (_saver is { IsEnabled: true } autoSave)
            {
                await autoSave.FlushAsync();
            }

            if (_workspace.IsDirty)
            {
                var choice = await AskToSaveProject();

                if (choice == ConfirmChoice.Primary)
                {
                    // A save that was called off — the file picker dismissed — is an answer as
                    // well: the project is still unsaved, so the program stays.
                    if (!await _save())
                    {
                        Tray?.CalledOff();
                        return;
                    }
                }
                else if (choice != ConfirmChoice.Secondary)
                {
                    Tray?.CalledOff();
                    return;
                }
            }

            // The program is going with nothing carried over: the project was just written out,
            // or the user said the unsaved work is not wanted, so the snapshot has no business
            // surviving to be offered back on the next run.
            RecoveryStore.ClearProject();

            _allowClose = true;
            window.Close();
        }
        finally
        {
            _asking = false;
        }
    }

    /// <summary>
    /// Asks about the macro documents before the project question. Answers false when the person
    /// cancelled or a document still has a problem that keeps it from being saved; the program
    /// stays in both cases.
    /// </summary>
    private async Task<bool> ResolveEditorWorkAsync()
    {
        if (_editor is not { } editor)
        {
            return true;
        }

        var choice = await AskToSaveEditor();

        switch (choice)
        {
            case ConfirmChoice.Primary:
                {
                    var result = await editor.SaveAllAsync();
                    if (result.Saved)
                    {
                        return true;
                    }

                    await ShowBlockedEditor(result);
                    return false;
                }
            case ConfirmChoice.Secondary:
                await editor.DiscardAllAsync();
                return true;
            default:
                return false;
        }
    }

    /// <summary>The default editor question, drawn by the window that is closing.</summary>
    private async Task<ConfirmChoice> DefaultAskToSaveEditor()
    {
        if (Owner() is not { } owner)
        {
            return ConfirmChoice.Cancel;
        }

        var names = string.Join(Environment.NewLine, _editor?.UnsavedDocuments ?? []);
        return await ConfirmDialog.ShowAsync(owner,
            Strings.Get("Editor.UnsavedTitle"),
            Strings.Format("Editor.UnsavedExitMessage", names),
            Strings.Get("Editor.Save"), Strings.Get("Editor.Discard"), true, height: 300);
    }

    /// <summary>The default report for a document that refused to be saved.</summary>
    private async Task DefaultShowBlockedEditor(EditorSaveAllResult result)
    {
        if (Owner() is not { } owner)
        {
            return;
        }

        var detail = string.Join(Environment.NewLine, result.Blocked.Select(document =>
            Strings.Format("Editor.UnsavedBlockedLine", document.Name, document.Problem)));
        await ConfirmDialog.ShowAsync(owner,
            Strings.Get("Editor.UnsavedBlockedTitle"), detail,
            Strings.Get("Common.Ok"), null, showCancel: false, height: 300);
    }

    private async Task<ConfirmChoice> DefaultAskToSave()
    {
        if (Owner() is not { } owner)
        {
            return ConfirmChoice.Cancel;
        }

        return await ConfirmDialog.ShowAsync(owner,
            Strings.Get("Main.UnsavedTitle"),
            Strings.Get("Main.UnsavedMessage"),
            Strings.Get("Main.UnsavedSave"),
            Strings.Get("Main.UnsavedDiscard"));
    }

    private async Task<ConfirmChoice> DefaultAskToRecover(RecoveryContents contents)
    {
        if (Owner() is not { } owner)
        {
            return ConfirmChoice.Cancel;
        }

        return await ConfirmDialog.ShowAsync(owner,
            Strings.Get("Recover.Title"),
            RecoveryMessage(contents),
            Strings.Get("Recover.Restore"),
            Strings.Get("Recover.Discard"),
            height: 280);
    }

    private Window? Owner() => _owner();

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectWorkspace.IsDirty)
            or nameof(ProjectWorkspace.CurrentPath))
        {
            _recovery?.Follow();
        }
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e) => _recovery?.Keep();

    private void OnWorkspaceReplaced(object? sender, EventArgs e) => _recovery?.Keep();

    /// <summary>Says what the snapshot holds, one place at a time, for the question.</summary>
    public static string RecoveryMessage(RecoveryContents contents)
    {
        var found = new List<string>();
        if (contents.Macros.Count > 0 || contents.PackagePath is not null)
        {
            found.Add(Strings.Format("Recover.Project", contents.Macros.Count));
        }

        return Strings.Format("Recover.Message", string.Join(Environment.NewLine, found));
    }
}
