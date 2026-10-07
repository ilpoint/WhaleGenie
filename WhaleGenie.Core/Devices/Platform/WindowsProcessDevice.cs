using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace WhaleGenie.Core.Devices.Platform;

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

        var environment = request.Environment is { Count: > 0 } ? request.Environment : null;
        if (request.RunAsAdmin && environment is not null)
        {
            // The approval prompt only exists on the route through the shell, and that route builds
            // the environment itself. Saying so beats starting the program with settings the macro
            // did not ask for.
            throw new DeviceActionException("Run.ElevationNoEnvironment", request.FileName);
        }

        // A program given its own environment variables has to be started directly, because only
        // that route can carry them; everything else keeps going through the shell, which is what
        // lets a document or a shortcut be started the way double-clicking it would.
        var throughShell = environment is null;
        var info = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            UseShellExecute = throughShell,
        };

        if (throughShell)
        {
            info.WindowStyle = request.Hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal;
        }
        else
        {
            info.CreateNoWindow = request.Hidden;
        }

        if (request.RunAsAdmin)
        {
            // Only the shell can raise the approval prompt, so this route has to go through it.
            info.Verb = "runas";
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            info.WorkingDirectory = request.WorkingDirectory;
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                info.Environment[name] = value;
            }
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

    /// <summary>UTF-8 that leaves the byte-order mark off, for text handed to another program.</summary>
    private static readonly Encoding NoMark = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The code page a program's output is read in. A macro that names one gets it; one that says
    /// nothing is read in this machine's own, which is what cmd.exe, Windows PowerShell and Python
    /// all write in when their output goes into a pipe. Reading those as UTF-8 is what used to turn
    /// every Chinese word they printed into question marks.
    /// </summary>
    private static Encoding OutputEncoding(CommandRequest request)
        => TextEncoding.Resolve(string.IsNullOrWhiteSpace(request.OutputEncoding)
            ? TextEncoding.System
            : request.OutputEncoding);

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

    public CommandResult Run(CommandRequest request)
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
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = OutputEncoding(request),
            StandardErrorEncoding = OutputEncoding(request),
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            info.WorkingDirectory = request.WorkingDirectory;
        }

        if (request.Environment is { Count: > 0 } environment)
        {
            foreach (var (name, value) in environment)
            {
                info.Environment[name] = value;
            }
        }

        if (request.StandardInput is not null)
        {
            info.RedirectStandardInput = true;

            // UTF-8 without a byte-order mark: the mark-carrying encoding that .NET starts from
            // puts three extra bytes in front of the first line, and tools that read a stream by
            // its first bytes (findstr is one) then see a file that does not start with what the
            // macro wrote. Python and Node read their own source off standard input as UTF-8 too.
            info.StandardInputEncoding = NoMark;
        }

        try
        {
            using var process = Process.Start(info)
                ?? throw new DeviceActionException("Run.ProgramFailed", request.FileName);

            var feeding = Feed(process, request.StandardInput);

            // Nobody is listening line by line, so the whole of what the program says can simply
            // be gathered at the end.
            if (request.OnOutput is null && request.OnError is null)
            {
                return Collect(process, request, feeding);
            }

            return Watch(process, request, feeding);
        }
        catch (Exception error) when (Recoverable(error))
        {
            throw new DeviceActionException("Run.ProgramFailed",
                $"{request.FileName}: {error.Message}");
        }
    }

    /// <summary>Runs a program to the end and gathers everything it printed.</summary>
    private static CommandResult Collect(Process process, CommandRequest request, Task? feeding)
    {
        // Both pipes are drained at once, so a program that talks a lot on one of them
        // cannot fill it up and stop while this side waits on the other.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(Math.Max(0, request.TimeoutMs)))
        {
            Kill(process);
            throw new DeviceActionException("Run.CommandTimeout", request.FileName);
        }

        feeding?.GetAwaiter().GetResult();

        return new CommandResult(
            process.ExitCode,
            output.GetAwaiter().GetResult(),
            error.GetAwaiter().GetResult());
    }

    /// <summary>
    /// Runs a program somebody is watching: every line is handed over as it is printed, and the
    /// whole of it is kept for the result. The lines are picked up on the thread that asked for the
    /// run rather than on the reading threads, so a caller that writes them into a log gets them in
    /// order, on a thread it already knows, and without anything else to lock.
    /// </summary>
    private static CommandResult Watch(Process process, CommandRequest request, Task? feeding)
    {
        var printed = new ConcurrentQueue<string>();
        var complained = new ConcurrentQueue<string>();

        process.OutputDataReceived += (_, line) => Take(printed, line.Data);
        process.ErrorDataReceived += (_, line) => Take(complained, line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var lines = new List<string>();
        var errors = new List<string>();
        var started = Stopwatch.GetTimestamp();
        var timeout = Math.Max(0, request.TimeoutMs);

        while (!process.WaitForExit(PollMs))
        {
            Drain(printed, lines, request.OnOutput);
            Drain(complained, errors, request.OnError);

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeout)
            {
                Kill(process);
                throw new DeviceActionException("Run.CommandTimeout", request.FileName);
            }
        }

        // The program has gone, but the two readers are still handing over what was left in the
        // pipes, and waiting for the process again is what waits for them to finish.
        process.WaitForExit();
        Drain(printed, lines, request.OnOutput);
        Drain(complained, errors, request.OnError);
        feeding?.GetAwaiter().GetResult();

        return new CommandResult(
            process.ExitCode,
            string.Join(Environment.NewLine, lines),
            string.Join(Environment.NewLine, errors));
    }

    /// <summary>How often a watched run looks at what has arrived so far.</summary>
    private const int PollMs = 20;

    /// <summary>Keeps a line the readers handed over; a null line is the end of that stream.</summary>
    private static void Take(ConcurrentQueue<string> queue, string? line)
    {
        if (line is not null)
        {
            queue.Enqueue(line);
        }
    }

    /// <summary>Hands on everything that has arrived, in the order it was printed.</summary>
    private static void Drain(ConcurrentQueue<string> queue, List<string> gathered,
        Action<string>? listener)
    {
        while (queue.TryDequeue(out var line))
        {
            listener?.Invoke(line);
            gathered.Add(line);
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

    /// <summary>
    /// Hands the text to the program's standard input and closes it, which is how a program finds
    /// out there is nothing more to read. Written on a thread of its own: a program that has not
    /// started reading yet would otherwise leave this side blocked with a full pipe while the
    /// program itself is blocked with a full pipe of its own.
    /// </summary>
    private static Task? Feed(Process process, string? text)
    {
        if (text is null)
        {
            return null;
        }

        return Task.Run(() =>
        {
            try
            {
                process.StandardInput.Write(text);
                process.StandardInput.Close();
            }
            catch (Exception error) when (error is IOException or InvalidOperationException
                or ObjectDisposedException)
            {
                // The program stopped reading and went away. What the step wants to know is what it
                // printed and what it returned, not that nobody was left to take the last line.
            }
        });
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
