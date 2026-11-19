using System;
using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// What happens when the editor is told to save: whether every open page can be written, what the
/// document amounts to as one macro, and whether the project took it.
/// </summary>
/// <remarks>
/// The order is the rule. A page that cannot save stops the attempt before anything is built, and
/// the project is asked before anything is let go: a macro that left the list while this window was
/// open cannot be written back, and clearing the dirty flags before hearing so would leave work
/// looking saved with nothing holding it. What is left to the window is what is said on screen —
/// which page to bring forward, which notice to show, whether to close — because a class cannot
/// point at a drawer.
/// </remarks>
internal sealed class EditorCommit(
    MacroEditorSession session,
    EditorDocumentPages pages,
    MacroEditorViewModel root,
    RecoveryDraft recovery)
{
    /// <summary>
    /// What the project does with the macro this editor built: it answers whether it took it. Null
    /// means nobody is holding the list — a check that opened an editor on its own — and the save
    /// then ends with the snapshot in hand.
    /// </summary>
    public Func<MacroDraft, bool>? Commit { get; set; }

    /// <summary>
    /// Writes the document into the project, or says why it could not. Only the ending that took the
    /// macro lets go of the work; the other two leave every page dirty and the draft on disk.
    /// </summary>
    public CommitOutcome Save()
    {
        if (pages.FirstOrDefault(page => !page.Editor.CanSave) is { } blocked)
        {
            return new CommitOutcome.PageCannotSave(blocked);
        }

        var draft = new MacroDraft(pages.Snapshot(),
            [.. root.TriggerEntries.Select(entry => entry.Copy())],
            [.. root.InterfaceLayoutsSnapshot()]);
        if (Commit is { } commit && !commit(draft))
        {
            return new CommitOutcome.ProjectRefused();
        }

        foreach (var page in pages)
        {
            page.Editor.IsDirty = false;
        }

        session.Saved();
        root.IsDirty = false;
        recovery.Stop();
        return new CommitOutcome.Saved(draft);
    }
}

/// <summary>
/// What one save ended in. Three endings, and each is a different thing for the window to say: a
/// page that has to be brought forward and explained, a project that no longer holds the macro, or
/// work that is now in the package.
/// </summary>
internal abstract record CommitOutcome
{
    private CommitOutcome()
    {
    }

    /// <summary>The macro is the project's now; it is also what the window ends with.</summary>
    public sealed record Saved(MacroDraft Draft) : CommitOutcome;

    /// <summary>One page cannot be written, and it is the one holding the reason.</summary>
    public sealed record PageCannotSave(MacroDocumentPageViewModel Page) : CommitOutcome;

    /// <summary>The project would not take the macro: it changed while this window was open.</summary>
    public sealed record ProjectRefused : CommitOutcome;
}
