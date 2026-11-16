using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// The draft both windows keep — the macro being written and the project behind it. What differs
/// between the two is only what a write puts on disk; when it is written, and the rule that keeps
/// one window from answering for another's work, is what is checked here.
/// </summary>
public class RecoveryDraftTests
{
    [Fact]
    public void A_change_that_makes_work_unsaved_is_written_at_once()
    {
        var writes = 0;
        var clears = 0;
        var draft = new RecoveryDraft(() => true, () => writes++, () => clears++);

        draft.Follow();

        Assert.Equal(1, writes);
        Assert.Equal(0, clears);
    }

    [Fact]
    public void A_draft_this_window_did_not_write_is_left_alone()
    {
        var writes = 0;
        var clears = 0;
        var draft = new RecoveryDraft(() => false, () => writes++, () => clears++);

        draft.Follow();
        draft.Stop();

        Assert.Equal(0, writes);
        Assert.Equal(0, clears);
    }

    /// <summary>
    /// The list comes up empty on every start, so "there is nothing dirty" is not the same as "the
    /// draft is this window's to drop": the work a stopped run left behind has not been offered yet.
    /// </summary>
    [Fact]
    public void The_work_becoming_clean_takes_back_only_what_this_window_wrote()
    {
        var dirty = true;
        var writes = 0;
        var clears = 0;
        var draft = new RecoveryDraft(() => dirty, () => writes++, () => clears++);
        draft.Follow();

        dirty = false;
        draft.Follow();

        Assert.Equal(1, writes);
        Assert.Equal(1, clears);
    }

    [Fact]
    public void Closing_lets_go_of_this_windows_draft_and_takes_it_back_only_once()
    {
        var dirty = true;
        var clears = 0;
        var draft = new RecoveryDraft(() => dirty, () => { }, () => clears++);

        // Nothing has been written by this window, so the draft on disk belongs to somebody else.
        draft.Stop();
        Assert.Equal(0, clears);

        draft.Follow();
        draft.Stop();
        draft.Stop();
        Assert.Equal(1, clears);
    }
}
