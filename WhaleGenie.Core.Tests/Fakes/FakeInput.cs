using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend keyboard and mouse: every call written down, and the routes and paths the engine
/// asked for.
/// </summary>
internal sealed class FakeInput(FakeDesk desk, FakeScreen screen) : IInputDevice
{
    /// <summary>
    /// Where the pointer is. It is a fact about the desk rather than about the way input is sent, so
    /// the screen holds it and this device only reports it.
    /// </summary>
    public ScreenPoint Cursor => screen.Cursor;

    /// <summary>The routes the engine asked input to take, in the order it asked.</summary>
    public List<InputRoute> Routes { get; } = [];

    /// <summary>The paths the pointer was asked to travel, in the order they were asked for.</summary>
    public List<IReadOnlyList<ScreenPoint>> Paths { get; } = [];

    /// <summary>Who held the input door when the click was sent, for the check about waiting.</summary>
    public string GateAtClick { get; private set; } = string.Empty;

    public void KeyPress(string key, int holdMs) => desk.Note($"keyPress {key} {holdMs}");

    public void KeyDown(string key) => desk.Note($"keyDown {key}");

    public void KeyUp(string key) => desk.Note($"keyUp {key}");

    public void Hotkey(IReadOnlyList<string> keys, int holdMs)
        => desk.Note($"hotkey {string.Join('|', keys)} {holdMs}");

    public void TypeText(string text, int intervalMs) => desk.Note($"type {text} {intervalMs}");

    public void MoveMouse(int x, int y, int durationMs) => desk.Note($"move {x} {y} {durationMs}");

    public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        Paths.Add(path);
        desk.Note($"along {path.Count} {durationMs}");
    }

    public void MoveMouseRelative(int dx, int dy, int durationMs)
        => desk.Note($"moveBy {dx} {dy} {durationMs}");

    public void MouseDown(string button, int x, int y) => desk.Note($"down {button} {x} {y}");

    public void MouseUp(string button, int x, int y) => desk.Note($"up {button} {x} {y}");

    public void Click(string button, int x, int y, int clicks, int intervalMs)
    {
        GateAtClick = InputGate.Owner;
        desk.Note($"click {button} {x} {y} {clicks} {intervalMs}");
    }

    public void Scroll(string direction, int delta, int x, int y)
        => desk.Note($"scroll {direction} {delta} {x} {y}");

    public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps)
        => desk.Note($"drag {button} {startX} {startY} {endX} {endY} {durationMs} {steps}");

    public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
    {
        Paths.Add(path);
        desk.Note($"dragAlong {button} {path.Count} {durationMs}");
    }
}
