using System.Linq;
using Viktor.Localization;
using Viktor.Models;

namespace Viktor.Tests;

/// <summary>
/// The unit dropdown beside a length-of-time box: which unit a stored number opens in, and that
/// every unit carries text in both interface languages.
/// </summary>
public class DurationUnitTests
{
    [Theory]
    [InlineData(0, "ms")]
    [InlineData(500, "ms")]
    [InlineData(1500, "ms")]
    [InlineData(1000, "s")]
    [InlineData(90_000, "s")]
    [InlineData(300_000, "min")]
    [InlineData(165_600_000, "h")]
    public void The_biggest_unit_a_number_divides_into_is_the_one_it_opens_in(
        int milliseconds, string expected)
        => Assert.Equal(expected, DurationUnit.Best(milliseconds).Key);

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
}
