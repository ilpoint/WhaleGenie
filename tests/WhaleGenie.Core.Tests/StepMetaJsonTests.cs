using System.Text.Json.Nodes;

using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The settings every step carries, as the file writes them: only what differs from the defaults,
/// each read back into the setting it came from, and every word the file may hold.
/// </summary>
public class StepMetaJsonTests
{
    private static void AssertSame(StepMeta expected, StepMeta actual)
    {
        Assert.Equal(expected.Comment, actual.Comment);
        Assert.Equal(expected.IsEnabled, actual.IsEnabled);
        Assert.Equal(expected.TimeoutMs, actual.TimeoutMs);
        Assert.Equal(expected.RetryCount, actual.RetryCount);
        Assert.Equal(expected.RetryDelayMs, actual.RetryDelayMs);
        Assert.Equal(expected.RetryBackoff, actual.RetryBackoff);
        Assert.Equal(expected.DelayBeforeMs, actual.DelayBeforeMs);
        Assert.Equal(expected.DelayAfterMs, actual.DelayAfterMs);
        Assert.Equal(expected.OnError, actual.OnError);
    }

    [Fact]
    public void A_step_with_no_settings_writes_nothing_and_reads_back_empty()
    {
        Assert.Empty(new StepMeta().ToJson());
        Assert.Same(StepMeta.Empty, StepMeta.FromJson(null));
    }

    [Fact]
    public void Every_setting_is_written_and_read_back()
    {
        var meta = new StepMeta
        {
            Comment = "订单导出",
            IsEnabled = false,
            TimeoutMs = 1500,
            RetryCount = 2,
            RetryDelayMs = 700,
            RetryBackoff = RetryBackoff.Doubling,
            DelayBeforeMs = 30,
            DelayAfterMs = 40,
            OnError = StepErrorAction.NextIteration,
        };

        AssertSame(meta, StepMeta.FromJson(meta.ToJson()));
    }

    [Fact]
    public void A_step_that_is_still_on_does_not_write_that_it_is()
    {
        Assert.False(new StepMeta().ToJson().ContainsKey("enabled"));
        Assert.False(new StepMeta { IsEnabled = false }.ToJson()["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void A_retry_writes_its_count_and_pause_and_the_backoff_only_when_it_is_not_fixed()
    {
        var fixedPause = new StepMeta { RetryCount = 2, RetryDelayMs = 700 }.ToJson();
        Assert.Equal(2, fixedPause["retryCount"]!.GetValue<int>());
        Assert.Equal(700, fixedPause["retryDelayMs"]!.GetValue<int>());
        Assert.False(fixedPause.ContainsKey("retryBackoff"));

        Assert.Equal("doubling", new StepMeta
        {
            RetryCount = 1,
            RetryBackoff = RetryBackoff.Doubling,
        }.ToJson()["retryBackoff"]!.GetValue<string>());

        Assert.Equal("jitter", new StepMeta
        {
            RetryCount = 1,
            RetryBackoff = RetryBackoff.Jitter,
        }.ToJson()["retryBackoff"]!.GetValue<string>());
    }

    [Fact]
    public void A_pause_nobody_wrote_reads_as_the_usual_one()
    {
        Assert.Equal(500, StepMeta.FromJson(new JsonObject { ["retryCount"] = 1 }).RetryDelayMs);
        Assert.Equal(500, StepMeta.FromJson(new JsonObject { ["retryDelayMs"] = 0 }).RetryDelayMs);
        Assert.Equal(1200, StepMeta.FromJson(new JsonObject { ["retryDelayMs"] = 1200 }).RetryDelayMs);
    }

    [Theory]
    [InlineData(StepErrorAction.Stop, "stop")]
    [InlineData(StepErrorAction.Continue, "continue")]
    [InlineData(StepErrorAction.NextIteration, "nextIteration")]
    [InlineData(StepErrorAction.AskUser, "ask")]
    public void Each_failure_rule_is_written_as_its_word_and_reads_back(
        StepErrorAction action, string word)
    {
        Assert.Equal(word, StepMeta.Name(action));
        Assert.Equal(action, StepMeta.Action(word));
        Assert.Equal(action, StepMeta.Action(word.ToUpperInvariant()));
    }

    [Fact]
    public void A_word_nobody_writes_reads_as_the_defaults()
    {
        var meta = StepMeta.FromJson(new JsonObject
        {
            ["onError"] = "nobody-writes-this",
            ["retryBackoff"] = "nobody-writes-this",
        });

        Assert.Equal(StepErrorAction.Stop, meta.OnError);
        Assert.Equal(RetryBackoff.Fixed, meta.RetryBackoff);
        Assert.Equal("fixed", StepMeta.Name(RetryBackoff.Fixed));
    }

    [Fact]
    public void A_number_written_as_a_decimal_reads_as_a_whole_one()
        => Assert.Equal(1500,
            StepMeta.FromJson(new JsonObject { ["timeoutMs"] = 1500.5 }).TimeoutMs);

    [Fact]
    public void Turning_a_step_off_keeps_every_other_setting()
    {
        var meta = new StepMeta
        {
            Comment = "note",
            TimeoutMs = 900,
            RetryCount = 3,
            RetryDelayMs = 200,
            RetryBackoff = RetryBackoff.Jitter,
            DelayBeforeMs = 10,
            DelayAfterMs = 20,
            OnError = StepErrorAction.Continue,
        };

        var off = meta.WithEnabled(false);

        Assert.False(off.IsEnabled);
        AssertSame(meta, off.WithEnabled(true));
        Assert.True(meta.WithEnabled(true).IsEnabled);
    }

    [Fact]
    public void Any_setting_makes_the_settings_no_longer_empty()
    {
        Assert.True(new StepMeta().IsEmpty);
        Assert.False(new StepMeta { Comment = "x" }.IsEmpty);
        Assert.False(new StepMeta { IsEnabled = false }.IsEmpty);
        Assert.False(new StepMeta { TimeoutMs = 1 }.IsEmpty);
        Assert.False(new StepMeta { RetryCount = 1 }.IsEmpty);
        Assert.False(new StepMeta { DelayBeforeMs = 1 }.IsEmpty);
        Assert.False(new StepMeta { DelayAfterMs = 1 }.IsEmpty);
        Assert.False(new StepMeta { OnError = StepErrorAction.Continue }.IsEmpty);
    }

    [Fact]
    public void The_first_retry_counts_as_one_and_doubling_grows_from_there()
    {
        var fixedPause = new StepMeta { RetryCount = 3, RetryDelayMs = 500 };
        Assert.Equal(500, fixedPause.RetryDelayFor(0));
        Assert.Equal(500, fixedPause.RetryDelayFor(1));

        var doubling = new StepMeta
        {
            RetryCount = 3,
            RetryDelayMs = 500,
            RetryBackoff = RetryBackoff.Doubling,
        };
        Assert.Equal(500, doubling.RetryDelayFor(1));
        Assert.Equal(1000, doubling.RetryDelayFor(2));
        Assert.Equal(2000, doubling.RetryDelayFor(3));
        Assert.Equal(StepMeta.LongestPauseMs, doubling.RetryDelayFor(60));
    }
}
