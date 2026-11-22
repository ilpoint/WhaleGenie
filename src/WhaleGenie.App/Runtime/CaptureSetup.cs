using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;

namespace WhaleGenie.Runtime;

/// <summary>
/// Keeps the engine's idea of how this machine reads the screen in step with the choice the
/// settings window stores. The engine cannot read the settings file — where it lives is the
/// interface's business — so the two answers are handed over here: once when the program starts,
/// and again whenever they change. A macro that is running picks a change up at its next look.
/// </summary>
public static class CaptureSetup
{
    /// <summary>Reads the stored choices and gives them to the engine.</summary>
    public static void Load(AppSettings settings)
    {
        CaptureChoice.Current.Window = settings.LoadCaptureWindow();
        CaptureChoice.Current.Screen = settings.LoadCaptureScreen();
    }

    /// <summary>Stores how a window's picture is taken, and gives it to the engine.</summary>
    public static void StoreWindow(AppSettings settings, CaptureWay way)
    {
        settings.StoreCaptureWindow(way);
        CaptureChoice.Current.Window = way;
    }

    /// <summary>Stores how the desktop's picture is taken, and gives it to the engine.</summary>
    public static void StoreScreen(AppSettings settings, CaptureWay way)
    {
        settings.StoreCaptureScreen(way);
        CaptureChoice.Current.Screen = way;
    }
}
