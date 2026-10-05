using System;
using System.Globalization;
using System.IO;

namespace Viktor.Core.Devices.Platform;

/// <summary>Odds and ends about this machine: who is signed in, and where things live.</summary>
public sealed class WindowsSystemDevice : ISystemDevice
{
    public string Info(string field) => field.Trim().ToLowerInvariant() switch
    {
        "username" => System.Environment.UserName,
        "machinename" => System.Environment.MachineName,
        "userdomain" => System.Environment.UserDomainName,
        "osversion" => System.Environment.OSVersion.VersionString,
        "processorcount" => System.Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
        "systemfolder" => System.Environment.SystemDirectory,
        "tempfolder" => Trimmed(Path.GetTempPath()),
        "userfolder" => Folder(System.Environment.SpecialFolder.UserProfile),
        "desktopfolder" => Folder(System.Environment.SpecialFolder.DesktopDirectory),
        "documentsfolder" => Folder(System.Environment.SpecialFolder.MyDocuments),
        "downloadsfolder" => Path.Combine(Folder(System.Environment.SpecialFolder.UserProfile), "Downloads"),
        "startupfolder" => Folder(System.Environment.SpecialFolder.Startup),
        "appdatafolder" => Folder(System.Environment.SpecialFolder.ApplicationData),
        "programfiles" => Folder(System.Environment.SpecialFolder.ProgramFiles),
        _ => throw new DeviceActionException("Run.UnknownSystemField", field),
    };

    public string Environment(string name)
        => string.IsNullOrWhiteSpace(name)
            ? throw new DeviceActionException("Run.MissingName")
            : System.Environment.GetEnvironmentVariable(name.Trim()) ?? string.Empty;

    private static string Folder(System.Environment.SpecialFolder folder)
        => System.Environment.GetFolderPath(folder);

    /// <summary>A folder path without the trailing separator, so it joins with a file name.</summary>
    private static string Trimmed(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
