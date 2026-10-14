using System;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// The buttons of the virtual controller, named the way they are printed on it: the pad the
/// program puts on the machine is an Xbox 360 one, which is the controller Windows games look for,
/// so the names a macro writes are that pad's names. One vocabulary, because it is one pad.
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

    /// <summary>
    /// The name a trigger is bound to, which carries "Pad" the way a mouse button's name carries
    /// "Mouse": a binding then says which device it is about without anyone having to be told, and
    /// the shared comparison keeps the two vocabularies apart — a binding of <c>Pad A</c> is not the
    /// letter key <c>A</c>.
    /// </summary>
    /// <remarks>
    /// The Guide button has no name here. Windows keeps that button to itself — it never appears in
    /// what <c>XInputGetState</c> reports, which is where a bound button is read from — so offering
    /// it would be offering a binding that can never fire.
    /// </remarks>
    public static string Binding(GamepadButtons button) => button switch
    {
        GamepadButtons.A => "Pad A",
        GamepadButtons.B => "Pad B",
        GamepadButtons.X => "Pad X",
        GamepadButtons.Y => "Pad Y",
        GamepadButtons.LeftBumper => "Pad LB",
        GamepadButtons.RightBumper => "Pad RB",
        GamepadButtons.LeftTrigger => "Pad LT",
        GamepadButtons.RightTrigger => "Pad RT",
        GamepadButtons.LeftStick => "Pad LS",
        GamepadButtons.RightStick => "Pad RS",
        GamepadButtons.Up => "Pad Up",
        GamepadButtons.Down => "Pad Down",
        GamepadButtons.Left => "Pad Left",
        GamepadButtons.Right => "Pad Right",
        GamepadButtons.Start => "Pad Start",
        GamepadButtons.Back => "Pad Back",
        _ => string.Empty,
    };
}

/// <summary>
/// The controller in the person's hands, as this machine reports it: what a trigger bound to a
/// controller button answers to, what the editor takes a binding from, and what a recording writes
/// down. It is read rather than subscribed to, because Windows has no word for "a controller button
/// went down" — what there is, is the state of the controller at a moment.
/// </summary>
/// <remarks>
/// It sits on the device layer beside the hook rather than inside the virtual controller: the
/// virtual one is what a macro drives, this is what the person at the machine drives. It is also
/// what makes the whole thing checkable — a check hands in a controller it can play, and nothing
/// about a trigger or a recording then needs a controller to be plugged in.
/// </remarks>
public interface IGamepadReading
{
    /// <summary>True when a controller is plugged in for there to be anything to read.</summary>
    bool Connected { get; }

    /// <summary>
    /// Which buttons are held down at this moment, or none when no controller is plugged in. A
    /// trigger counts as held once it is pulled past a little of its travel, because a binding has
    /// two states and a trigger has a range.
    /// </summary>
    GamepadButtons Held();
}

/// <summary>A machine with no controller to read, which is what a check gets unless it brings one.</summary>
public sealed class NoGamepadReading : IGamepadReading
{
    public bool Connected => false;

    public GamepadButtons Held() => GamepadButtons.None;
}

/// <summary>
/// Turns what a controller is doing now into what changed since it was last looked at: which
/// buttons went down and which came back up.
/// </summary>
/// <remarks>
/// A reading is a snapshot, so a press only exists as the difference between two of them, and
/// somebody has to hold the earlier one. Doing that here keeps the trigger, the picker and the
/// recorder from each folding the same two snapshots in their own way — and it is the whole of what
/// a controller contributes that can be worked out without a controller, so it is where the
/// headless checks have something to hold on to.
/// </remarks>
public sealed class GamepadButtonWatch
{
    private GamepadButtons _held;

    /// <summary>Forgets what was held, as if the controller had just been picked up.</summary>
    public void Forget() => _held = GamepadButtons.None;

    /// <summary>
    /// What changed between the last reading and this one: each button with whether it is now down.
    /// A button that was already held stays quiet — holding a button is one press, however many
    /// times it is read while it is down.
    /// </summary>
    public IEnumerable<(GamepadButtons Button, bool Down)> Changed(GamepadButtons now)
    {
        var pressed = now & ~_held;
        var released = _held & ~now;
        _held = now;

        for (var bit = 1; bit <= (int)GamepadButtons.Guide; bit <<= 1)
        {
            var button = (GamepadButtons)bit;
            if ((pressed & button) != 0)
            {
                yield return (button, true);
            }
            else if ((released & button) != 0)
            {
                yield return (button, false);
            }
        }
    }
}

/// <summary>A virtual controller a macro drives, the way a person would with their hands.</summary>
public interface IGamepadDevice
{
    /// <summary>
    /// Puts the controller on the machine. Every step that drives one does this for itself, so
    /// this is for a macro that wants the pad there before a game looks at what is connected.
    /// </summary>
    void Connect();

    /// <summary>Holds a button down, or lets it up. The name is one of <see cref="GamepadNames.Buttons"/>.</summary>
    void Button(string button, bool down);

    /// <summary>Moves one stick — <c>left</c> or <c>right</c> — to a whole percent from its centre.</summary>
    void Stick(string stick, int x, int y);

    /// <summary>Pulls one trigger — <c>left</c> or <c>right</c> — by a whole percent.</summary>
    void Trigger(string trigger, int amount);

    /// <summary>Lets go of everything, which is what the end of a run leaves behind it.</summary>
    void ReleaseAll();
}
