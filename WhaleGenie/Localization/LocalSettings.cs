using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Localization;

/// <summary>Small JSON file that remembers the choices made in the settings window between runs.</summary>
public static class LocalSettings
{
    /// <summary>
    /// The file the choices live in: in the person's own profile rather than beside the program, so
    /// an update or a reinstall does not take their settings with it. It can be moved so a check
    /// runs against a file of its own instead of the one whoever is using WhaleGenie keeps.
    /// </summary>
    internal static string FilePath { get; set; } = Adopt(AppPaths.Settings, AppPaths.Root);

    /// <summary>
    /// Takes over a file an older build left beside the program. Done once, when this class is first
    /// used: the file is moved rather than copied, so the install folder keeps nothing of the
    /// person's and no second copy can drift away from the one being read.
    /// </summary>
    internal static string Adopt(string wanted, string programFolder)
    {
        var beside = Path.Combine(programFolder, Path.GetFileName(wanted));

        try
        {
            if (File.Exists(wanted) || !File.Exists(beside))
            {
                return wanted;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(wanted)!);
            File.Move(beside, wanted);
            return wanted;
        }
        catch
        {
            // A file that cannot be moved is still readable where it lies: better an old location
            // than silently starting from the defaults and calling the choices lost.
            return File.Exists(beside) ? beside : wanted;
        }
    }

    /// <summary>
    /// The package this copy of the program had open last, so the next start does not begin with an
    /// empty list the person has to fill by hand. A path that no longer exists is still remembered:
    /// the file may be on a drive that is not plugged in, and forgetting it would lose the only
    /// record of where the work was.
    /// </summary>
    public static string LastProject() => Read().LastProject;

    /// <summary>Remembers the package that is open now. An empty path forgets it.</summary>
    public static void StoreLastProject(string? path) => Write(model => model.LastProject = path ?? string.Empty);

    /// <summary>
    /// Whether a start opens the project that was open last time. A start without one is a start
    /// with nothing open, which is what keeping several projects apart asks for.
    /// </summary>
    public static bool LoadLastProjectAtStart() => Read().LoadLastProjectAtStart;

    /// <summary>Stores whether the last project is opened at start.</summary>
    public static void StoreLastProjectAtStart(bool load)
        => Write(model => model.LoadLastProjectAtStart = load);

    /// <summary>
    /// Whether triggers are already listening when the program starts. Off means the system stays
    /// disabled until it is turned on, which is the quieter thing to do on a shared machine.
    /// </summary>
    public static bool LoadSystemAtStart() => Read().SystemAtStart;

    /// <summary>Stores whether the system is enabled at start.</summary>
    public static void StoreSystemAtStart(bool enabled)
        => Write(model => model.SystemAtStart = enabled);

    /// <summary>
    /// How many keyboard or mouse events may leave the program in a second, 0 for no limit. The
    /// ceiling exists because a macro that types as fast as the machine allows reads as a machine:
    /// games drop the input and windows miss clicks.
    /// </summary>
    public static int LoadInputRate() => Read().InputRate;

    /// <summary>Stores the ceiling on how fast input may leave.</summary>
    public static void StoreInputRate(int rate)
        => Write(model => model.InputRate = Math.Clamp(rate, 0, WhaleGenie.Core.Devices.InputLimit.Most));

    /// <summary>
    /// Whether the program keeps keyboard and mouse input to one macro at a time. Off by default:
    /// it only means anything when more than one macro runs at once, and a single macro should not
    /// stop for a door nothing else is knocking on.
    /// </summary>
    public static bool LoadExclusiveInput() => Read().ExclusiveInput;

    /// <summary>Stores whether input is kept to one macro at a time.</summary>
    public static void StoreExclusiveInput(bool exclusive)
        => Write(model => model.ExclusiveInput = exclusive);

    /// <summary>
    /// Whether what this program sends counts as its own rather than as the person's. On by default:
    /// a macro that presses a key another macro is bound to must not start it, and a recording must
    /// not take in what a running macro is doing.
    /// </summary>
    public static bool LoadIgnoreOwnInput() => Read().IgnoreOwnInput;

    /// <summary>Stores whether the program answers its own input.</summary>
    public static void StoreIgnoreOwnInput(bool ignore)
        => Write(model => model.IgnoreOwnInput = ignore);

    /// <summary>
    /// Whether the interface says out loud that the system switch or a macro changed state. On by
    /// default: those two are the moments a click or a key gives no other proof it landed.
    /// </summary>
    public static bool LoadSounds() => Read().Sounds;

    /// <summary>Stores whether the interface plays those sounds.</summary>
    public static void StoreSounds(bool on) => Write(model => model.Sounds = on);

    /// <summary>
    /// How many failure and MCP logs are kept, counted by modification time. A hundred is what a
    /// year of ordinary use leaves behind without filling anything, and it is enough to go back to
    /// the failure that started all this.
    /// </summary>
    public static int LoadLogCount() => Math.Clamp(
        Read().LogCount, LogRetention.Fewest, LogRetention.Most);

    /// <summary>Stores how many of those logs are kept.</summary>
    public static void StoreLogCount(int count) => Write(model => model.LogCount = Math.Clamp(
        count, LogRetention.Fewest, LogRetention.Most));

    /// <summary>
    /// The shortcut that ends a recording. Ctrl+F10 by default: it is out of the way of the work
    /// being recorded, and a recording that could only be stopped with the mouse is a recording
    /// that keeps writing while the pointer goes looking for the button.
    /// </summary>
    public static string LoadRecordStopKey()
    {
        var key = Read().RecordStopKey.Trim();
        return key.Length > 0 ? key : UsualRecordStopKey;
    }

    /// <summary>What a recording is stopped with when nothing else has been chosen.</summary>
    public const string UsualRecordStopKey = "Ctrl+F10";

    /// <summary>Stores that shortcut.</summary>
    public static void StoreRecordStopKey(string key) => Write(model => model.RecordStopKey = key.Trim());

    /// <summary>
    /// Whether every start asks Windows for the administrator token. Off by default: a macro tool
    /// that puts a UAC prompt in front of everybody, including the macros that never touch an
    /// elevated window, is a tool people stop starting.
    /// </summary>
    public static bool LoadRunAsAdmin() => Read().RunAsAdmin;

    /// <summary>Stores whether a start asks for the administrator token.</summary>
    public static void StoreRunAsAdmin(bool run) => Write(model => model.RunAsAdmin = run);

    /// <summary>Which sound this moment plays, or the one it starts with when nothing was chosen.</summary>
    public static SoundKind LoadSound(UiSound which)
    {
        var model = Read();
        return UiSounds.Read(which switch
        {
            UiSound.SystemOn => model.SoundSystemOn,
            UiSound.SystemOff => model.SoundSystemOff,
            UiSound.MacroOn => model.SoundMacroOn,
            _ => model.SoundMacroOff,
        }, which);
    }

    /// <summary>Stores which sound this moment plays.</summary>
    public static void StoreSound(UiSound which, SoundKind kind)
        => Write(model =>
        {
            var written = UiSounds.Written(kind);
            switch (which)
            {
                case UiSound.SystemOn:
                    model.SoundSystemOn = written;
                    break;
                case UiSound.SystemOff:
                    model.SoundSystemOff = written;
                    break;
                case UiSound.MacroOn:
                    model.SoundMacroOn = written;
                    break;
                default:
                    model.SoundMacroOff = written;
                    break;
            }
        });

    /// <summary>
    /// Whether the macro list puts enabled macros first. Off keeps the order they were made in.
    /// </summary>
    public static bool LoadEnabledFirst() => Read().EnabledFirst;

    /// <summary>Stores whether enabled macros are listed first.</summary>
    public static void StoreEnabledFirst(bool first) => Write(model => model.EnabledFirst = first);

    /// <summary>Whether the window frame and controls use the light variant.</summary>
    public static bool LoadLightTheme() => Read().LightTheme;

    /// <summary>Stores the chosen theme variant. Failures are ignored on purpose.</summary>
    public static void StoreLightTheme(bool light) => Write(model => model.LightTheme = light);

    /// <summary>
    /// The whole theme choice. A file from before custom themes carried only <see cref="Model.LightTheme"/>,
    /// so that flag is the fallback when no mode was written. A file from before this version kept a
    /// dictionary of twenty-two colours; only the accent in it still means anything, so that one entry
    /// is carried over and the rest are dropped rather than left to rot.
    /// </summary>
    public static ThemeSelection LoadTheme()
    {
        var model = Read();
        var mode = Enum.TryParse<ThemeMode>(model.Theme, true, out var selected)
            ? selected
            : model.LightTheme ? ThemeMode.Light : ThemeMode.Dark;

        var accent = model.Accent;
        if (string.IsNullOrWhiteSpace(accent)
            && model.CustomThemeColors is { } palette
            && palette.TryGetValue("AppAccent", out var legacy))
        {
            accent = legacy;
        }

        // Only a custom theme has a base of its own; the built-in light and dark choices say theirs
        // in the mode, and reading a stale flag over it would leave "light" painting dark controls.
        var baseLight = mode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => model.CustomThemeLight,
        };

        return new ThemeSelection(
            mode,
            baseLight,
            string.IsNullOrWhiteSpace(accent) ? null : accent);
    }

    /// <summary>Stores the whole theme choice; the old flag is kept for older copies of the program.</summary>
    public static void StoreTheme(ThemeSelection selection)
        => Write(model =>
        {
            model.Theme = selection.Mode.ToString().ToLowerInvariant();
            model.CustomThemeLight = selection.BaseLight;
            model.Accent = selection.AccentHex ?? string.Empty;
            model.LightTheme = selection.Mode is ThemeMode.Light;

            // Writing is where the old shape goes away: a dictionary left in the file would read as
            // a palette that was never migrated, and the next start would pick the accent out of it
            // over the one just chosen.
            model.CustomThemeColors = null;
        });

    /// <summary>
    /// Reads the stored language. A machine that has never chosen one follows its Windows display
    /// language: Chinese when that is Chinese, English otherwise.
    /// </summary>
    public static Language Load()
        => Enum.TryParse<Language>(Read().Language, out var language)
            ? language
            : ForCulture(CultureInfo.CurrentUICulture);

    /// <summary>The interface language a system language maps to when nothing has been chosen.</summary>
    internal static Language ForCulture(CultureInfo culture)
        => culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? Language.Chinese
            : Language.English;

    /// <summary>Stores the chosen language. Failures are ignored on purpose.</summary>
    public static void Store(Language language) => Write(model => model.Language = language.ToString());

    /// <summary>
    /// Whether the text recognition models are loaded in the background as the program starts. Off
    /// by default: loading them holds memory for the whole run, and a machine that never reads text
    /// should not pay for a machine that does.
    /// </summary>
    public static bool LoadPreloadOcr() => Read().PreloadOcr;

    /// <summary>Stores that choice. Failures are ignored on purpose.</summary>
    public static void StorePreloadOcr(bool value) => Write(model => model.PreloadOcr = value);

    /// <summary>
    /// Whether the virtual keyboard and mouse are put on the machine as the program starts. Off by
    /// default: a machine that never uses driver-level input should not have virtual devices on it,
    /// and putting them on is a wait only the first driver-level step of a macro would pay for.
    /// </summary>
    public static bool LoadDriverAttachAtStart() => Read().DriverAttachAtStart;

    /// <summary>Stores that choice. Failures are ignored on purpose.</summary>
    public static void StoreDriverAttachAtStart(bool value)
        => Write(model => model.DriverAttachAtStart = value);

    /// <summary>
    /// The two permissions the MCP flyout last handed out. Remembered so the
    /// block a person copies next time is the one they meant, rather than a bare "read only" again.
    /// </summary>
    public static (bool Control, bool Run) LoadMcpGrants()
    {
        var model = Read();
        return (model.McpAllowControl, model.McpAllowRun);
    }

    /// <summary>Stores those two choices. Failures are ignored on purpose.</summary>
    public static void StoreMcpGrants(bool control, bool run) => Write(model =>
    {
        model.McpAllowControl = control;
        model.McpAllowRun = run;
    });

    /// <summary>
    /// How this machine reads a picture of a window. Automatic by default, which is the order the
    /// machine has always tried the ways in.
    /// </summary>
    public static CaptureWay LoadCaptureWindow()
        => CaptureWayNames.Read(Read().CaptureWindow);

    /// <summary>Stores that choice, as the way is written in a file. Failures are ignored on purpose.</summary>
    public static void StoreCaptureWindow(CaptureWay way)
        => Write(model => model.CaptureWindow = CaptureWayNames.Written(way));

    /// <summary>
    /// How this machine reads a picture of the desktop. Automatic by default, which is the order the
    /// machine has always tried the ways in.
    /// </summary>
    public static CaptureWay LoadCaptureScreen()
        => CaptureWayNames.Read(Read().CaptureScreen);

    /// <summary>Stores that choice, as the way is written in a file. Failures are ignored on purpose.</summary>
    public static void StoreCaptureScreen(CaptureWay way)
        => Write(model => model.CaptureScreen = CaptureWayNames.Written(way));

    /// <summary>The global key that switches all triggers on or off. Home remains the default.</summary>
    public static string LoadSystemHotkey()
        => Read().SystemHotkey.Trim();

    public static void StoreSystemHotkey(string value)
        => Write(model => model.SystemHotkey = value.Trim());

    /// <summary>The key that opens the floating macro menu on this machine. Empty means none.</summary>
    public static string LoadMenuLauncher() => Read().MenuLauncher.Trim();

    public static void StoreMenuLauncher(string value)
        => Write(model => model.MenuLauncher = value.Trim());

    /// <summary>
    /// The size the main window was last closed at. A missing or invalid size means this machine
    /// has not chosen one yet, so the window keeps the size declared in XAML.
    /// </summary>
    public static (double Width, double Height)? LoadMainWindowSize()
    {
        var model = Read();
        return UsableSize(model.MainWindowWidth) && UsableSize(model.MainWindowHeight)
            ? (model.MainWindowWidth, model.MainWindowHeight)
            : null;
    }

    public static void StoreMainWindowSize(double width, double height)
    {
        if (!UsableSize(width) || !UsableSize(height))
        {
            return;
        }

        Write(model =>
        {
            model.MainWindowWidth = width;
            model.MainWindowHeight = height;
        });
    }

    private static bool UsableSize(double value) => double.IsFinite(value) && value > 0;

    public static DockPage LoadDockPage()
        => Enum.TryParse<DockPage>(Read().DockPage, true, out var page) ? page : DockPage.Keys;

    public static void StoreDockPage(DockPage page)
        => Write(model => model.DockPage = page.ToString());

    private static Model Read()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath)) ?? new Model()
                : new Model();
        }
        catch
        {
            return new Model();
        }
    }

    /// <summary>
    /// Changes one choice and writes the file back. The whole file is written every time, so the
    /// choice being changed has to start from what was already stored or it would drop the rest.
    /// </summary>
    private static void Write(Action<Model> change)
    {
        try
        {
            var model = Read();
            change(model);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(model));
        }
        catch
        {
            // A settings file that cannot be written must never break the app.
        }
    }

    private sealed class Model
    {
        public string Language { get; set; } = string.Empty;

        public string LastProject { get; set; } = string.Empty;

        public bool PreloadOcr { get; set; }

        public bool DriverAttachAtStart { get; set; }

        public string CaptureWindow { get; set; } = string.Empty;

        public string CaptureScreen { get; set; } = string.Empty;

        public string SystemHotkey { get; set; } = "Home";

        public string MenuLauncher { get; set; } = string.Empty;

        /// <summary>
        /// Whether a start opens the project that was open when the program was last closed. On by
        /// default: that is what the program has always done, and the choice is here to turn it off.
        /// </summary>
        public bool LoadLastProjectAtStart { get; set; } = true;

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
        /// Whether keyboard and mouse input is kept to one macro at a time. Off by default: with one
        /// macro running there is nothing to wait for, and turning it on by default would make every
        /// macro wait for a door it has never needed.
        /// </summary>
        public bool ExclusiveInput { get; set; }

        /// <summary>
        /// Whether what the program sends is ignored by its own triggers and left out of a
        /// recording. On by default: a macro that presses a bound key would otherwise start another
        /// macro, and a recording made while one runs would play that run back.
        /// </summary>
        public bool IgnoreOwnInput { get; set; } = true;

        /// <summary>
        /// Whether the interface plays a sound when the system switch or a macro changes state. On
        /// by default: those two are the moments a click or a key gives no other proof it landed.
        /// </summary>
        public bool Sounds { get; set; } = true;

        /// <summary>
        /// The sound each of the four moments plays. Empty means "the one it starts with", which is
        /// how a file written before these choices existed is read.
        /// </summary>
        public string SoundSystemOn { get; set; } = string.Empty;

        public string SoundSystemOff { get; set; } = string.Empty;

        public string SoundMacroOn { get; set; } = string.Empty;

        public string SoundMacroOff { get; set; } = string.Empty;

        /// <summary>
        /// How many failure and MCP logs are kept; the rest are deleted at startup, oldest first.
        /// A hundred by default, which is what ordinary use leaves behind without filling a disk.
        /// </summary>
        public int LogCount { get; set; } = LogRetention.Usual;

        /// <summary>
        /// The shortcut that ends a recording, written as a hotkey is written. Ctrl+F10 by default,
        /// which is what the recording panel has always said out loud.
        /// </summary>
        public string RecordStopKey { get; set; } = UsualRecordStopKey;

        /// <summary>
        /// Whether every start asks Windows for the administrator token. Off by default: a macro
        /// tool that puts a UAC prompt in front of everybody is one people stop starting.
        /// </summary>
        public bool RunAsAdmin { get; set; }

        /// <summary>Zero means no size has been stored, which is not a window size.</summary>
        public double MainWindowWidth { get; set; }

        public double MainWindowHeight { get; set; }

        public string DockPage { get; set; } = nameof(Models.DockPage.Keys);

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
}
