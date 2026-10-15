using System;
using System.IO;
using Microsoft.Win32;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The two pieces driver-level input rests on: the usbip-win2 kernel driver, which is installed once
/// and belongs to the machine, and the library the program carries with it, which brings the server
/// and the virtual devices along. All this does is look — the settings window turns that look into
/// "install this", and the driver is the only one of the two a user ever installs.
/// </summary>
public static class DriverInput
{
    /// <summary>
    /// The service the usbip-win2 driver registers, which is what "installed" means. The version is
    /// the installer's business and is deliberately not written down here, where it would only be
    /// wrong the day after that project released again.
    /// </summary>
    private const string DriverService = @"SYSTEM\CurrentControlSet\Services\usbip2_ude";

    /// <summary>The library the program carries, under the name Windows is asked for it.</summary>
    internal const string LibraryFile = "libVIIPER.dll";

    /// <summary>
    /// Whether the driver is on this machine. A check hands in its own answer, so what the settings
    /// window says about a machine that is not this one — one with the driver missing, say — can be
    /// read without asking the registry of the machine the check runs on.
    /// </summary>
    internal static Func<bool> DriverInstalled { get; set; } = FromRegistry;

    /// <summary>The same, for the library the program brings along.</summary>
    internal static Func<bool> LibraryPresent { get; set; } = BesideTheProgram;

    /// <summary>Looks at this machine and says which piece driver-level input is still missing.</summary>
    public static DriverInputState Check()
    {
        if (!DriverInstalled())
        {
            return DriverInputState.DriverMissing;
        }

        return LibraryPresent() ? DriverInputState.Ready : DriverInputState.LibraryMissing;
    }

    private static bool FromRegistry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var service = Registry.LocalMachine.OpenSubKey(DriverService);
            return service is not null;
        }
        catch
        {
            // A key that cannot be read is a driver that cannot be counted on.
            return false;
        }
    }

    /// <summary>
    /// Where the library sits beside the program. A source checkout and the output of a development
    /// build have it next to the exe; a released copy keeps every dll one folder down in lib, and
    /// this one goes there with the rest (see build/package.ps1), so both places are looked at.
    /// </summary>
    private static bool BesideTheProgram()
    {
        var beside = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(beside, LibraryFile))
            || File.Exists(Path.Combine(beside, "lib", LibraryFile));
    }
}
