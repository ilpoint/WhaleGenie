using System;
using System.Collections.Generic;
using System.Threading;
using SharpHook.Data;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Keyboard and mouse input sent through a virtual USB device, so the machine sees input that
/// really came from hardware. This is the third way a step can ask for its input: the window in
/// front, messages posted at one window, or this.
/// </summary>
/// <remarks>
/// Two things about a virtual device shape everything below. Its mouse says how far to move rather
/// than where to be, so a move is worked out as a distance from wherever the pointer is now. Its
/// keyboard report is the whole keyboard rather than a change to it, so what is held down is kept
/// here and sent again with every key.
///
/// One report at a time: the virtual keyboard and mouse are single pieces of hardware, so two
/// macros sending at once would type on top of each other. The machine also answers only the pair
/// that got on the bus first — a second pair stays attached and does nothing — so the program keeps
/// one pair and shares it out (<see cref="SharedDriverInput"/>).
/// </remarks>
public sealed partial class ViiperInputDevice : IInputDevice, IGamepadDevice, IDisposable
{
    /// <summary>Wheel units in one notch, the amount a wheel normally turns in.</summary>
    private const int Notch = 120;

    /// <summary>How long a key stays down when the step asked for no time in particular.</summary>
    private const int LeastHoldMs = 50;

    /// <summary>How long a key stays down while text is typed, one character at a time.</summary>
    private const int TypingHoldMs = 10;

    /// <summary>How long a button stays down for a click.</summary>
    private const int ClickHoldMs = 10;

    /// <summary>Pause between the keys of a chord, so the target sees it being put together.</summary>
    private const int ChordGapMs = 10;

    /// <summary>Pause between clicks that did not ask for one.</summary>
    private const int ClickGapMs = 30;

    /// <summary>Pause between wheel notches.</summary>
    private const int NotchGapMs = 10;

    /// <summary>
    /// How long a report needs before the machine has moved the pointer by it. A report is polled
    /// rather than applied on the spot, so reading the pointer straight after sending one hands
    /// back where it was before it — and a move measured from there lands short.
    /// </summary>
    private const int SettleMs = 30;

    /// <summary>How many times a move is tried before the machine is given up on.</summary>
    private const int SeekTries = 12;

    /// <summary>How close to the point asked for counts as being there, in pixels.</summary>
    private const int Arrived = 1;

    /// <summary>
    /// How far the pointer is moved to find out whether the machine is listening to the devices at
    /// all. A pixel is not enough: the machine rounds movement this small away, and the check would
    /// answer "no" on a device that is working perfectly well.
    /// </summary>
    private const int Nudge = 10;

    private readonly Func<IViiperLink> _connect;

    private readonly object _gate = new();

    /// <summary>The keys held down right now, in the order they were pressed.</summary>
    private readonly List<byte> _keys = [];

    private IViiperLink? _link;
    private byte _modifiers;
    private byte _buttons;
    private long _movedAt;

    /// <summary>Whether a controller has been put on the machine yet.</summary>
    private bool _gamepadOn;

    /// <summary>What the controller is doing, kept here because a report is the whole picture of it.</summary>
    private GamepadState _gamepad = GamepadState.Neutral;
}
