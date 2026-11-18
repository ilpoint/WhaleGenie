using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// The unit dropdown beside a length-of-time box: which unit a stored number opens in, and that
/// every unit carries text in both interface languages.
/// </summary>
public class DurationUnitTests
{
    [Fact]
    public void Every_unit_is_named_in_both_languages()
    {
        foreach (var unit in DurationUnit.Catalog)
        {
            Assert.True(Strings.English.ContainsKey(unit.LabelKey), unit.LabelKey);
            Assert.True(Strings.Chinese.ContainsKey(unit.LabelKey), unit.LabelKey);
        }
    }

    [Fact]
    public void A_minute_is_sixty_thousand_milliseconds()
    {
        var minutes = DurationUnit.Catalog.First(unit => unit.Key == "min");

        // 45 minutes is the 2700000 a step will carry.
        Assert.Equal(2_700_000m, minutes.Factor * 45m);
    }

    [Fact]
    public void A_length_of_time_is_written_with_the_unit_it_divides_into()
    {
        // The same wording the step list uses, so a number typed as "5 分" never comes back as
        // an unreadable 300000 in the editor.
        Assert.Equal("5 " + Strings.Get("Add.Unit.min"), DurationUnit.Written(300_000));
        Assert.Equal("1500 " + Strings.Get("Add.Unit.ms"), DurationUnit.Written(1_500));
        Assert.Equal("2 " + Strings.Get("Add.Unit.h"), DurationUnit.Written(7_200_000));
    }

    [Fact]
    public void A_step_list_summary_writes_a_long_pause_out_with_its_unit()
    {
        var step = new MacroStep
        {
            Type = "control.delay",
            Meta = new StepMeta { TimeoutMs = 300_000, DelayBeforeMs = 1_500 },
        };

        Assert.Contains(DurationUnit.Written(300_000), step.MetaSummary);
        Assert.Contains(DurationUnit.Written(1_500), step.MetaSummary);
    }
}
