using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// How the wheel looks and behaves, and the launchers allowed to open it.
/// <summary>
/// The wheel choices that belong to this machine rather than to a project: the key that opens it,
/// its size and its behaviour while open. Written as one object so a new choice does not need a
/// second copy of the read/write pair in the settings file.
/// </summary>
public sealed class WheelOptions
{
    public const int SmallestDiameter = 150;
    public const int LargestDiameter = 600;
    public const int SmallestFont = 8;
    public const int LargestFont = 24;
    public const int LongestTimeoutMs = 120_000;

    public const string MiddleButton = "Mouse Middle";
    public const string RightButton = "Mouse Right";
    public const string BackButton = "Mouse 4";
    public const string ForwardButton = "Mouse 5";
    public const string UsualRepeatKey = "F1";

    /// <summary>
    /// The four buttons a wheel can be opened with. They are the buttons a hand can hold while it
    /// moves: the left button drags things, and the wheel notches belong to switching the scene.
    /// A keyboard binding is deliberately not among them — the gesture is a press-and-move, and a
    /// key that has to be held while the pointer moves is a different interaction.
    /// </summary>
    public static IReadOnlyList<string> LauncherButtons =>
        [RightButton, MiddleButton, ForwardButton, BackButton];

    /// <summary>
    /// The wheel opens on the right button by default. The middle button belongs to the floating
    /// macro menu, and one button cannot own two gestures that both start on a press.
    /// </summary>
    public string Launcher { get; set; } = RightButton;

    public int Diameter { get; set; } = 420;

    public int FontSize { get; set; } = 11;

    public int TimeoutMs { get; set; } = 20_000;

    public bool WideOuter { get; set; }

    public bool ExtendedHint { get; set; } = true;

    public bool HideLabelWithIcon { get; set; }

    public bool CenterName { get; set; }

    public bool ClampToScreen { get; set; } = true;

    public bool MovePointerToCenter { get; set; }

    public string RepeatKey { get; set; } = UsualRepeatKey;

    /// <summary>Capacity of the third resident circle: 0 hides it, otherwise 8, 16 or 24.</summary>
    public int ThirdCapacity { get; set; }

    /// <summary>Hides a whole outer circle when every position in it is empty.</summary>
    public bool HideEmptyOuter { get; set; } = true;

    /// <summary>Shows the line from the centre to the pointer, solid inside and dashed outside.</summary>
    public bool ShowGuide { get; set; } = true;

    /// <summary>
    /// Keeps the trigger button to the programs that have a scene. With this on the wheel opens
    /// only where a program scene matches; the desktop and every other program keep an ordinary
    /// button, which is what makes the wheel feel switched off by default.
    /// </summary>
    public bool OnlyInPrograms { get; set; }

    /// <summary>Uses the application's theme colours instead of a fixed light palette.</summary>
    public bool FollowTheme { get; set; } = true;

    /// <summary>A soft sheen over each sector.</summary>
    public bool Gloss { get; set; } = true;

    /// <summary>A brighter rim around the sector the pointer is on.</summary>
    public bool HoverBorder { get; set; } = true;

    /// <summary>Scales and fades the wheel in when it opens.</summary>
    public bool PopAnimation { get; set; } = true;

    public WheelOptions Copy() => new()
    {
        Launcher = Launcher,
        Diameter = Diameter,
        FontSize = FontSize,
        TimeoutMs = TimeoutMs,
        WideOuter = WideOuter,
        ExtendedHint = ExtendedHint,
        HideLabelWithIcon = HideLabelWithIcon,
        CenterName = CenterName,
        ClampToScreen = ClampToScreen,
        MovePointerToCenter = MovePointerToCenter,
        RepeatKey = RepeatKey,
        ThirdCapacity = ThirdCapacity,
        HideEmptyOuter = HideEmptyOuter,
        ShowGuide = ShowGuide,
        OnlyInPrograms = OnlyInPrograms,
        FollowTheme = FollowTheme,
        Gloss = Gloss,
        HoverBorder = HoverBorder,
        PopAnimation = PopAnimation,
    };

    public WheelOptions Clamped() => new()
    {
        // An empty launcher is a real choice: it means the wheel is switched off. Falling back to
        // the default here would turn the off switch back on after every restart.
        Launcher = Launcher.Trim().Length == 0
            ? string.Empty
            : IsAllowedLauncher(Launcher) ? Launcher.Trim() : RightButton,
        Diameter = Math.Clamp(Diameter, SmallestDiameter, LargestDiameter),
        FontSize = Math.Clamp(FontSize, SmallestFont, LargestFont),
        TimeoutMs = Math.Clamp(TimeoutMs, 0, LongestTimeoutMs),
        WideOuter = WideOuter,
        ExtendedHint = ExtendedHint,
        HideLabelWithIcon = HideLabelWithIcon,
        CenterName = CenterName,
        ClampToScreen = ClampToScreen,
        MovePointerToCenter = MovePointerToCenter,
        RepeatKey = HotkeyBinding.IsSupported(RepeatKey) ? RepeatKey.Trim() : UsualRepeatKey,
        ThirdCapacity = WheelShape.ClampThird(ThirdCapacity),
        HideEmptyOuter = HideEmptyOuter,
        ShowGuide = ShowGuide,
        OnlyInPrograms = OnlyInPrograms,
        FollowTheme = FollowTheme,
        Gloss = Gloss,
        HoverBorder = HoverBorder,
        PopAnimation = PopAnimation,
    };

    /// <summary>
    /// The four buttons of <see cref="LauncherButtons"/>, and nothing else. A stored binding that
    /// is not one of them — a keyboard key from an older file, say — is read back as the default
    /// rather than left in place, because nothing would ever open the wheel with it.
    /// </summary>
    public static bool IsAllowedLauncher(string? binding)
    {
        var text = binding?.Trim() ?? string.Empty;
        return LauncherButtons.Contains(text, StringComparer.OrdinalIgnoreCase);
    }
}
