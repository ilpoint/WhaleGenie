using System;
using System.Collections.Concurrent;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Remembers the input Viktor itself has just sent, so the parts of the app that watch the
/// keyboard and the mouse can tell their own output from a person's.
/// </summary>
/// <remarks>
/// The platform marks a whole event as injected, but that also covers input forwarded from a
/// remote session or another tool — input a bound key must still answer to. Knowing which key
/// was sent is narrower and enough: a macro that presses the very key another macro waits for
/// will not set it off, while a key a person pressed will.
/// </remarks>
public static class ViktorInputGate
{
    /// <summary>How long after sending a key it still counts as ours.</summary>
    private const int WindowMs = 400;

    private static readonly ConcurrentDictionary<string, long> Sent =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that Viktor is sending this key.</summary>
    public static void Note(string? key)
    {
        var name = (key ?? string.Empty).Trim();
        if (name.Length > 0)
        {
            Sent[name] = Environment.TickCount64;
        }
    }

    /// <summary>True when Viktor sent this key a moment ago, so the event is its own doing.</summary>
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
