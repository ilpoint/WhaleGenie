using System.IO;

using WhaleGenie.Core.Devices;
using WhaleGenie.Platform.Windows;

namespace WhaleGenie.Platform.Tests;

/// <summary>
/// What the script device asks pip for. Which packages are already there is the device's own
/// reading of the folder, so the stand-in for the process device is what the question runs against.
/// </summary>
public class ScriptDevicePackageTests
{
    [Fact]
    public void Installing_packages_leaves_the_ones_that_are_already_there_alone()
    {
        var folder = Directory.CreateTempSubdirectory("whalegenie-pip").FullName;
        try
        {
            // What pip leaves behind when a package is installed: a folder named after the package
            // and the version it was installed at, under the one packages of this program live in.
            Directory.CreateDirectory(Path.Combine(folder, "python", "PyYAML-6.0.dist-info"));
            var devices = new FakeDeviceLayer();
            var scripts = new ScriptDevice(devices.Processes, folder, string.Empty);
            scripts.InstallPip(new PipRequest(["PyYAML==6.0", "requests"]), _ => { }, _ => { },
                default);
            // Only what is missing is asked for, pip is told to put it in the folder the scripts
            // import from, and the version a step pinned is part of what "already there" means.
            Assert.Contains(devices.Calls, call =>
                call.StartsWith("run python|-m pip install")
                && call.Contains(Path.Combine(folder, "python")));
            Assert.Contains(devices.Calls, call => call.Contains("requests"));
            Assert.DoesNotContain(devices.Calls, call => call.Contains("PyYAML"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
