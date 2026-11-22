using System;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Runtime;

/// <summary>
/// Keeps the engine's idea of how often this machine may hand over a picture — and of how long one
/// it has handed over stays worth reading again — in step with what the settings window stores. The
/// engine cannot read the settings file, so the two answers are handed over here: once when the
/// program starts, and again whenever they change. A macro that is running picks a change up at its
/// next look.
/// </summary>
public static class FrameSetup
{
    /// <summary>Reads the stored choices and gives them to the engine.</summary>
    public static void Load(AppSettings settings)
    {
        var hertz = SharedDevices.Current.Screen.RefreshHertz;
        FrameShelf.Current.MaxFramesPerSecond = settings.LoadFrameRateFollow()
            ? 0
            : Math.Clamp(settings.LoadFrameRate(), 1, hertz);
        FrameShelf.Current.CacheMilliseconds = settings.LoadFrameCache()
            ? Math.Clamp(settings.LoadFrameCacheAge(), 0, AppSettings.FrameCacheOldest)
            : 0;
    }

    /// <summary>
    /// The fastest a macro may look on this machine: the display's own rate, which is the most a
    /// second of looking can tell anything. The settings window offers it as the ceiling of the
    /// number it shows, so the two answer the same.
    /// </summary>
    public static int FastestRate => Math.Max(1, SharedDevices.Current.Screen.RefreshHertz);

    /// <summary>Stores how often a picture may be taken, and gives it to the engine.</summary>
    public static void StoreRate(AppSettings settings, bool followDisplay, int hertz)
    {
        var rate = Math.Clamp(hertz, 1, FastestRate);
        settings.StoreFrameRateFollow(followDisplay);
        if (!followDisplay)
        {
            settings.StoreFrameRate(rate);
        }

        FrameShelf.Current.MaxFramesPerSecond = followDisplay ? 0 : rate;
    }

    /// <summary>Stores whether a picture may be read again, and how old it may be.</summary>
    public static void StoreCache(AppSettings settings, bool reuse, int milliseconds)
    {
        var age = Math.Clamp(milliseconds, 0, AppSettings.FrameCacheOldest);
        settings.StoreFrameCache(reuse);
        settings.StoreFrameCacheAge(age);
        FrameShelf.Current.CacheMilliseconds = reuse ? age : 0;
    }
}
