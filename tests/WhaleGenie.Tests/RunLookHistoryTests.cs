using WhaleGenie.Contracts;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

/// <summary>
/// The frames one debugger session kept. A macro that loops over a search makes a picture every
/// time round, so the list is bounded; a step that looks again replaces its own earlier picture, and
/// picking one out of the list changes what is on show rather than running anything.
/// </summary>
public class RunLookHistoryTests
{
    [Fact]
    public void A_step_that_looks_again_replaces_its_own_picture()
    {
        var history = new LookHistory();

        history.Keep(Look("s1", "first"));
        history.Keep(Look("s2", "second"));
        history.Keep(Look("s1", "again"));

        Assert.Equal(2, history.Frames.Count);
        Assert.Equal("s2", history.Frames[0].StepId);
        Assert.Equal("s1", history.Frames[1].StepId);
        Assert.Equal("again", history.Frames[1].Looking);
        Assert.Equal("s1", history.Active!.StepId);
    }

    [Fact]
    public void The_list_keeps_the_newest_frames_and_lets_the_oldest_go()
    {
        var history = new LookHistory();
        for (var index = 0; index < LookHistory.Kept + 5; index++)
        {
            history.Keep(Look("s" + index, index.ToString()));
        }

        Assert.Equal(LookHistory.Kept, history.Frames.Count);
        Assert.Equal("s5", history.Frames[0].StepId);
        Assert.Equal("s" + (LookHistory.Kept + 4), history.Frames[^1].StepId);
    }

    [Fact]
    public void Picking_a_kept_frame_changes_what_is_on_show_and_nothing_else()
    {
        var history = new LookHistory();
        history.Keep(Look("s1", "first"));
        history.Keep(Look("s2", "second"));

        Assert.True(history.Select("s1"));
        Assert.Equal("s1", history.Active!.StepId);
        Assert.Equal(2, history.Frames.Count);

        Assert.False(history.Select("s9"));
        Assert.Equal("s1", history.Active!.StepId);
    }

    [Fact]
    public void Ending_the_session_lets_go_of_every_frame()
    {
        var history = new LookHistory();
        history.Keep(Look("s1", "first"));
        history.Keep(Look("s2", "second"));

        history.Clear();

        Assert.Empty(history.Frames);
        Assert.Null(history.Active);
        Assert.False(history.Select("s1"));
    }

    private static RunLook Look(string stepId, string looking)
        => new(stepId, "control.findImage", looking, "data:image/bmp;base64,AA==");
}
