using System.Collections.Generic;
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
    /// The units the dropdown offers, smallest first. The labels here are the English fallbacks;
    /// <see cref="Localized"/> swaps in the ones for the interface language in use.
    /// </summary>
    public static IReadOnlyList<DurationUnit> Catalog { get; } =
    [
        new("ms", 1m, "Milliseconds"),
        new("s", 1000m, "Seconds"),
        new("min", 60_000m, "Minutes"),
        new("h", 3_600_000m, "Hours"),
    ];

    /// <summary>Resource key of this unit's label.</summary>
    public string LabelKey => "Add.Unit." + Key;

    /// <summary>
    /// The largest unit the number divides into exactly, so a step saved as 300000 milliseconds
    /// opens reading "5 minutes" while 1500 stays on milliseconds. Zero stays on milliseconds,
    /// because "0 means no limit" reads worst as "0 hours".
    /// </summary>
    public static DurationUnit Best(decimal milliseconds)
    {
        if (milliseconds == 0m)
        {
            return Catalog[0];
        }

        for (var index = Catalog.Count - 1; index > 0; index--)
        {
            if (milliseconds % Catalog[index].Factor == 0m)
            {
                return Catalog[index];
            }
        }

        return Catalog[0];
    }

    /// <summary>The units as the dialog shows them, labelled in the interface language in use.</summary>
    public static IReadOnlyList<DurationUnit> Localized() =>
        [.. Catalog.Select(unit => unit with { Label = Strings.Get(unit.LabelKey, unit.Label) })];
}
