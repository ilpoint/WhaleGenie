using System.Text.Json.Nodes;
using Viktor.Core.Execution;

namespace Viktor.Core.Tests;

/// <summary>
/// The settings that decide how a step that keeps failing is tried again. The pause before each
/// attempt is worked out from the settings alone, so what a macro will do can be shown to the user
/// and tested without running anything.
/// </summary>
public class StepMetaTests
{
    private static StepMeta Retrying(int count, int delay, RetryBackoff backoff = RetryBackoff.Fixed)
        => new() { RetryCount = count, RetryDelayMs = delay, RetryBackoff = backoff };

    [Fact]
    public void A_fixed_pause_is_the_same_before_every_attempt()
    {
        var meta = Retrying(3, 500);

        Assert.Equal(500, meta.RetryDelayFor(1));
        Assert.Equal(500, meta.RetryDelayFor(2));
        Assert.Equal(500, meta.RetryDelayFor(3));
    }

    [Fact]
    public void A_doubling_pause_grows_with_every_attempt()
    {
        var meta = Retrying(4, 250, RetryBackoff.Doubling);

        Assert.Equal([250, 500, 1000, 2000],
            Enumerable.Range(1, 4).Select(attempt => meta.RetryDelayFor(attempt)));
    }

    [Fact]
    public void A_doubling_pause_stops_growing_at_the_cap()
    {
        // A pause left to double on its own would put a macro to sleep for hours without saying so.
        var meta = Retrying(40, 1000, RetryBackoff.Doubling);

        Assert.Equal(StepMeta.LongestPauseMs, meta.RetryDelayFor(20));
    }

    [Fact]
    public void The_cap_is_a_day_and_the_best_part_of_another()
        => Assert.Equal(46 * 60 * 60 * 1000, StepMeta.LongestPauseMs);

    [Fact]
    public void A_pause_the_user_wrote_is_waited_out_in_full()
    {
        // A long pause used to be cut down to half a minute on the way to the clock, so a macro
        // that asked to wait two hours waited thirty seconds instead and said nothing.
        var meta = Retrying(2, 2 * 60 * 60 * 1000);

        Assert.Equal(2 * 60 * 60 * 1000, meta.RetryDelayFor(1));
        Assert.Equal(2 * 60 * 60 * 1000, meta.RetryDelayFor(2));
    }

    [Fact]
    public void A_random_pause_stays_between_half_and_one_and_a_half_times()
    {
        var meta = Retrying(20, 400, RetryBackoff.Jitter);
        var dice = new Random(1);

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            Assert.InRange(meta.RetryDelayFor(attempt, dice), 200, 600);
        }
    }

    [Fact]
    public void The_backoff_survives_being_written_and_read_back()
    {
        var node = Retrying(2, 750, RetryBackoff.Doubling).ToJson();

        Assert.Equal("doubling", node["retryBackoff"]?.GetValue<string>());

        var again = StepMeta.FromJson(node);
        Assert.Equal(2, again.RetryCount);
        Assert.Equal(750, again.RetryDelayMs);
        Assert.Equal(RetryBackoff.Doubling, again.RetryBackoff);
    }

    [Fact]
    public void A_macro_written_before_backoff_existed_reads_as_a_fixed_pause()
    {
        var old = StepMeta.FromJson(new JsonObject { ["retry"] = 2, ["retryDelayMs"] = 300 });

        Assert.Equal(RetryBackoff.Fixed, old.RetryBackoff);
        Assert.Equal(300, old.RetryDelayFor(2));

        // A fixed pause is the default, so a step that asks for one writes nothing extra.
        Assert.Null(Retrying(2, 300).ToJson()["retryBackoff"]);
    }

    [Fact]
    public void Settings_a_step_does_not_use_are_left_out_of_the_macro_file()
    {
        Assert.True(StepMeta.Empty.IsEmpty);
        Assert.Empty(StepMeta.Empty.ToJson());

        // Turning the step off has to be written, or the macro would run it again next time.
        Assert.False(StepMeta.Empty.WithEnabled(false).IsEmpty);
        Assert.Equal(false, StepMeta.Empty.WithEnabled(false).ToJson()["enabled"]?.GetValue<bool>());
    }

}
