using System;
using System.Globalization;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Models;

namespace WhaleGenie.Localization;

/// <summary>
/// The program's one settings file, reached without handing an instance around. Everything here
/// forwards to <see cref="AppSettings.Shared"/>; new code takes an <see cref="AppSettings"/> and
/// leaves this facade for the call sites that still read a choice during startup.
/// </summary>
public static class LocalSettings
{
    private static AppSettings Settings => AppSettings.Shared;

    /// <summary>The file the choices live in; changing it drops the in-memory copy.</summary>
    internal static string FilePath
    {
        get => Settings.FilePath;
        set => Settings.FilePath = value;
    }

    /// <summary>Reads the file again after something outside this class changed it.</summary>
    internal static void Forget() => Settings.Forget();

    /// <summary>The oldest a reused picture may be, in milliseconds.</summary>
    public const int FrameCacheOldest = AppSettings.FrameCacheOldest;

    /// <summary>What a recording is stopped with when nothing else has been chosen.</summary>
    public const string UsualRecordStopKey = AppSettings.UsualRecordStopKey;

    /// <summary>Takes over a file an older build left beside the program.</summary>
    internal static string Adopt(string wanted, string programFolder)
        => AppSettings.Adopt(wanted, programFolder);

    public static string LastProject() => Settings.LastProject();

    public static void StoreLastProject(string? path) => Settings.StoreLastProject(path);

    public static bool LoadLastProjectAtStart() => Settings.LoadLastProjectAtStart();

    public static void StoreLastProjectAtStart(bool load) => Settings.StoreLastProjectAtStart(load);

    public static bool LoadSystemAtStart() => Settings.LoadSystemAtStart();

    public static void StoreSystemAtStart(bool enabled) => Settings.StoreSystemAtStart(enabled);

    public static int LoadInputRate() => Settings.LoadInputRate();

    public static void StoreInputRate(int rate) => Settings.StoreInputRate(rate);

    public static bool LoadIgnoreOwnInput() => Settings.LoadIgnoreOwnInput();

    public static void StoreIgnoreOwnInput(bool ignore) => Settings.StoreIgnoreOwnInput(ignore);

    public static bool LoadSoundSystem() => Settings.LoadSoundSystem();

    public static void StoreSoundSystem(bool on) => Settings.StoreSoundSystem(on);

    public static bool LoadSoundMacro() => Settings.LoadSoundMacro();

    public static void StoreSoundMacro(bool on) => Settings.StoreSoundMacro(on);

    public static int LoadLogCount() => Settings.LoadLogCount();

    public static void StoreLogCount(int count) => Settings.StoreLogCount(count);

    public static string LoadRecordStopKey() => Settings.LoadRecordStopKey();

    public static void StoreRecordStopKey(string key) => Settings.StoreRecordStopKey(key);

    public static bool LoadRunAsAdmin() => Settings.LoadRunAsAdmin();

    public static void StoreRunAsAdmin(bool run) => Settings.StoreRunAsAdmin(run);

    public static bool LoadEnabledFirst() => Settings.LoadEnabledFirst();

    public static void StoreEnabledFirst(bool first) => Settings.StoreEnabledFirst(first);

    public static bool LoadLightTheme() => Settings.LoadLightTheme();

    public static void StoreLightTheme(bool light) => Settings.StoreLightTheme(light);

    public static ThemeSelection LoadTheme() => Settings.LoadTheme();

    public static void StoreTheme(ThemeSelection selection) => Settings.StoreTheme(selection);

    public static Language Load() => Settings.Load();

    public static void Store(Language language) => Settings.Store(language);

    internal static Language ForCulture(CultureInfo culture) => Settings.ForCulture(culture);

    public static bool LoadPreloadOcr() => Settings.LoadPreloadOcr();

    public static void StorePreloadOcr(bool value) => Settings.StorePreloadOcr(value);

    public static bool LoadDriverAttachAtStart() => Settings.LoadDriverAttachAtStart();

    public static void StoreDriverAttachAtStart(bool value) => Settings.StoreDriverAttachAtStart(value);

    public static (bool Control, bool Run) LoadMcpGrants() => Settings.LoadMcpGrants();

    public static void StoreMcpGrants(bool control, bool run) => Settings.StoreMcpGrants(control, run);

    public static CaptureWay LoadCaptureWindow() => Settings.LoadCaptureWindow();

    public static void StoreCaptureWindow(CaptureWay way) => Settings.StoreCaptureWindow(way);

    public static CaptureWay LoadCaptureScreen() => Settings.LoadCaptureScreen();

    public static void StoreCaptureScreen(CaptureWay way) => Settings.StoreCaptureScreen(way);

    public static bool LoadFrameRateFollow() => Settings.LoadFrameRateFollow();

    public static void StoreFrameRateFollow(bool follow) => Settings.StoreFrameRateFollow(follow);

    public static int LoadFrameRate() => Settings.LoadFrameRate();

    public static void StoreFrameRate(int hertz) => Settings.StoreFrameRate(hertz);

    public static bool LoadFrameCache() => Settings.LoadFrameCache();

    public static void StoreFrameCache(bool cache) => Settings.StoreFrameCache(cache);

    public static int LoadFrameCacheAge() => Settings.LoadFrameCacheAge();

    public static void StoreFrameCacheAge(int milliseconds) => Settings.StoreFrameCacheAge(milliseconds);

    public static string LoadSystemHotkey() => Settings.LoadSystemHotkey();

    public static void StoreSystemHotkey(string value) => Settings.StoreSystemHotkey(value);

    public static string LoadMenuLauncher() => Settings.LoadMenuLauncher();

    public static void StoreMenuLauncher(string value) => Settings.StoreMenuLauncher(value);

    public static (double Width, double Height)? LoadMainWindowSize() => Settings.LoadMainWindowSize();

    public static void StoreMainWindowSize(double width, double height)
        => Settings.StoreMainWindowSize(width, height);

    public static bool LoadMainWindowMaximized() => Settings.LoadMainWindowMaximized();

    public static void StoreMainWindowMaximized(bool maximized)
        => Settings.StoreMainWindowMaximized(maximized);
}
