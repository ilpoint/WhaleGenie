using System.Collections.Generic;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// Opening the macro editor, and what the window does with what the editor hands back.
public partial class MainWindow : AppWindow
{
    /// <summary>
    /// Opens the macro editor on a macro: one from the list, a new one (<paramref name="editing"/> is
    /// null), or a draft recovered from a run that stopped without notice. What that macro replaces
    /// when it is saved is <paramref name="existing"/>, which is null for a new one. It answers the
    /// editor back so that a check can write in it; nothing else reads that.
    /// </summary>
    internal MacroEditorWindow? OpenEditor(MacroItem? editing, MacroItem? existing, bool draft,
        MacroView? newView = null, IReadOnlyList<TriggerEntry>? entries = null)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return null;
        }

        // Two editors on one macro would each hand back their own version and the last one closed
        // would win, so the editor already open is the one wanted; it is brought to the front.
        if (_editor is { } open)
        {
            open.Activate();
            return open;
        }

        // The package path is what a picture taken from the screen is stored beside, and the device
        // layer is the program's own, so the trials and the runs started from here use the one layer
        // whose text recognition models are already loaded.
        var editor = new MacroEditorWindow(editing, [.. viewModel.Macros], viewModel.CurrentPath,
            _devices, viewModel.Triggers, callableOnly: false, newView,
            viewModel.Persisted, entries: entries);

        // Recovered work is unsaved by definition, so closing that editor has to ask about it rather
        // than let it go without a word.
        if (draft)
        {
            editor.MarkUnsaved();
        }

        var commit = new ProjectMacroCommit(viewModel.Workspace, existing, newView ?? MacroView.All,
            macro => _triggers?.Stop(macro.Name),
            () => RefreshRunning(viewModel),
            () => _session?.Follow());
        _editor = editor;
        editor.Commit = draft => commit.Apply(draft, editor.LiftedMacros);
        editor.Closed += (_, _) => OnEditorClosed(editor, commit);

        // The editor stands beside the list rather than replacing it, so both stay on screen and
        // either can be minimized without taking the other down.
        editor.ShowAsPeer(this);
        return editor;
    }

    /// <summary>
    /// Takes what the editor ended with: the macro joins the list, or the one that was edited
    /// replaces it. Then the list comes back, unless the user is on their way out of the program.
    /// </summary>
    private void OnEditorClosed(MacroEditorWindow editor, ProjectMacroCommit commit)
    {
        _editor = null;

        if (editor.SteppedAside && !(Tray?.IsLeaving ?? false))
        {
            Tray?.ComeBack();
        }

        if (editor.Result is { } draft)
        {
            commit.Apply(draft, editor.LiftedMacros);
        }
    }
}
