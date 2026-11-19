using System;

namespace WhaleGenie.Storage;

/// <summary>
/// The draft a window leaves behind while it holds work the store does not: written the moment the
/// work first becomes unsaved, kept up to date while it stays that way, and taken back only if this
/// window was the one that wrote it.
/// </summary>
/// <remarks>
/// The rule that matters is the last one. A draft left by a run that stopped belongs to nobody yet —
/// a window opened and closed without touching anything must not answer for it, or the next start
/// offers back work the user never asked to recover. Keeping the "did I write it" question next to
/// the writing is what makes that rule hold in one place instead of at each exit. Two windows keep a
/// draft — the macro being written and the project behind it — and what differs between them is only
/// what a write puts on disk, which is why that part is handed in.
/// </remarks>
internal sealed class RecoveryDraft
{
    private readonly Func<bool> _hasUnsavedWork;
    private readonly Action _save;
    private readonly Action _clear;
    private readonly RecoveryWriter _writer;
    private bool _written;

    /// <summary>
    /// Writes through <paramref name="save"/> whenever <paramref name="hasUnsavedWork"/> says there
    /// is something to lose, and takes the draft back through <paramref name="clear"/> once it is
    /// this draft's to take.
    /// </summary>
    public RecoveryDraft(Func<bool> hasUnsavedWork, Action save, Action clear)
    {
        _hasUnsavedWork = hasUnsavedWork;
        _save = save;
        _clear = clear;
        _writer = new RecoveryWriter(Write);
    }

    /// <summary>
    /// What the draft stands for moved. Written straight away when there is something to lose: work
    /// pasted in and lost to a crash a moment later must not be waiting on a timer. When there is
    /// nothing left to lose, the draft is taken back instead.
    /// </summary>
    public void Follow()
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
    /// Another change while the work was already unsaved. The write waits for the changes to pause;
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
    /// The work is no longer this window's to keep — it was saved, or the user let it go. Takes back
    /// this window's own draft and leaves anyone else's alone.
    /// </summary>
    public void Stop()
    {
        _writer.Stop();
        if (!_written)
        {
            return;
        }

        _written = false;
        _clear();
    }

    private void Write()
    {
        if (!_hasUnsavedWork())
        {
            return;
        }

        _save();
        _written = true;
    }
}
