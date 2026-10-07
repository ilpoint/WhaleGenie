using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Localization;

namespace WhaleGenie.Execution;

/// <summary>
/// Keeps the engine's idea of where viiper.exe is in step with the choice the settings window
/// stores. The engine cannot read the settings file — where it lives is the interface's business —
/// so the choice is handed over here: once when the program starts, and again whenever it changes.
/// </summary>
public static class ViiperSetup
{
    /// <summary>Reads the stored choice and gives it to the engine.</summary>
    public static void Load() => ViiperServer.Executable = LocalSettings.LoadViiperPath();

    /// <summary>Stores a new choice and gives it to the engine.</summary>
    public static void Store(string path)
    {
        LocalSettings.StoreViiperPath(path);
        ViiperServer.Executable = path;
    }
}
