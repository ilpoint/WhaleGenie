using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Runtime;

// The questions that stand between the editor documents and a close, a hide, or a project change.
public sealed partial class ProjectSession
{
    /// <summary>
    /// Asks about the open macros before the window goes out of the way. The page draws the
    /// question when it is there; the native dialog is the fallback for a window whose page is not
    /// ready. Answers true only when the documents were saved or discarded and nothing is left
    /// unsaved; false keeps the window open.
    /// </summary>
    public async Task<bool> AskToLeaveEditorAsync()
    {
        if (_editor is not { HasUnsavedWork: true } editor)
        {
            return true;
        }

        if (_editorPrompt is not null
            && await _editorPrompt.AskAsync(editor.UnsavedDocuments) is { } answered)
        {
            // The page says "leave", but a document may still be dirty if its save was refused
            // between the answer and this check. Staying is safer than closing over that work.
            return answered && !editor.HasUnsavedWork;
        }

        return await ResolveEditorWorkAsync();
    }

    /// <summary>
    /// Answers whether the project may be replaced by another one. Editor documents are resolved
    /// first, because they are work the project does not hold; after that an open asks about an
    /// unsaved project, while reload/close-all already carry their own discard confirmation.
    /// </summary>
    public async Task<bool> PrepareProjectChangeAsync(ProjectChangeKind kind)
    {
        // An open asks about the project first: if that question is cancelled, the editor
        // documents must not have been closed or discarded for an open that will not happen.
        if (kind == ProjectChangeKind.Open)
        {
            if (_saver is { IsEnabled: true } saver)
            {
                await saver.FlushAsync();
            }

            if (_workspace.IsDirty)
            {
                var choice = await AskToSaveProject();
                if (choice == ConfirmChoice.Primary)
                {
                    if (!await _save())
                    {
                        return false;
                    }
                }
                else if (choice != ConfirmChoice.Secondary)
                {
                    return false;
                }
            }
        }

        if (_editor is { } editor)
        {
            if (editor.HasUnsavedWork && !await AskToLeaveEditorAsync())
            {
                return false;
            }

            try
            {
                await editor.CloseAllAsync();
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        if (kind == ProjectChangeKind.Open)
        {
            // The recovery draft follows the replacement's path/dirty changes and takes itself
            // back once there is nothing left to lose. Clearing it here would also clear it when
            // the open itself fails and the current project stays unsaved.
            _recovery?.Follow();
        }

        return true;
    }

    /// <summary>
    /// Asks about the open macros before the window is hidden. The project itself is not asked:
    /// hiding is not leaving, and the work is still there to save when the window comes back.
    /// </summary>
    private async Task PromptToHideAsync(Window window)
    {
        if (_asking)
        {
            return;
        }

        _asking = true;
        try
        {
            if (!await AskToLeaveEditorAsync())
            {
                return;
            }

            // A page can answer "leave" after a save was refused; the dirty document is the
            // reason to keep the window, not a formality to ignore.
            if (_editor is { HasUnsavedWork: true })
            {
                return;
            }

            _allowHide = true;
            try
            {
                window.Close();
            }
            finally
            {
                _allowHide = false;
            }
        }
        finally
        {
            _asking = false;
        }
    }
}
