using System.Collections.Generic;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// Turns the key names a macro writes into the keys the hook and a recording hand over, and back
/// again, so the interface and the hook always agree on what a key is called.
/// </summary>
/// <remarks>
/// This is the face everything outside the engine's device code talks to: it speaks
/// <see cref="HookKey"/> and the two enums beside it rather than the hook library's own codes.
/// Which library reads the keyboard is therefore the engine's business alone.
/// </remarks>
public static class KeyNames
{
    /// <summary>
    /// Every key name a macro may write, in the order a picker reads them, so a key can be chosen
    /// rather than remembered. A modifier is listed by the spelling a chord uses and again with
    /// each side named, because both are written by macros: a chord reads better short, while a
    /// trigger is bound to one particular key and the two shift keys are two different keys.
    /// </summary>
    public static IReadOnlyList<string> Names => SharpKeys.Names;

    /// <summary>The key for a name, or null when the name means nothing.</summary>
    public static HookKey? Resolve(string? name)
        => SharpKeys.ResolveCode(name) is { } code ? SharpKeys.Key(code) : null;

    /// <summary>Splits a chord such as <c>Ctrl+Shift+S</c> into keys.</summary>
    public static List<HookKey> ResolveChord(string? chord, out string? unknown)
        => SharpKeys.ResolveChordCodes(chord, out unknown).ConvertAll(SharpKeys.Key);

    /// <summary>
    /// The name a macro writes for a key, the opposite of <see cref="Resolve"/>. Modifiers are
    /// spelled the way a chord spells them, so a recording and a hand-written macro agree.
    /// </summary>
    public static string Name(HookKey key) => Name(key, sided: false);

    /// <summary>
    /// The same name, optionally keeping the left/right word on the modifiers. A trigger binding
    /// is written with its side (<c>右Shift</c>) because the two shift keys are two different
    /// keys; the side-agnostic name stays the default for chords and recordings, where the
    /// shorter spelling reads better.
    /// </summary>
    public static string Name(HookKey key, bool sided) => SharpKeys.Name(SharpKeys.Code(key), sided);

    /// <summary>
    /// The name a macro writes for a mouse button, so a trigger bound to one reads the same
    /// wherever it is shown. Every name carries "Mouse", which is also how the macro list knows
    /// to draw the mouse icon rather than the keyboard one.
    /// </summary>
    public static string MouseName(HookMouseButton button)
        => SharpKeys.MouseName(SharpKeys.Button(button));

    /// <summary>
    /// The name a macro writes for one notch of the wheel. The sign follows the input device, so
    /// a recording and a trigger agree on which way the wheel was turned.
    /// </summary>
    public static string WheelName(HookWheelAxis axis, int rotation)
        => SharpKeys.WheelName(SharpKeys.Direction(axis), rotation);
}
