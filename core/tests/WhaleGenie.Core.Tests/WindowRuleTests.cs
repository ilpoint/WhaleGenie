using System;
using System.Text.Json.Nodes;

using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The rule a step writes to name a window: the JSON it is saved as, the default a missing field
/// takes, and the ten ways it may say which part of a window is compared and how.
/// </summary>
public class WindowRuleTests
{
    [Fact]
    public void A_rule_round_trips_through_the_json_it_is_saved_as()
    {
        var rule = new WindowRule(
            Title: "订单 - 记事本",
            Process: "notepad",
            ClassName: "Notepad",
            How: WindowHow.TitleEndsWith,
            CaseSensitive: true,
            Trim: false,
            Multiline: true);

        Assert.True(WindowRule.TryParse(rule.ToJson().ToJsonString(), out var read));
        Assert.Equal(rule, read);
    }

    [Fact]
    public void Following_the_previous_rule_is_written_only_when_it_is_asked_for()
    {
        Assert.True(new WindowRule(Follow: true).ToJson().ContainsKey(FieldNames.Follow));
        Assert.False(new WindowRule(Title: "x").ToJson().ContainsKey(FieldNames.Follow));
    }

    [Fact]
    public void A_rule_with_no_fields_is_the_empty_one_and_has_no_target()
    {
        var rule = WindowRule.FromJson(new JsonObject());

        Assert.Equal(WindowRule.Empty, rule);
        Assert.False(rule.HasTarget);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    [InlineData("\"title\"")]
    [InlineData("{oops")]
    public void Anything_that_is_not_a_rule_object_is_refused(string written)
        => Assert.False(WindowRule.TryParse(written, out _));

    [Fact]
    public void A_rule_has_a_target_unless_it_follows_the_previous_one()
    {
        Assert.True(new WindowRule(Title: "订单").HasTarget);
        Assert.True(new WindowRule(Process: "notepad", How: WindowHow.Process).HasTarget);
        Assert.True(new WindowRule(How: WindowHow.Handle, Handle: 0x1234).HasTarget);

        Assert.False(new WindowRule().HasTarget);
        Assert.False(new WindowRule(How: WindowHow.Handle).HasTarget);
        Assert.False(new WindowRule(Follow: true, Title: "订单").HasTarget);
    }

    [Theory]
    [InlineData(WindowHow.TitleContains, WindowMatch.Title, TextCompare.Contains)]
    [InlineData(WindowHow.TitleStartsWith, WindowMatch.Title, TextCompare.StartsWith)]
    [InlineData(WindowHow.TitleEndsWith, WindowMatch.Title, TextCompare.EndsWith)]
    [InlineData(WindowHow.TitleEquals, WindowMatch.Title, TextCompare.Exactly)]
    [InlineData(WindowHow.TitleRegex, WindowMatch.Title, TextCompare.Regex)]
    [InlineData(WindowHow.Process, WindowMatch.Process, TextCompare.Contains)]
    [InlineData(WindowHow.ProcessEquals, WindowMatch.Process, TextCompare.Exactly)]
    [InlineData(WindowHow.Class, WindowMatch.ClassName, TextCompare.Contains)]
    [InlineData(WindowHow.ClassEquals, WindowMatch.ClassName, TextCompare.Exactly)]
    [InlineData(WindowHow.Handle, WindowMatch.Handle, TextCompare.Exactly)]
    [InlineData("nobody-writes-this", WindowMatch.Title, TextCompare.Contains)]
    public void Each_way_names_the_part_and_the_comparison(string how, WindowMatch match,
        string compare)
        => Assert.Equal((match, compare), WindowHow.Read(how));

    [Fact]
    public void Every_way_is_named_and_no_two_labels_say_the_same_thing()
    {
        Assert.All(WindowHow.Labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));
        Assert.Equal(WindowHow.Labels.Count, WindowHow.Labels.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(WindowHow.Values.Count, WindowHow.Labels.Count);
    }

    [Fact]
    public void Following_is_written_as_a_yes_rather_than_merely_being_there()
        => Assert.True(new WindowRule(Follow: true).ToJson()[FieldNames.Follow]!.GetValue<bool>());

    [Fact]
    public void A_rule_that_leaves_a_way_out_reads_as_the_oldest_way()
    {
        Assert.Equal(WindowHow.TitleContains, WindowRule.FromJson(new JsonObject()).How);

        // A way the ten do not name is kept as it was written and read as the oldest way, so a
        // file written by a newer build still names a window here rather than finding nothing.
        var unknown = new JsonObject { [FieldNames.How] = "nobody-writes-this" };
        Assert.Equal((WindowMatch.Title, TextCompare.Contains),
            WindowRule.FromJson(unknown).Reading);
    }

    [Fact]
    public void A_rule_that_leaves_a_switch_out_reads_as_its_default()
    {
        var rule = WindowRule.FromJson(new JsonObject
        {
            [FieldNames.Title] = "订单",
        });

        Assert.True(rule.Trim);
        Assert.False(rule.CaseSensitive);
        Assert.False(rule.Multiline);
        Assert.False(rule.Follow);
    }

    [Fact]
    public void The_words_and_their_labels_are_kept_in_one_order()
    {
        Assert.Equal(10, WindowHow.Values.Count);
        Assert.Equal(WindowHow.Values.Count, WindowHow.Labels.Count);

        for (var at = 0; at < WindowHow.Values.Count; at++)
        {
            Assert.Equal(at, WindowHow.IndexOf(WindowHow.Values[at]));
        }

        Assert.Equal(0, WindowHow.IndexOf("nobody-writes-this"));
        Assert.Equal(0, WindowHow.IndexOf(null));
    }

    [Fact]
    public void A_text_way_reads_the_three_switches_and_a_handle_does_not()
    {
        Assert.True(WindowHow.UsesTextComparison(WindowHow.TitleContains));
        Assert.False(WindowHow.UsesTextComparison(WindowHow.Handle));
        Assert.True(WindowHow.IsPattern(WindowHow.TitleRegex));
        Assert.False(WindowHow.IsPattern(WindowHow.TitleContains));
    }

    [Fact]
    public void The_text_a_rule_is_held_up_against_is_the_part_it_names()
    {
        Assert.Equal("订单", new WindowRule(Title: "订单").MatchValue);
        Assert.Equal("notepad", new WindowRule(Process: "notepad", How: WindowHow.Process).MatchValue);
        Assert.Equal("Notepad", new WindowRule(ClassName: "Notepad", How: WindowHow.Class).MatchValue);
        Assert.Equal("4660", new WindowRule(How: WindowHow.Handle, Handle: 0x1234).MatchValue);
    }

    [Fact]
    public void The_three_switches_travel_to_the_comparison_as_they_were_written()
    {
        var options = new WindowRule(CaseSensitive: true, Trim: false, Multiline: true).Options;

        Assert.Equal(new TextCompareOptions(true, false, true), options);
    }
}
