using System;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// The machine saying "something about the windows just changed", so a window that opens and closes
/// again between two looks is still noticed. Where nothing will say — a Windows too old for it, or a
/// session with no desktop to watch — <see cref="Available"/> is false, and the caller goes on
/// looking on its own.
/// </summary>
/// <remarks>
/// What arrives is a hint, not an answer: the windows still have to be read to know whether what a
/// macro waits for is there. The hint is worth having because it says <em>now</em>, and the moment a
/// window is up is the moment that is missed when the reading happens on a timer instead.
/// </remarks>
public interface IWindowEvents : IDisposable
{
    /// <summary>True when the machine is telling us about the windows.</summary>
    bool Available { get; }

    /// <summary>
    /// Raised on a thread of its own when a window may have appeared or gone away. It is raised
    /// often, and runs at the speed of whatever handles it.
    /// </summary>
    event Action? Changed;

    /// <summary>Asks to be told. Doing this twice is the same as doing it once.</summary>
    void Start();
}

/// <summary>The answer of a machine with nothing to say, which leaves the caller looking on its own.</summary>
public sealed class NoWindowEvents : IWindowEvents
{
    public bool Available => false;

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public void Start()
    {
    }

    public void Dispose()
    {
    }
}
