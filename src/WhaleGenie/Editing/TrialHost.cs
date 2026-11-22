using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Editing;

/// <summary>The app layer's machine services behind the editor's trial port.</summary>
internal sealed class TrialHost : ITrialHost
{
    public VariableStore SeedVariables(IDeviceLayer devices, VariableSession variables)
        => MacroVariables.Seed(new SystemVariableReader(devices), variables);

    public ImageFrame CaptureScreen(IScreenDevice screen, int x, int y, int width, int height)
        => ScreenReady.Capture(screen, x, y, width, height);
}
