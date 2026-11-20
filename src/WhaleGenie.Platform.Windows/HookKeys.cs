using SharpHook.Data;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Platform.Windows;

/// <summary>
/// The two vocabularies this project sits between: what the hook library reports and asks for, and
/// what the engine calls the same thing. A key of the engine's is one number to the library and
/// another to Windows, so every crossing is named here rather than cast at the place that needs it.
/// </summary>
/// <remarks>
/// The engine's own naming lives in <see cref="KeyNames"/>, which knows how a macro spells a key;
/// this is the other side of that, and the only place that has both numberings in scope at once.
/// </remarks>
internal static class HookKeys
{
    /// <summary>The key of ours for a code the library reported.</summary>
    internal static HookKey Key(KeyCode code) => KeyNames.Key((HookCode)(int)code);

    /// <summary>
    /// Our name for a button the library reported, or nothing for one it has no name for. Naming
    /// only the buttons a macro can be bound to is what keeps an unnamed one from being taken for
    /// the left button and firing something the user never asked for.
    /// </summary>
    internal static string MouseName(MouseButton button)
        => Mouse(button) is { } named ? KeyNames.MouseName(named) : string.Empty;

    /// <summary>The button of ours a library button stands for, or null for one we do not name.</summary>
    internal static HookMouseButton? Mouse(MouseButton button) => button switch
    {
        MouseButton.Button1 => HookMouseButton.Left,
        MouseButton.Button2 => HookMouseButton.Right,
        MouseButton.Button3 => HookMouseButton.Middle,
        MouseButton.Button4 => HookMouseButton.Back,
        MouseButton.Button5 => HookMouseButton.Forward,
        _ => null,
    };

    /// <summary>Our name for one notch of the wheel the library reported.</summary>
    internal static string WheelName(MouseWheelScrollDirection axis, int rotation)
        => KeyNames.WheelName(Axis(axis), rotation);

    /// <summary>Our name for the way round the library sets the wheel.</summary>
    internal static HookWheelAxis Axis(MouseWheelScrollDirection axis) =>
        axis == MouseWheelScrollDirection.Horizontal
            ? HookWheelAxis.Horizontal
            : HookWheelAxis.Vertical;

    /// <summary>The way round the library spells a wheel that is set one of our ways.</summary>
    internal static MouseWheelScrollDirection Direction(HookWheelAxis axis) =>
        axis == HookWheelAxis.Horizontal
            ? MouseWheelScrollDirection.Horizontal
            : MouseWheelScrollDirection.Vertical;
}
