using System;
using WhaleGenie.Models;

namespace WhaleGenie.Storage;

/// <summary>
/// The draft an editor leaves behind while it holds work the package does not: written the moment
/// the work first becomes unsaved, kept up to date while it stays that way, and taken back only if
/// this editor was the one that wrote it.
/// </summary>
/// <remarks>
/// The rule that matters is the last one. A draft left by a run that stopped belongs to nobody yet —
/// an editor opened and closed without touching anything must not answer for it, or the next start
/// offers back work the user never asked to recover. Keeping the "did I write it" question next to
/// the writing is what makes that rule hold in one place instead of at each exit.
/// </remarks>
internal sealed class EditorRecovery
{
    private readonly Func<bool> _hasUnsavedWork;
    private readonly Func<MacroItem> _snapshot;
    private readonly Func<string> _name;
    private readonly RecoveryWriter _writer;
    private bool _written;

    /// <summary>
    /// Writes what <paramref name="snapshot"/> builds under <paramref name="name"/> whenever
    /// <paramref name="hasUnsavedWork"/> says there is something to lose.
    /// </summary>
    public EditorRecovery(Func<bool> hasUnsavedWork, Func<MacroItem> snapshot, Func<string> name)
    {
        _hasUnsavedWork = hasUnsavedWork;
        _snapshot = snapshot;
        _name = name;
        _writer = new RecoveryWriter(Save);
    }

    /// <summary>
    /// The edit that turned "nothing to lose" into "something to lose". Written straight away: work
    /// pasted in and lost to a crash a moment later must not be waiting on a timer to be kept.
    /// </summary>
    public void FirstChange()
    {
        if (!_hasUnsavedWork())
        {
            Stop();
            return;
        }

        _written = true;
        _writer.Now();
    }

    /// <summary>
    /// Another edit while the work was already unsaved. The write waits for the changes to pause;
    /// what first made it unsaved is already on disk.
    /// </summary>
    public void Keep()
    {
        if (!_hasUnsavedWork())
        {
            return;
        }

        _written = true;
        _writer.Changed();
    }

    /// <summary>
    /// The work is no longer this editor's to keep — it was saved into the list, or the user let it
    /// go. Takes back this editor's own draft and leaves anyone else's alone.
    /// </summary>
    public void Stop()
    {
        _writer.Stop();
        if (!_written)
        {
            return;
        }

        _written = false;
        RecoveryStore.ClearEditor();
    }

    private void Save()
    {
        if (!_hasUnsavedWork())
        {
            return;
        }

        RecoveryStore.SaveEditor(_snapshot(), _name());
        _written = true;
    }
}
