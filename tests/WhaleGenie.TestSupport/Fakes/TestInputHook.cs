using System;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.TestSupport;

/// <summary>The hook a test can press keys on, so a wait can be ended from the test rather than by hand.</summary>
internal sealed class TestInputHook : IInputHook
{
    public event Action<KeyHookEvent>? KeyDown;

    public event Action<KeyHookEvent>? KeyUp;

    public event Action<MouseHookEvent>? MouseDown { add { } remove { } }

    public event Action<MouseHookEvent>? MouseUp { add { } remove { } }

    public event Action<MouseMovedHookEvent>? Moved { add { } remove { } }

    public event Action<MouseMovedRelativeHookEvent>? MovedRelative { add { } remove { } }

    public event Action<WheelHookEvent>? Wheel { add { } remove { } }

    public bool HasKeyDown => KeyDown is not null;

    public KeyHookEvent? Last { get; private set; }

    public void Press(string name, bool up = false)
    {
        var key = KeyNames.Resolve(name) ?? throw new ArgumentException(name, nameof(name));
        var seen = new KeyHookEvent(key, injected: false);
        Last = seen;
        if (up)
        {
            KeyUp?.Invoke(seen);
        }
        else
        {
            KeyDown?.Invoke(seen);
        }
    }
}
