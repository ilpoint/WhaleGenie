using System;
using System.Collections.Concurrent;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// Remembers the input WhaleGenie itself has just sent, so the parts of the app that watch the
/// keyboard and the mouse can tell their own output from a person's.
/// </summary>
/// <remarks>
/// The platform marks a whole event as injected, but that also covers input forwarded from a
/// remote session or another tool — input a bound key must still answer to. Knowing which key
/// was sent is narrower and enough: a macro that presses the very key another macro waits for
/// will not set it off, while a key a person pressed will.
/// </remarks>
public static class WhaleGenieInputGate
{
    /// <summary>How long after sending a key it still counts as ours.</summary>
    private const int WindowMs = 400;

    private static readonly ConcurrentDictionary<string, long> Sent =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that WhaleGenie is sending this key.</summary>
    public static void Note(string? key)
    {
        var name = (key ?? string.Empty).Trim();
        if (name.Length > 0)
        {
            Sent[name] = Environment.TickCount64;
        }
    }

    /// <summary>
    /// Records that WhaleGenie is sending this key, under every name it can come back as. A key
    /// whose name the keyboard's state decides has to be written down as both: a number pad key
    /// pressed with NumLock off arrives as the navigation key it has become, so a NumPad7 noted
    /// only by that name would come back as a Home nobody wrote down — and the program would take
    /// its own key for a person's, answering whichever shortcut Home is bound to while the macro
    /// is still pressing keys.
    /// </summary>
    public static void Note(HookKey key)
    {
        Note(KeyNames.Name(key, sided: true));

        foreach (var other in KeyNames.AlsoSeenAs(key))
        {
            Note(other);
        }
    }

    /// <summary>True when WhaleGenie sent this key a moment ago, so the event is its own doing.</summary>
    public static bool RecentlySent(string? key, int withinMs = WindowMs)
    {
        var name = (key ?? string.Empty).Trim();
        if (name.Length == 0 || !Sent.TryGetValue(name, out var at))
        {
            return false;
        }

        var age = Environment.TickCount64 - at;
        return age >= 0 && age <= withinMs;
    }
}
