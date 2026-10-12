using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Execution;

/// <summary>
/// Keeps the engine's idea of how this machine reads the screen in step with the choice the
/// settings window stores. The engine cannot read the settings file — where it lives is the
/// interface's business — so the two answers are handed over here: once when the program starts,
/// and again whenever they change. A macro that is running picks a change up at its next look.
/// </summary>
public static class CaptureSetup
{
    /// <summary>Reads the stored choices and gives them to the engine.</summary>
    public static void Load()
    {
        CaptureChoice.Current.Window = LocalSettings.LoadCaptureWindow();
        CaptureChoice.Current.Screen = LocalSettings.LoadCaptureScreen();
    }

    /// <summary>Stores how a window's picture is taken, and gives it to the engine.</summary>
    public static void StoreWindow(CaptureMethod way)
    {
        LocalSettings.StoreCaptureWindow(way);
        CaptureChoice.Current.Window = way;
    }

    /// <summary>Stores how the desktop's picture is taken, and gives it to the engine.</summary>
    public static void StoreScreen(CaptureMethod way)
    {
        LocalSettings.StoreCaptureScreen(way);
        CaptureChoice.Current.Screen = way;
    }
}
