using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The scripts this program runs itself, and the packages they ask for. A script of one of these
/// languages runs in a process of its own with a way back into the macro over a local pipe, so it
/// can read and write the macro's values and run its steps while it runs — and whatever it does to
/// itself, a crash or an exit of its own or a loop that never ends, touches nothing of the program
/// that started it.
/// </summary>
/// <remarks>
/// The pipe is served on the thread that asked for the run, which is the thread walking the macro:
/// a script that asks for a step to be run gets it run there and then, the way a block inside a
/// macro runs the steps inside it. The three streams the child has — stdout, stderr and the pipe —
/// are each read on a thread of their own and handed over in the order they arrived.
/// </remarks>
public sealed class ScriptDevice : IScriptDevice
{
    /// <summary>The environment variable the child is told the pipe by.</summary>
    private const string PipeVariable = "WHALEGENIE_PIPE";

    /// <summary>How long the program waits for a script to go away after it has been told to stop.</summary>
    private const int StopWaitMs = 2000;

    private readonly IProcessDevice _processes;
    private readonly string _pythonPackages;

    /// <summary>The device the program itself uses: packages beside the program, this machine's own programs.</summary>
    public ScriptDevice(IProcessDevice processes)
        : this(processes, ScriptFolders.PythonPackages)
    {
    }

    /// <summary>The same with somewhere else to keep the packages, which is what a check hands in.</summary>
    public ScriptDevice(IProcessDevice processes, string pythonPackages)
    {
        _processes = processes;
        _pythonPackages = pythonPackages;
    }

    // ------------------------------------------------------------------ python

    public ScriptResult RunPython(PythonScript script, IScriptHost host, Action<string> onOutput,
        Action<string> onError, CancellationToken token)
    {
        var folder = Path.Combine(ScriptFolder(), $"script-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var running = Path.Combine(folder, "script.py");

        try
        {
            File.WriteAllText(running, script.Script, NoMark);
            File.WriteAllText(Path.Combine(folder, "whalegenie.py"), PythonBridge.Host, NoMark);
            var boot = Path.Combine(folder, "boot.py");
            File.WriteAllText(boot, PythonBridge.Boot, NoMark);

            // The packages installed for scripts of this program go on the search path whether the
            // script asks for them or not: they were installed so that scripts could import them,
            // and a script that says "import numpy" should not have to be told where it lives.
            // Whatever the machine already has on that path stays on it, after these two.
            var search = new List<string> { folder };
            if (Directory.Exists(_pythonPackages))
            {
                search.Add(_pythonPackages);
            }

            if (Environment.GetEnvironmentVariable("PYTHONPATH") is { Length: > 0 } already)
            {
                search.Add(already);
            }

            var environment = new Dictionary<string, string>
            {
                ["PYTHONPATH"] = string.Join(Path.PathSeparator, search),
                ["PYTHONIOENCODING"] = "utf-8",
                ["PYTHONUTF8"] = "1",
            };

            var arguments = new List<string> { "-X", "utf8", Quoted(boot), Quoted(running) };
            if (script.Arguments.Length > 0)
            {
                arguments.Add(script.Arguments);
            }

            return Serve(script.Command, string.Join(' ', arguments), script.Folder, environment,
                host, script.TimeoutMs, onOutput, onError, token);
        }
        finally
        {
            Tidy(folder);
        }
    }

    /// <summary>
    /// Runs a program over one of the little programs above and serves it until it stops: the
    /// requests it makes are answered here, on the thread that is walking the macro, and what it
    /// prints is handed over line by line as it arrives. The interpreter is named the way a macro
    /// writes a command line, so a virtual environment's own Python — a path with spaces in it —
    /// and the flags it needs in front both work.
    /// </summary>
    private ScriptResult Serve(string command, string arguments, string folder,
        IDictionary<string, string> environment, IScriptHost host, int timeoutMs,
        Action<string> onOutput, Action<string> onError, CancellationToken token)
    {
        var (program, flags) = CommandLine.Split(command);
        if (flags.Length > 0)
        {
            arguments = $"{flags} {arguments}";
        }

        var name = $"whalegenie-{Guid.NewGuid():N}";
        environment[PipeVariable] = name;

        var info = new ProcessStartInfo
        {
            FileName = program,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = NoMark,
            StandardErrorEncoding = NoMark,
        };

        if (folder.Length > 0)
        {
            info.WorkingDirectory = folder;
        }

        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }

        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var asked = new BlockingCollection<JsonObject>();
        var output = new ConcurrentQueue<string>();
        var error = new ConcurrentQueue<string>();
        var said = new List<string>();
        var complained = new List<string>();

        using var process = Start(info, program);
        var reading = new[]
        {
            Task.Run(() => Pump(process.StandardOutput, output)),
            Task.Run(() => Pump(process.StandardError, error)),
            Task.Run(() => Listen(pipe, asked)),
        };

        var started = Stopwatch.GetTimestamp();
        var handed = Value.Null;
        var exitCode = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                if (asked.TryTake(out var request, 25))
                {
                    if (request["call"]?.GetValue<string>() is "result")
                    {
                        // What the script wants to hand back is kept here rather than asked of the
                        // macro: it is the step's result, and the step is not over yet.
                        handed = JsonValues.FromJson(request["value"]);
                        Answer(pipe, new JsonObject { ["ok"] = true });
                    }
                    else
                    {
                        Answer(pipe, Reply(host, request));
                    }

                    continue;
                }

                Drain(output, said, onOutput);
                Drain(error, complained, onError);

                if (process.HasExited && asked.Count == 0)
                {
                    break;
                }

                if (timeoutMs > 0
                    && Stopwatch.GetElapsedTime(started).TotalMilliseconds > timeoutMs)
                {
                    throw new DeviceActionException("Run.CommandTimeout", program);
                }
            }

            process.WaitForExit();
            exitCode = process.ExitCode;

            // Everything the script said is read before the step is over: a line that arrives just
            // as it exits is still something it said. A stream a program of its own is still
            // holding open is left alone rather than waited on for ever.
            Task.WaitAll([reading[0], reading[1]], StopWaitMs);
        }
        finally
        {
            // Whichever way this ended — the script finished, it ran out of time, the macro was
            // stopped — the script does not go on running: one left behind would keep touching the
            // machine after the macro it belongs to had been given up on.
            Stop(process);
            pipe.Dispose();
            Drain(output, said, onOutput);
            Drain(error, complained, onError);
        }

        return new ScriptResult(exitCode, string.Join(Environment.NewLine, said),
            string.Join(Environment.NewLine, complained), handed);
    }

    /// <summary>
    /// Starts the interpreter, saying in the engine's own words that this machine has none when
    /// that is what Windows reports. Python is one of the two things a script step needs and
    /// Windows does not bring.
    /// </summary>
    private static Process Start(ProcessStartInfo info, string program)
    {
        try
        {
            return Process.Start(info)
                ?? throw new DeviceActionException("Run.NoPython", program);
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception
            or InvalidOperationException or PlatformNotSupportedException)
        {
            throw new DeviceActionException("Run.NoPython", program);
        }
    }

    /// <summary>What one request from the script is answered with.</summary>
    private static JsonObject Reply(IScriptHost host, JsonObject request) =>
        request["call"]?.GetValue<string>() switch
        {
            "get" => new JsonObject
            {
                ["ok"] = true,
                ["value"] = host.Read(Text(request, "name")) is { } value
                    ? JsonValues.Node(value)
                    : null,
            },
            "set" => Written(host, request),
            "step" => new JsonObject
            {
                ["ok"] = true,
                ["value"] = host.RunStep(Text(request, "id")),
            },
            "print" => Printed(host, request),
            var call => new JsonObject { ["ok"] = false, ["error"] = call },
        };

    private static JsonObject Written(IScriptHost host, JsonObject request)
    {
        host.Write(Text(request, "name"), JsonValues.FromJson(request["value"]));
        return new JsonObject { ["ok"] = true };
    }

    private static JsonObject Printed(IScriptHost host, JsonObject request)
    {
        host.Print(Text(request, "text"));
        return new JsonObject { ["ok"] = true };
    }

    private static string Text(JsonObject request, string field)
        => request[field]?.GetValue<string>() ?? string.Empty;

    /// <summary>
    /// Hands one answer back. A pipe whose other end has gone is not a failure of its own: the
    /// script that was listening to it has already ended, and what became of it is its exit code.
    /// </summary>
    private static void Answer(NamedPipeServerStream pipe, JsonObject reply)
    {
        try
        {
            var written = Encoding.UTF8.GetBytes(reply.ToJsonString() + "\n");
            pipe.Write(written, 0, written.Length);
            pipe.Flush();
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException
            or InvalidOperationException)
        {
            // Nothing to say to nobody.
        }
    }

    /// <summary>Reads a stream a line at a time and puts what it finds in the queue.</summary>
    private static void Pump(StreamReader reader, ConcurrentQueue<string> into)
    {
        try
        {
            while (reader.ReadLine() is { } line)
            {
                into.Enqueue(line);
            }
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException)
        {
            // A stream that closed under the reader is the script having ended.
        }
    }

    /// <summary>Reads the requests a script makes until the pipe closes.</summary>
    private static void Listen(NamedPipeServerStream pipe, BlockingCollection<JsonObject> into)
    {
        try
        {
            pipe.WaitForConnection();
            using var reader = new StreamReader(pipe, NoMark, false, 4096, leaveOpen: true);
            while (reader.ReadLine() is { } line)
            {
                if (JsonNode.Parse(line) is JsonObject request)
                {
                    into.Add(request);
                }
            }
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException
            or InvalidOperationException or System.Text.Json.JsonException)
        {
            // The script closed the pipe, or never opened one: either way nothing more is coming.
        }
        finally
        {
            into.CompleteAdding();
        }
    }

    /// <summary>
    /// Hands over everything a stream has said since the last look, keeping a copy of it: the lines
    /// go into the run log as they arrive, and the whole of what was said is what the step hands
    /// back afterwards, which is what a script that failed is read through.
    /// </summary>
    private static void Drain(ConcurrentQueue<string> lines, List<string> kept, Action<string> said)
    {
        while (lines.TryDequeue(out var line))
        {
            kept.Add(line);
            said(line);
        }
    }

    /// <summary>Stops a script and everything it started.</summary>
    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(StopWaitMs);
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException
            or AggregateException)
        {
            // A program that has just gone is exactly what this was for.
        }
    }

    // --------------------------------------------------------------- packages

    public ScriptResult InstallPip(PipRequest request, Action<string> onOutput, Action<string> onError,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var missing = request.Packages.Where(package => !PipInstalled(package)).ToList();
        if (missing.Count == 0)
        {
            return new ScriptResult(0, string.Empty, string.Empty, Value.FromList([]));
        }

        Directory.CreateDirectory(_pythonPackages);
        var arguments = new List<string>
        {
            "-m", "pip", "install", "--disable-pip-version-check", "--upgrade",
            "--target", Quoted(_pythonPackages),
        };
        arguments.AddRange(missing.Select(Quoted));

        CommandResult ran;
        try
        {
            var (program, flags) = CommandLine.Split(request.Command);
            var written = flags.Length == 0
                ? string.Join(' ', arguments)
                : $"{flags} {string.Join(' ', arguments)}";

            ran = _processes.Run(new CommandRequest(program, written,
                TimeoutMs: request.TimeoutMs <= 0 ? int.MaxValue : request.TimeoutMs,
                OnOutput: onOutput, OnError: onError,
                OutputEncoding: TextEncoding.System));
        }
        catch (DeviceActionException failure) when (failure.Key == "Run.ProgramFailed")
        {
            // pip is run through the interpreter named above, so a program that cannot be started
            // at all means this machine has no Python rather than a pip that refused.
            throw new DeviceActionException("Run.NoPython", request.Command);
        }

        var added = missing.Where(PipInstalled).ToList();
        return new ScriptResult(ran.ExitCode, ran.StandardOutput, ran.StandardError,
            Value.FromList([.. added.Select(Value.FromText)]));
    }

    /// <summary>
    /// Whether a package is already in the folder pip installs into. The folder says so by itself:
    /// pip writes a <c>.dist-info</c> folder beside the code it unpacks, named after the package and
    /// its version, and reading that costs nothing where asking pip would mean starting an
    /// interpreter and going to the index this check exists to avoid.
    /// </summary>
    private bool PipInstalled(string package)
    {
        if (!Directory.Exists(_pythonPackages))
        {
            return false;
        }

        var (name, version) = Wanted(package);
        var wanted = Normalised(name);

        foreach (var folder in Directory.EnumerateDirectories(_pythonPackages, "*.dist-info"))
        {
            var stem = Path.GetFileNameWithoutExtension(folder);
            var dash = stem.LastIndexOf('-');
            if (dash <= 0 || Normalised(stem[..dash]) != wanted)
            {
                continue;
            }

            if (version.Length == 0 || stem[(dash + 1)..] == version)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A package as it is written and, where it says so, the version it has to be. Only the one
    /// spelling pip itself calls exact — <c>name==version</c> — is read here; anything else is left
    /// to pip, which understands all of it, at the price of installing a package that was in fact
    /// already there.
    /// </summary>
    private static (string Name, string Version) Wanted(string package)
    {
        var written = package.Trim();
        var at = written.IndexOf("==", StringComparison.Ordinal);
        return at > 0 ? (written[..at], written[(at + 2)..]) : (written, string.Empty);
    }

    /// <summary>How pip spells a package name in a folder, without the differences that do not count.</summary>
    private static string Normalised(string name)
        => name.Trim().Replace('-', '_').Replace('.', '_').ToLowerInvariant();

    // ----------------------------------------------------------------- shared

    /// <summary>Where the copies of a script are written while it runs.</summary>
    private static string ScriptFolder() => Path.Combine(Path.GetTempPath(), "WhaleGenie");

    private static string Quoted(string text) => $"\"{text}\"";

    /// <summary>Removes the copies of a script once nothing is reading them.</summary>
    private static void Tidy(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException
            or DirectoryNotFoundException)
        {
            // A copy that cannot be removed is not worth failing a step over: it is in the system's
            // own temporary folder, which Windows clears out by itself.
        }
    }

    /// <summary>UTF-8 that leaves the byte-order mark off, which is how a script is written.</summary>
    private static readonly UTF8Encoding NoMark = new(encoderShouldEmitUTF8Identifier: false);
}
