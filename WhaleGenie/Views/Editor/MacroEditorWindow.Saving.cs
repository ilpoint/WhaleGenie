using Avalonia.Controls;

namespace WhaleGenie.Views;

public partial class MacroEditorWindow : AppWindow
{
    // What a save ends with, said out loud: the page that blocks it, the project that would not
    // take it, or work that is now in the package. What each ending means for the pages themselves
    // is `EditorCommit`'s answer; this file only points at what the answer needs pointing at.
    /// <summary>
    /// Saves every open page as one macro and says what came of it. Closing afterwards is the way
    /// out from the tray that asked for the save; the toolbar's own save keeps the editor open.
    /// </summary>
    private bool SaveAll(bool closeAfterSave)
    {
        // The macros a page may call are gathered before anything is judged: a step that names one
        // the project no longer has would otherwise block a save it no longer should.
        RefreshProjectCallables();
        switch (_commit.Save())
        {
            case CommitOutcome.PageCannotSave blocked:
                SelectDocument(blocked.Page);
                OnSaveBlocked();
                return false;

            case CommitOutcome.ProjectRefused:
                RefuseCommit();
                return false;

            case CommitOutcome.Saved saved:
                _mustStayUnsaved = false;
                Result = saved.Draft;
                _settings.Find<TextBlock>("CommitRejectedNotice")!.IsVisible = false;
                _settings.Find<TextBlock>("SavedNotice")!.IsVisible = true;
                if (closeAfterSave)
                {
                    _closing = true;
                    _allowClose = true;
                    Close(saved.Draft);
                }

                return true;

            default:
                // An ending this window does not know is not one it can close on: the work stays
                // here, which is what every ending but a save already does.
                return false;
        }
    }

    /// <summary>
    /// The project would not take this snapshot: it changed while the window was open. Nothing is
    /// written and nothing is let go — the work stays in the editor, the recovery draft stays on
    /// disk, and closing still asks about it.
    /// </summary>
    private void RefuseCommit()
    {
        _mustStayUnsaved = true;
        NoteEdit();

        // What the project holds is not this session's work any more, so the window has not ended
        // with a macro: closing must not try to hand the same stale snapshot over a second time.
        Result = null;

        _settings.Find<TextBlock>("SavedNotice")!.IsVisible = false;
        _settings.Find<TextBlock>("CommitRejectedNotice")!.IsVisible = true;
        _recovery.Follow();
    }
}
