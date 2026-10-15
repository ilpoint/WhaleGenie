using System;
using System.IO;
using Microsoft.Win32;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Where the AutoHotkey program is on this machine, or null when there is none. AutoHotkey is one
/// of the two things a script step may need and Windows does not ship — Python is the other — so
/// "this machine has no AutoHotkey" is a thing the engine has to be able to say rather than a
/// command line that fails for a reason of its own.
/// </summary>
public static class AutoHotkey
{
    /// <summary>
    /// The class its own installer registers a script file under, whose open command names the
    /// program. This is the same place Windows looks when a .ahk file is double-clicked, so a
    /// machine where scripts do run is a machine this finds.
    /// </summary>
    private const string ScriptClass = @"AutoHotkeyScript\shell\open\command";

    /// <summary>
    /// The names the program answers to on the search path, newest first: version 2 is what the
    /// current release installs and what a script written for this step should be written in.
    /// </summary>
    private static readonly string[] OnPath = ["AutoHotkey64.exe", "AutoHotkey.exe"];

    /// <summary>
    /// Where an installation puts it. The two versions sit in folders of their own, and version 1
    /// installs straight into the folder named after the program.
    /// </summary>
    private static readonly string[] Installed =
    [
        @"AutoHotkey\v2\AutoHotkey64.exe",
        @"AutoHotkey\v2\AutoHotkey32.exe",
        @"AutoHotkey\AutoHotkey.exe",
    ];

    /// <summary>
    /// The program this machine runs a script with, or null when it has none. A check hands in its
    /// own answer the way <see cref="DriverInput"/> lets one, so what a macro does on a machine
    /// without AutoHotkey can be read without uninstalling it.
    /// </summary>
    internal static Func<string?> Program { get; set; } = Locate;

    private static string? Locate()
        => FromRegistry() ?? FromSearchPath() ?? FromInstallFolder();

    /// <summary>
    /// What the installer wrote for a script file: the whole command line, with the program in
    /// quotes and the file it is given after it.
    /// </summary>
    private static string? FromRegistry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(ScriptClass);
            if (key?.GetValue(null) is not string command || command.Trim().Length == 0)
            {
                return null;
            }

            var written = command.Trim();
            var program = written.StartsWith('"')
                ? written[1..(written.IndexOf('"', 1) is var end && end > 0 ? end : written.Length)]
                : written.Split(' ')[0];

            return File.Exists(program) ? program : null;
        }
        catch
        {
            // A key that cannot be read is a program that cannot be counted on.
            return null;
        }
    }

    /// <summary>
    /// Whether the program is on the search path, which is the other way a machine has one: named
    /// here rather than resolved, because starting it by name is what the search path is for.
    /// </summary>
    private static string? FromSearchPath()
    {
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var name in OnPath)
        {
            foreach (var folder in folders)
            {
                try
                {
                    if (File.Exists(Path.Combine(folder.Trim(), name)))
                    {
                        return name;
                    }
                }
                catch
                {
                    // A path on the search path that cannot be read is not one that holds it.
                }
            }
        }

        return null;
    }

    /// <summary>The folder an installation puts it in, for a machine where nothing registered it.</summary>
    private static string? FromInstallFolder()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };

        foreach (var root in roots)
        {
            if (root.Length == 0)
            {
                continue;
            }

            foreach (var relative in Installed)
            {
                var candidate = Path.Combine(root, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                // Under the user's own folder the same shape sits one level down.
                var underPrograms = Path.Combine(root, "Programs", relative);
                if (File.Exists(underPrograms))
                {
                    return underPrograms;
                }
            }
        }

        return null;
    }
}
