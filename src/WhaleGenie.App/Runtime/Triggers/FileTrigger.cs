using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Runs the macros whose watched file or folder has changed. The watch is asked for by entry and
/// held by the pool, so one that is re-pointed elsewhere is closed rather than left holding a
/// handle, and one whose settings did not change loses nothing when the project is edited.
/// </summary>
internal sealed class FileTrigger(IFileDevice files, WatchPool watches) : ITriggerPoll
{
    public TriggerKind Kind => TriggerKind.File;

    public void Poll(TriggerRound round)
    {
        foreach (var trigger in round.Plan.OfKind(TriggerKind.File))
        {
            if (trigger.Spec is not FileSpec file || file.Path.Trim().Length == 0)
            {
                continue;
            }

            string path;
            try
            {
                path = files.Resolve(file.Path.Trim());
            }
            catch (Exception)
            {
                continue;   // no files on this machine, so there is nothing to watch
            }

            if (path.Length == 0)
            {
                continue;
            }

            var watch = watches.Acquire(trigger.Id, file, path);
            watch.Ensure(round.Now);

            if (watch.Take(round.Now))
            {
                round.Start(trigger, repeating: false);
            }
        }
    }

    public IEnumerable<TriggerWatch> Report(TriggerRound round)
    {
        foreach (var trigger in round.Plan.OfKind(TriggerKind.File))
        {
            yield return new TriggerWatch(trigger.Id, trigger.Macro.Name, trigger.Looking,
                watches.Holds(trigger.Id), DateTime.MinValue, DateTime.MinValue, true);
        }
    }
}
