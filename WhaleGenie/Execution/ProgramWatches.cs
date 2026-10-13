using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.Execution;

/// <summary>
/// Which programs a macro waiting for one is watching are running. Nothing says when a program
/// starts, so this is found out by reading the list over and over, and the rule is the difference
/// against the reading before it: a program that was already running when the watching started is
/// not something that has just started, and one program seen twice is one start.
/// </summary>
internal sealed class ProgramWatches
{
    private readonly Dictionary<MacroItem, HashSet<int>> _running = [];

    /// <summary>Whether any of them is running, or null while nothing has been read yet.</summary>
    public bool? For(MacroItem macro)
        => _running.TryGetValue(macro, out var ids) ? ids.Count > 0 : null;

    public IReadOnlyCollection<MacroItem> Macros => _running.Keys;

    public int Count => _running.Count;

    /// <summary>
    /// What this reading says was not said by the last one, or null on the first reading of them
    /// all: counting starts where the watching does.
    /// </summary>
    public (bool Started, bool Stopped)? Changed(MacroItem macro, IReadOnlyCollection<int> running)
    {
        var now = new HashSet<int>(running);
        var known = _running.TryGetValue(macro, out var before);
        _running[macro] = now;

        return known
            ? (now.Except(before!).Any(), before!.Except(now).Any())
            : null;
    }

    /// <summary>Stops watching a macro, so the next reading starts from nothing again.</summary>
    public void Forget(MacroItem macro) => _running.Remove(macro);

    public void Clear() => _running.Clear();
}
