using System;
using System.Threading;
using WhaleGenie.Localization;

namespace WhaleGenie.Runtime;

/// <summary>
/// The system switch: whether the triggers are listening at all. It is the one piece of machine-wide
/// state the page, the hotkey and the native window all flip, so it lives beside the triggers rather
/// than on any one window. A page reads it through the bridge; a trigger host follows it; the
/// settings file only supplies the value the next run starts with.
/// </summary>
/// <remarks>
/// Set may be called from the interface thread by the page or the window, and from the trigger host
/// after a hotkey was posted there. The event is raised on the caller's thread; a subscriber that
/// owns windows posts to its own loop.
/// </remarks>
public sealed class SystemState
{
    private int _enabled;

    public SystemState()
        : this(LocalSettings.LoadSystemAtStart())
    {
    }

    public SystemState(bool enabled)
    {
        _enabled = enabled ? 1 : 0;
    }

    /// <summary>Whether the trigger service is meant to be listening.</summary>
    public bool IsEnabled => Volatile.Read(ref _enabled) == 1;

    /// <summary>Raised once for each actual flip, not for a write of the value it already had.</summary>
    public event Action? Changed;

    /// <summary>Writes the switch and tells everyone when the value actually moved.</summary>
    public void Set(bool enabled)
    {
        var next = enabled ? 1 : 0;
        if (Interlocked.Exchange(ref _enabled, next) == next)
        {
            return;
        }

        Changed?.Invoke();
    }

    public void Toggle() => Set(!IsEnabled);
}
