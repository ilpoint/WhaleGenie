using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Devices;

// Keys and pointer: the keyboard, the mouse and the space their numbers are counted in.
/// <summary>The space in which an input device's x and y are counted.</summary>
public enum InputCoordinateSpace
{
    /// <summary>Screen pixels, the desktop's own space.</summary>
    Screen,

    /// <summary>Pixels from the target window's client-area top-left corner.</summary>
    WindowClient,
}

/// <summary>The keyboard and the mouse.</summary>
public interface IInputDevice
{
    /// <summary>
    /// What the x and y of this device mean. The front and driver devices take screen pixels;
    /// a background device takes client-area pixels of the window it posts at.
    /// </summary>
    InputCoordinateSpace CoordinateSpace => InputCoordinateSpace.Screen;

    /// <summary>Where the pointer is right now.</summary>
    ScreenPoint Cursor { get; }

    void KeyPress(string key, int holdMs);

    void KeyDown(string key);

    void KeyUp(string key);

    /// <summary>Presses every key in the chord, holds them together, then releases them.</summary>
    void Hotkey(IReadOnlyList<string> keys, int holdMs);

    void TypeText(string text, int intervalMs);

    void MoveMouse(int x, int y, int durationMs);

    /// <summary>
    /// Walks the pointer along a planned path, finishing on its last point. The first point is
    /// where the pointer already is, so only the stops after it are sent. A move that goes
    /// straight is the same call with a line as the path.
    /// </summary>
    void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs);

    void MoveMouseRelative(int dx, int dy, int durationMs);

    void MouseDown(string button, int x, int y);

    void MouseUp(string button, int x, int y);

    void Click(string button, int x, int y, int clicks, int intervalMs);

    /// <summary>
    /// Scrolls the wheel; the direction is <c>up</c>, <c>down</c>, <c>left</c> or <c>right</c>,
    /// and <paramref name="delta"/> is measured in wheel units, where 120 units make one notch.
    /// A whole number of notches is what a wheel normally moves in; the finer units are there so
    /// a step can ask for a part of one.
    /// </summary>
    void Scroll(string direction, int delta, int x, int y);

    void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps);

    /// <summary>
    /// Presses the button at the first point, walks the rest of the path with it held, and
    /// releases on the last one.
    /// </summary>
    void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs);
}
