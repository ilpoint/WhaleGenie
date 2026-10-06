using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Other programs, through the .NET process API. Every call names what went wrong in a way
/// the interface can translate, so a macro reports "there is no such program" rather than
/// spilling a Windows error code.
/// </summary>
public sealed class WindowsProcessDevice : IProcessDevice, IDisposable
{
    /// <summary>How long a politely asked process gets to close before it is killed.</summary>
    private const int PoliteWaitMs = 2000;

    /// <summary>
    /// Programs this device started, kept by id. Windows only hands out a program's exit code
    /// through the handle that was open while it ran, so letting go of it here would mean
    /// "wait for exit" could never say what the program returned.
    /// </summary>
    private readonly ConcurrentDictionary<int, Process> _started = new();

    public int Start(StartRequest request)
    {
        Require();

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new DeviceActionException("Run.MissingProgram");
        }

        var info = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            UseShellExecute = true,
            WindowStyle = request.Hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
        };

        if (request.RunAsAdmin)
        {
            // Only the shell can raise the approval prompt, so this route has to go through it.
            info.Verb = "runas";
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            info.WorkingDirectory = request.WorkingDirectory;
        }

        try
        {
            var process = Process.Start(info)
                ?? throw new DeviceActionException("Run.ProgramFailed", request.FileName);

            _started[process.Id] = process;
            return process.Id;
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == Refused)
        {
            throw new DeviceActionException("Run.ElevationRefused", request.FileName);
        }
        catch (Exception error) when (Recoverable(error))
        {
            throw new DeviceActionException("Run.ProgramFailed",
                $"{request.FileName}: {error.Message}");
        }
    }

    /// <summary>What Windows calls it when the person says no to the approval prompt.</summary>
    private const int Refused = 1223;

    public IReadOnlyList<int> Find(string name)
    {
        Require();

        var wanted = Normalise(name);
        if (wanted.Length == 0)
        {
            return [];
        }

        return [.. Snapshot().Where(row => row.Name == wanted).Select(row => row.Id)];
    }

    public IReadOnlyList<string> List()
        => [.. Snapshot()
            .Select(row => row.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

    public bool HasExited(int id)
    {
        Require();

        try
        {
            if (_started.TryGetValue(id, out var started))
            {
                return started.HasExited;
            }

            using var process = Process.GetProcessById(id);
            return process.HasExited;
        }
        catch (Exception error) when (Recoverable(error) || error is ArgumentException)
        {
            // A process that is not there has, in every sense that matters, finished.
            return true;
        }
    }

    public int? ExitCode(int id)
    {
        Require();

        try
        {
            if (_started.TryGetValue(id, out var started))
            {
                return started.HasExited ? started.ExitCode : null;
            }

            using var process = Process.GetProcessById(id);
            return process.HasExited ? process.ExitCode : null;
        }
        catch (Exception error) when (Recoverable(error) || error is ArgumentException)
        {
            return null;
        }
    }

    public int StopByName(string name, bool force)
    {
        Require();

        var wanted = Normalise(name);
        if (wanted.Length == 0)
        {
            return 0;
        }

        var stopped = 0;
        foreach (var row in Snapshot())
        {
            if (row.Name == wanted && Stop(row.Id, force))
            {
                stopped++;
            }
        }

        return stopped;
    }

    public bool StopById(int id, bool force)
    {
        Require();
        return Stop(id, force);
    }

    public CommandResult Run(string fileName, string arguments, string workingDirectory, int timeoutMs)
    {
        Require();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DeviceActionException("Run.MissingProgram");
        }

        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            info.WorkingDirectory = workingDirectory;
        }

        try
        {
            using var process = Process.Start(info)
                ?? throw new DeviceActionException("Run.ProgramFailed", fileName);

            // Both pipes are drained at once, so a program that talks a lot on one of them
            // cannot fill it up and stop while this side waits on the other.
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(Math.Max(0, timeoutMs)))
            {
                Kill(process);
                throw new DeviceActionException("Run.CommandTimeout", fileName);
            }

            return new CommandResult(
                process.ExitCode,
                output.GetAwaiter().GetResult(),
                error.GetAwaiter().GetResult());
        }
        catch (Exception error) when (Recoverable(error))
        {
            throw new DeviceActionException("Run.ProgramFailed", $"{fileName}: {error.Message}");
        }
    }

    /// <summary>Lets go of the programs this device started and was still holding on to.</summary>
    public void Dispose()
    {
        foreach (var process in _started.Values)
        {
            process.Dispose();
        }

        _started.Clear();
    }

    /// <summary>Stops a process, asking it to close first unless told to be firm.</summary>
    private static bool Stop(int id, bool force)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            if (process.HasExited)
            {
                return false;
            }

            if (!force && process.CloseMainWindow() && process.WaitForExit(PoliteWaitMs))
            {
                return true;
            }

            Kill(process);
            return true;
        }
        catch (Exception error) when (Recoverable(error) || error is ArgumentException)
        {
            return false;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception error) when (Recoverable(error) || error is ArgumentException)
        {
            // It went away on its own between the check and the call, which is fine.
        }
    }

    /// <summary>Every running process, by name and id, letting go of each handle as it goes.</summary>
    private static List<(int Id, string Name)> Snapshot()
    {
        var rows = new List<(int, string)>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                rows.Add((process.Id, process.ProcessName));
            }
            catch (Exception error) when (Recoverable(error) || error is ArgumentException)
            {
                // A protected system process will not say what it is called; skip it.
            }
            finally
            {
                process.Dispose();
            }
        }

        return rows;
    }

    /// <summary>Lower-cased, without the <c>.exe</c>, so "Notepad" and "notepad.exe" agree.</summary>
    private static string Normalise(string name)
    {
        var text = (name ?? string.Empty).Trim().ToLowerInvariant();
        return text.EndsWith(".exe", StringComparison.Ordinal) ? text[..^4] : text;
    }

    private static bool Recoverable(Exception error)
        => error is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException;

    private static void Require()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("other programs");
        }
    }
}
