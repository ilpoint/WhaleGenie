using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// One line of the triggers in play when a run starts.
/// <summary>
/// One line of what a trigger is waiting for, already written out. The words come from the text
/// table, so the two languages read alike and the line is not put together twice.
/// </summary>
public sealed record TriggerWatchLine(string Text)
{
    public static TriggerWatchLine Of(TriggerWatch watch)
    {
        var parts = new List<string> { watch.Macro, watch.Looking };

        if (watch.Matched is { } matched)
        {
            parts.Add(Strings.Get(matched ? "Run.TriggerThere" : "Run.TriggerAway"));
        }

        parts.Add(Strings.Format("Run.TriggerLooked", Moment(watch.Checked), Moment(watch.Next)));

        if (watch.Told)
        {
            parts.Add(Strings.Get("Run.TriggerTold"));
        }

        return new TriggerWatchLine(string.Join(" · ", parts.Where(part => part.Length > 0)));
    }

    /// <summary>A time of day, or the words for a trigger nothing is on a clock for.</summary>
    private static string Moment(DateTime moment)
        => moment == default ? Strings.Get("Run.TriggerNoClock") : moment.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}
