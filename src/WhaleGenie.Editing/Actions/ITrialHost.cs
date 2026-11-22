using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;

namespace WhaleGenie.Editing;

/// <summary>
/// The machine services a trial run needs: the built-in variables seeded from live devices, and a
/// screen frame read after the desktop settled. The app layer owns both; the editor only asks.
/// </summary>
internal interface ITrialHost
{
    VariableStore SeedVariables(IDeviceLayer devices, VariableSession variables);

    ImageFrame CaptureScreen(IScreenDevice screen, int x, int y, int width, int height);
}
