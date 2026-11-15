using System;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Runtime;

/// <summary>
/// Keeps the engine's idea of how often this machine may hand over a picture in step with what the
/// settings window stores. The engine cannot read the settings file, so the two answers are handed
/// over here: once when the program starts, and again whenever they change. A macro that is running
/// picks a change up at its next look.
/// </summary>
public static class FrameSetup
{
    /// <summary>Reads the stored choices and gives them to the engine.</summary>
    public static void Load()
    {
        var hertz = SharedDevices.Current.Screen.RefreshHertz;
        FramePolicy.Current.MaxFramesPerSecond = LocalSettings.LoadFrameRateFollow()
            ? 0
            : Math.Min(LocalSettings.LoadFrameRate(), hertz);
        FramePolicy.Current.CacheMilliseconds =
            LocalSettings.LoadFrameCache() ? LocalSettings.LoadFrameCacheAge() : 0;
    }

    /// <summary>
    /// The fastest a macro may look on this machine: the display's own rate, which is the most a
    /// second of looking can tell anything. The settings window offers it as the ceiling of the
    /// number it shows, so the two answer the same.
    /// </summary>
    public static int FastestRate => Math.Max(1, SharedDevices.Current.Screen.RefreshHertz);

    /// <summary>Stores how often a picture may be taken, and gives it to the engine.</summary>
    public static void StoreRate(bool followDisplay, int hertz)
    {
        var rate = Math.Clamp(hertz, 1, FastestRate);
        LocalSettings.StoreFrameRateFollow(followDisplay);
        if (!followDisplay)
        {
            LocalSettings.StoreFrameRate(rate);
        }

        FramePolicy.Current.MaxFramesPerSecond = followDisplay ? 0 : rate;
    }

    /// <summary>Stores whether a picture may be read again, and how old it may be.</summary>
    public static void StoreCache(bool reuse, int milliseconds)
    {
        var age = Math.Clamp(milliseconds, 0, LocalSettings.FrameCacheOldest);
        LocalSettings.StoreFrameCache(reuse);
        LocalSettings.StoreFrameCacheAge(age);
        FramePolicy.Current.CacheMilliseconds = reuse ? age : 0;
    }
}
