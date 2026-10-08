using System;

namespace WhaleGenie.Core.Devices;

/// <summary>Which virtual controller a macro puts on the machine.</summary>
public enum GamepadKind
{
    /// <summary>An Xbox 360 wired controller, the one Windows games expect to find.</summary>
    Xbox360,

    /// <summary>A DualShock 4, the controller that goes with a PlayStation 4.</summary>
    DualShock4,

    /// <summary>A DualSense, the controller that goes with a PlayStation 5.</summary>
    DualSense,
}

/// <summary>
/// The buttons of a virtual controller, named the Xbox way whatever controller is really behind
/// it. The three controllers spell their buttons differently — a DualSense has a Cross and a
/// Circle where an Xbox pad has an A and a B — and a macro that had to name each one its own way
/// would stop working the moment the controller changed. So one vocabulary is used everywhere and
/// each controller's own report is built from it.
/// </summary>
[Flags]
public enum GamepadButtons
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    LeftBumper = 1 << 4,
    RightBumper = 1 << 5,
    LeftTrigger = 1 << 6,
    RightTrigger = 1 << 7,
    LeftStick = 1 << 8,
    RightStick = 1 << 9,
    Up = 1 << 10,
    Down = 1 << 11,
    Left = 1 << 12,
    Right = 1 << 13,
    Start = 1 << 14,
    Back = 1 << 15,
    Guide = 1 << 16,
}

/// <summary>
/// Everything a virtual controller is doing at one moment. Sticks are whole percent from the
/// centre, with the axes reading the way a person thinks of them: X grows to the right and Y grows
/// upwards. Triggers are whole percent from untouched to pulled all the way.
/// </summary>
public readonly record struct GamepadState(
    GamepadButtons Buttons,
    int LeftX,
    int LeftY,
    int RightX,
    int RightY,
    int LeftTrigger,
    int RightTrigger)
{
    /// <summary>A controller nobody is touching.</summary>
    public static GamepadState Neutral { get; } = new(GamepadButtons.None, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// The button and axis names a macro writes, so the engine and the interface agree on what a
/// name means and a name that means nothing is refused rather than guessed at.
/// </summary>
public static class GamepadNames
{
    /// <summary>Every button name a macro may write, in the order a picker reads them.</summary>
    public static IReadOnlyList<string> Buttons { get; } =
    [
        "a", "b", "x", "y",
        "lb", "rb", "lt", "rt",
        "ls", "rs",
        "up", "down", "left", "right",
        "start", "back", "guide",
    ];

    /// <summary>The button a name stands for, or null when the name means nothing.</summary>
    public static GamepadButtons? Button(string? name) => (name ?? string.Empty).Trim().ToLowerInvariant()
        switch
    {
        "a" => GamepadButtons.A,
        "b" => GamepadButtons.B,
        "x" => GamepadButtons.X,
        "y" => GamepadButtons.Y,
        "lb" or "l1" or "l" => GamepadButtons.LeftBumper,
        "rb" or "r1" or "r" => GamepadButtons.RightBumper,
        "lt" or "l2" or "zl" => GamepadButtons.LeftTrigger,
        "rt" or "r2" or "zr" => GamepadButtons.RightTrigger,
        "ls" or "l3" or "lstick" => GamepadButtons.LeftStick,
        "rs" or "r3" or "rstick" => GamepadButtons.RightStick,
        "up" or "dpadup" => GamepadButtons.Up,
        "down" or "dpaddown" => GamepadButtons.Down,
        "left" or "dpadleft" => GamepadButtons.Left,
        "right" or "dpadright" => GamepadButtons.Right,
        "start" or "options" or "plus" => GamepadButtons.Start,
        "back" or "share" or "create" or "minus" => GamepadButtons.Back,
        "guide" or "ps" or "home" => GamepadButtons.Guide,
        _ => null,
    };

    /// <summary>The name a button is written by, which is the one its flag came from.</summary>
    public static string Name(GamepadButtons button) => button switch
    {
        GamepadButtons.A => "a",
        GamepadButtons.B => "b",
        GamepadButtons.X => "x",
        GamepadButtons.Y => "y",
        GamepadButtons.LeftBumper => "lb",
        GamepadButtons.RightBumper => "rb",
        GamepadButtons.LeftTrigger => "lt",
        GamepadButtons.RightTrigger => "rt",
        GamepadButtons.LeftStick => "ls",
        GamepadButtons.RightStick => "rs",
        GamepadButtons.Up => "up",
        GamepadButtons.Down => "down",
        GamepadButtons.Left => "left",
        GamepadButtons.Right => "right",
        GamepadButtons.Start => "start",
        GamepadButtons.Back => "back",
        GamepadButtons.Guide => "guide",
        _ => string.Empty,
    };
}

/// <summary>A virtual controller a macro drives, the way a person would with their hands.</summary>
public interface IGamepadDevice
{
    /// <summary>
    /// Puts a controller of this kind on the machine, taking down a controller of another kind
    /// that is already there. The kind is remembered, so the steps after this one do not name it.
    /// </summary>
    void Connect(GamepadKind kind);

    /// <summary>Holds a button down, or lets it up. The name is one of <see cref="GamepadNames.Buttons"/>.</summary>
    void Button(string button, bool down);

    /// <summary>Moves one stick — <c>left</c> or <c>right</c> — to a whole percent from its centre.</summary>
    void Stick(string stick, int x, int y);

    /// <summary>Pulls one trigger — <c>left</c> or <c>right</c> — by a whole percent.</summary>
    void Trigger(string trigger, int amount);

    /// <summary>Lets go of everything, which is what the end of a run leaves behind it.</summary>
    void ReleaseAll();
}
