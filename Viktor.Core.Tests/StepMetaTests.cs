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

    [Theory]
    [InlineData("Run.ImageNotFound", "Run.ImageNotFound", true)]
    [InlineData("Run.ImageNotFound", "Run.TextNotFound", false)]
    [InlineData("*NotFound", "Run.ImageNotFound", true)]
    [InlineData("*", "Run.ImageNotFound", true)]
    [InlineData("", "Run.ImageNotFound", true)]
    [InlineData("run.image?", "run.imagex", true)]
    [InlineData("Run.ImageNotFound; Run.TextNotFound", "Run.TextNotFound", true)]
    [InlineData("Run.ImageNotFound; Run.TextNotFound", "Run.Other", false)]
    public void An_error_rule_answers_to_the_keys_it_names(string when, string key, bool wanted)
        => Assert.Equal(wanted, new ErrorJump(when, "处理", false).Matches(key));

    [Fact]
    public void Error_rules_are_written_and_read_as_lines()
    {
        ErrorJump[] rules =
        [
            new("Run.ImageNotFound", "补一张图", false),
            new("*", "收工", true),
        ];

        var text = StepMeta.Text(rules);
        Assert.Equal($"Run.ImageNotFound -> 补一张图{Environment.NewLine}* => 收工", text);

        Assert.True(StepMeta.TryRead(text, out var read, out var bad));
        Assert.Equal(string.Empty, bad);
        Assert.Equal(rules, read);
    }

    [Fact]
    public void A_line_that_says_nothing_about_where_to_carry_on_is_refused()
    {
        Assert.False(StepMeta.TryRead("Run.ImageNotFound -> 处理\n这行漏了箭头", out _, out var bad));
        Assert.Equal("这行漏了箭头", bad);

        Assert.False(StepMeta.TryRead("Run.ImageNotFound ->", out _, out bad));
        Assert.Equal("Run.ImageNotFound ->", bad);

        // Blank lines and comments are there to be skipped, not to fail on.
        Assert.True(StepMeta.TryRead("# 先停用\n\n* -> 处理", out var rules, out _));
        Assert.Equal(["* -> 处理"], rules.Select(rule => $"{rule.When} -> {rule.Jump}"));
    }

    [Fact]
    public void Error_rules_travel_through_the_macro_file()
    {
        var meta = new StepMeta
        {
            Jumps = [new ErrorJump("*NotFound", "补一下", true)],
        };

        var written = meta.ToJson()["onErrorJumps"]?.AsArray();
        Assert.NotNull(written);
        Assert.Equal("*NotFound", written[0]?["when"]?.GetValue<string>());
        Assert.Equal("补一下", written[0]?["jump"]?.GetValue<string>());
        Assert.True(written[0]?["back"]?.GetValue<bool>());

        var again = StepMeta.FromJson(meta.ToJson());
        Assert.Equal(meta.Jumps, again.Jumps);
        Assert.False(again.IsEmpty);

        // A step with no rules writes nothing about them, and a jump with no "back" leaves the
        // flag out rather than writing a false every time.
        Assert.Null(StepMeta.Empty.ToJson()["onErrorJumps"]);
        Assert.Null(new StepMeta { Jumps = [new ErrorJump("*", "处理", false)] }.ToJson()["onErrorJumps"]?[0]?["back"]);
    }
}
