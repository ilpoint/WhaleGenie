using System.Collections.Generic;
using WhaleGenie.Models;

namespace WhaleGenie.Execution;

/// <summary>
/// What each macro waiting for a window is looking at, and whether what it waits for has just
/// changed. Both ways of finding out come through here — the machine saying a window changed, and
/// the clock coming round — so a macro behaves the same whichever it is running on, and a machine
/// that stops saying anything leaves the clock carrying on where it left off.
/// </summary>
internal sealed class WindowWatches
{
    private readonly Dictionary<MacroItem, bool> _there = [];

    /// <summary>Whether a window the macro waits for is there, or null while nothing has been looked at.</summary>
    public bool? For(MacroItem macro) => _there.TryGetValue(macro, out var there) ? there : null;

    public IReadOnlyCollection<MacroItem> Macros => _there.Keys;

    public int Count => _there.Count;

    /// <summary>
    /// What a look found, and whether that is worth doing something about. The first look is the
    /// baseline rather than news — what is already open when the watching starts has not just
    /// appeared — and after that only a change counts, so the same window seen again, however many
    /// times it is reported, is still one appearance.
    /// </summary>
    public bool Changed(MacroItem macro, bool there)
    {
        var before = For(macro);
        _there[macro] = there;
        return before is { } known && known != there;
    }

    /// <summary>Stops watching a macro, so the next look starts from nothing again.</summary>
    public void Forget(MacroItem macro) => _there.Remove(macro);

    public void Clear() => _there.Clear();
}
