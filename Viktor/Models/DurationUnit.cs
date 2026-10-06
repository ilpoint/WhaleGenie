using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Viktor.Localization;

namespace Viktor.Models;

/// <summary>
/// A unit a length-of-time box can be written in. A step always stores milliseconds, so a unit
/// only changes how the number reads on screen: pick minutes, type 5, and the macro holds 300000.
/// </summary>
public sealed record DurationUnit(string Key, decimal Factor, string Label)
{
    /// <summary>
    /// The units the dropdown offers, smallest first. The labels here are the English fallbacks
    /// (short forms, because they also end up inside a sentence); <see cref="Localized"/> swaps in
    /// the ones for the interface language in use.
    /// </summary>
    public static IReadOnlyList<DurationUnit> Catalog { get; } =
    [
        new("ms", 1m, "ms"),
        new("s", 1000m, "s"),
        new("min", 60_000m, "min"),
        new("h", 3_600_000m, "h"),
    ];

    /// <summary>Resource key of this unit's label.</summary>
    public string LabelKey => "Add.Unit." + Key;

    /// <summary>
    /// The largest unit the number divides into exactly, so a step saved as 300000 milliseconds
    /// opens reading "5 minutes" while 1500 stays on milliseconds. Zero stays on milliseconds,
    /// because "0 means no limit" reads worst as "0 hours".
    /// </summary>
    public static DurationUnit Best(decimal milliseconds) => Biggest(Catalog, milliseconds);

    /// <summary>A length of time written the way a person reads it, for example "5 分" or "1.5 s".</summary>
    public static string Written(decimal milliseconds)
    {
        var unit = Biggest(Localized(), milliseconds);
        var number = (milliseconds / unit.Factor).ToString("0.####", CultureInfo.InvariantCulture);
        return number + " " + unit.Label;
    }

    private static DurationUnit Biggest(IReadOnlyList<DurationUnit> units, decimal milliseconds)
    {
        if (milliseconds == 0m)
        {
            return units[0];
        }

        for (var index = units.Count - 1; index > 0; index--)
        {
            if (milliseconds % units[index].Factor == 0m)
            {
                return units[index];
            }
        }

        return units[0];
    }

    /// <summary>The units as the dialog shows them, labelled in the interface language in use.</summary>
    public static IReadOnlyList<DurationUnit> Localized() =>
        [.. Catalog.Select(unit => unit with { Label = Strings.Get(unit.LabelKey, unit.Label) })];
}
