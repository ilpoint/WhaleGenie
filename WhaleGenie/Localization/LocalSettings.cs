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
    /// The file the choices live in: beside the program, so a copy of WhaleGenie carries its own
    /// choices with it. It can be moved so a check runs against a file of its own instead of the
    /// one whoever is using WhaleGenie keeps.
    /// </summary>
    internal static string FilePath { get; set; } = AppPaths.Settings;

    /// <summary>
    /// The package this copy of the program had open last, so the next start does not begin with an
    /// empty list the person has to fill by hand. A path that no longer exists is still remembered:
    /// the file may be on a drive that is not plugged in, and forgetting it would lose the only
    /// record of where the work was.
    /// </summary>
    public static string LastProject() => Read().LastProject;

    /// <summary>Remembers the package that is open now. An empty path forgets it.</summary>
    public static void StoreLastProject(string? path) => Write(model => model.LastProject = path ?? string.Empty);

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
