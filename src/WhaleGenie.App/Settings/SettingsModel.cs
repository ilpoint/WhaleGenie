using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Settings;

/// <summary>The persisted shape of the settings file.</summary>
internal sealed class SettingsModel
{
    public string Language { get; set; } = string.Empty;

    public string LastProject { get; set; } = string.Empty;

    public bool PreloadOcr { get; set; }

    public bool DriverAttachAtStart { get; set; }

    public string CaptureWindow { get; set; } = string.Empty;

    public string CaptureScreen { get; set; } = string.Empty;

    public string SystemHotkey { get; set; } = "Home";

    public string MenuLauncher { get; set; } = string.Empty;

    /// <summary>Whether the menu's pin survives closing the menu and restarting the program.</summary>
    public bool RememberMenuPin { get; set; }

    /// <summary>The remembered pin state, read only while <see cref="RememberMenuPin"/> is on.</summary>
    public bool MenuPinned { get; set; }

    /// <summary>
    /// Whether the frame cap follows the display. On by default: the display's own rate is the
    /// fastest a macro can usefully look, and it is what every step used to get when it left the
    /// interval empty.
    /// </summary>
    public bool FrameRateFollow { get; set; } = true;

    /// <summary>The frames a second the person picked, zero while the cap follows the display.</summary>
    public int FrameRateHertz { get; set; }

    /// <summary>
    /// Whether a picture already taken is read again rather than taken again. Off by default:
    /// reuse is a deliberate trade, and a macro that waits for something to disappear must not
    /// be shown a picture from before it disappeared.
    /// </summary>
    public bool FrameCache { get; set; }

    /// <summary>How old a reused picture may be, in milliseconds, when reuse is on.</summary>
    public int FrameCacheAgeMs { get; set; } = 100;

    /// <summary>
    /// Whether a start opens the project that was open when the program was last closed. On by
    /// default: that is what the program has always done, and the choice is here to turn it off.
    /// </summary>
    public bool LoadLastProjectAtStart { get; set; } = true;

    /// <summary>
    /// Whether a change is written back to the package and, while a macro is open, committed as it
    /// is edited. On by default: a person who has just saved a macro should not be asked to save
    /// the project all over again when they leave.
    /// </summary>
    public bool AutoSaveProject { get; set; } = true;

    /// <summary>
    /// Whether triggers listen as soon as the program starts. Off by default: a macro program that
    /// starts taking keyboard and mouse the moment it opens is a surprise nobody asked for.
    /// </summary>
    public bool SystemAtStart { get; set; }

    /// <summary>
    /// Whether enabled macros are listed above disabled ones. Off by default, which keeps the
    /// order the project was made in.
    /// </summary>
    public bool EnabledFirst { get; set; }

    /// <summary>
    /// Events per second this program may send. What the program has always done is the default:
    /// the ceiling is there for the macros that need it, and turning it on by default is what
    /// keeps a fresh install from typing like a machine on the first run.
    /// </summary>
    public int InputRate { get; set; } = WhaleGenie.Core.Devices.InputLimit.Usual;

    /// <summary>
    /// Whether what the program sends is ignored by its own triggers and left out of a
    /// recording. On by default: a macro that presses a bound key would otherwise start another
    /// macro, and a recording made while one runs would play that run back.
    /// </summary>
    public bool IgnoreOwnInput { get; set; } = true;

    /// <summary>
    /// Whether the program's own switch is heard when it goes on or off. On by default: that
    /// hotkey is pressed blind, and nothing else on screen answers at that moment.
    /// </summary>
    public bool SoundSystem { get; set; } = true;

    /// <summary>
    /// Whether arming or disarming one macro is heard. On by default: the dot changes, but the
    /// pointer is usually already moving away from it.
    /// </summary>
    public bool SoundMacro { get; set; } = true;

    /// <summary>
    /// How many failure and MCP logs are kept; the rest are deleted at startup, oldest first.
    /// A hundred by default, which is what ordinary use leaves behind without filling a disk.
    /// </summary>
    public int LogCount { get; set; } = LogRetention.Usual;

    /// <summary>
    /// The shortcut that ends a recording, written as a hotkey is written. Ctrl+F10 by default,
    /// which is what the recording panel has always said out loud.
    /// </summary>
    public string RecordStopKey { get; set; } = AppSettings.UsualRecordStopKey;

    /// <summary>
    /// Whether every start asks Windows for the administrator token. Off by default: a macro
    /// tool that puts a UAC prompt in front of everybody is one people stop starting.
    /// </summary>
    public bool RunAsAdmin { get; set; }

    /// <summary>Zero means no size has been stored, which is not a window size.</summary>
    public double MainWindowWidth { get; set; }

    public double MainWindowHeight { get; set; }

    /// <summary>Whether the shell was maximized when it was last put away.</summary>
    public bool MainWindowMaximized { get; set; }

    public bool McpAllowControl { get; set; }

    public bool McpAllowRun { get; set; }

    public bool LightTheme { get; set; }

    public string Theme { get; set; } = string.Empty;

    public bool CustomThemeLight { get; set; }

    /// <summary>The one colour a custom theme paints with; empty means the built-in accent.</summary>
    public string Accent { get; set; } = string.Empty;

    /// <summary>
    /// Where a custom theme used to keep twenty-two colours, one per interface colour. Read so
    /// that the accent chosen there survives the move to a single colour, and never written:
    /// leaving it behind would look like a palette that had not been migrated yet.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? CustomThemeColors { get; set; } = new();
}
