using System;
using System.Globalization;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.ViewModels;

// One line of the triggers in play when a run starts.
/// <summary>
/// One armed trigger as the panel lists it. The words come from the text table, so the two
/// languages read alike and the line is not put together twice — the page draws the parts it is
/// handed instead of knowing which part goes where.
/// </summary>
public static class RunTriggerLine
{
    /// <summary>The whole of what one watched trigger is waiting for, split into its parts.</summary>
    public static RunTrigger Of(TriggerWatch watch)
    {
        var status = watch.Matched switch
        {
            true => Strings.Get("Run.TriggerThere"),
            false => Strings.Get("Run.TriggerAway"),
            _ => string.Empty,
        };

        var checkedAt = watch.Checked == default
            ? Strings.Get("Run.TriggerNoClock")
            : Strings.Format("Run.TriggerLooked", Moment(watch.Checked), Moment(watch.Next));

        return new RunTrigger(watch.Macro, watch.Looking, status, checkedAt,
            watch.Told ? Strings.Get("Run.TriggerTold") : string.Empty);
    }

    /// <summary>A time of day, or the words for a trigger nothing is on a clock for.</summary>
    private static string Moment(DateTime moment)
        => moment == default ? Strings.Get("Run.TriggerNoClock") : moment.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}
