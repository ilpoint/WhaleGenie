using System;
using System.Diagnostics;
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

    /// <summary>
    /// The program this machine would run a script with, or null when it has none, for anyone
    /// outside the engine: a check that runs a real script has to know whether there is anything to
    /// run it with before it can say what this machine left untested.
    /// </summary>
    public static string? Found() => Program();

    private static string? Locate()
        => FromRegistry() ?? FromSearchPath() ?? FromInstallFolder();

    /// <summary>
    /// The first number of the version of the program a command names, which is what tells the two
    /// AutoHotkeys apart — they read different scripts and only one of them is what a script step is
    /// written for. Nothing comes back when nothing can say: a name resolved through the search
    /// path, a program with no version written into it, or a file that is not there.
    /// </summary>
    public static int? MajorVersion(string program)
    {
        var path = Full(program);
        if (path is null)
        {
            return null;
        }

        try
        {
            var written = FileVersionInfo.GetVersionInfo(path);
            return written.FileMajorPart > 0 ? written.FileMajorPart : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException
            or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The file a command names, whether it was written as a path or left to the search path. A
    /// name with nothing on the search path stands for no file, which is not the same question as
    /// whether the program is there at all: starting it by name is left to Windows.
    /// </summary>
    private static string? Full(string program)
    {
        var named = program.Trim().Trim('"');
        if (named.Length == 0)
        {
            return null;
        }

        if (Path.IsPathRooted(named))
        {
            return File.Exists(named) ? named : null;
        }

        return OnSearchPath(named);
    }

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
        foreach (var name in OnPath)
        {
            if (OnSearchPath(name) is not null)
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>The file a name on the search path stands for, or null when it stands for none.</summary>
    private static string? OnSearchPath(string name)
    {
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var folder in folders)
        {
            try
            {
                var candidate = Path.Combine(folder.Trim(), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // A path on the search path that cannot be read is not one that holds it.
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
