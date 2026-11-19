using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// What every kind of run reads before anything can happen: how this machine captures the screen,
/// how often it may be read, how fast input may leave, whether the program listens to its own
/// output, and how many old run logs to keep. It touches no window and no control, so a run with no
/// window prepares itself the same way the windowed one does.
/// </summary>
internal static class RuntimeSetup
{
    internal static void Prepare()
    {
        // How this machine reads the screen is what a capture answers with, so it is told here
        // rather than when a window happens to open.
        CaptureSetup.Load();

        // How often it may be read, and how long a picture stays worth reading again, for the same
        // reason: a trigger can fire before a settings window has ever been opened.
        FrameSetup.Load();

        // The ceiling on how fast input may leave is read before anything can send any: a trigger
        // can fire while the window is still being built.
        InputLimit.PerSecond = LocalSettings.LoadInputRate();

        // The same for whether the program listens to what it sent itself: the hook is already
        // answering keys by the time a window exists, and a trigger must not act on a macro's own
        // output in the moment it takes to open one.
        WhaleGenieInputGate.IgnoreOwnInput = LocalSettings.LoadIgnoreOwnInput();

        // Old run logs are cleared before this start can add to them, so the folder holds what the
        // setting says rather than one more than that. A folder that cannot be read is not a reason
        // to stop starting up.
        LogRetention.Prune(AppPaths.Logs, LocalSettings.LoadLogCount());
    }
}
