using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What every pretend device shares: one log of the calls the engine made, and the two switches a
/// check uses to make a device refuse.
/// </summary>
/// <remarks>
/// The log is one list rather than one per device on purpose. A check asks "did the engine ask for
/// this, and in what order", and the answer that matters is the order across devices — a click
/// before the wait, not two separate histories to interleave by hand.
/// </remarks>
internal sealed class FakeDesk
{
    public List<string> Calls { get; } = [];

    /// <summary>When set, every call is refused this way instead of being recorded.</summary>
    public Exception? Refusal { get; set; }

    /// <summary>When set, every call reports the device as missing.</summary>
    public bool Unavailable { get; set; }

    /// <summary>
    /// Writes one call down and then answers the two switches. A device that refuses has still been
    /// asked, which is what the log is for; what it says after that is up to the switch.
    /// </summary>
    public void Note(string call)
    {
        Calls.Add(call);
        if (Unavailable)
        {
            throw new DeviceUnavailableException("the test device");
        }

        if (Refusal is not null)
        {
            throw Refusal;
        }
    }
}
