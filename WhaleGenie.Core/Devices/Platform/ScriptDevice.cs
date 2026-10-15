using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NuGet.Common;
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
    /// <summary>How long the program waits for a script to go away after it has been told to stop.</summary>
    private const int StopWaitMs = 2000;

    private readonly IProcessDevice _processes;
    private readonly string _packages;
    private readonly string _host;

    /// <summary>
    /// The device the program itself uses: packages beside the program, this machine's own programs,
    /// and this program's own file to run a compiled script with.
    /// </summary>
    public ScriptDevice(IProcessDevice processes)
        : this(processes, ScriptFolders.Packages, Environment.ProcessPath ?? string.Empty)
    {
    }

    /// <summary>The same with somewhere else to keep the packages and another file to run scripts
    /// with, which is what a check hands in.</summary>
    public ScriptDevice(IProcessDevice processes, string packages, string host)
    {
        _processes = processes;
        _packages = packages;
        _host = host;
    }

    /// <summary>Where the Python packages live: the folder every Python script imports from.</summary>
    private string PythonPackages => Path.Combine(_packages, "python");

    /// <summary>Where the NuGet packages live: the folder every C# script is compiled against.</summary>
    private string NuGetPackages => Path.Combine(_packages, "nuget");

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
            if (Directory.Exists(PythonPackages))
            {
                search.Add(PythonPackages);
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

            return Serve(
                new Serving(script.Command, string.Join(' ', arguments), script.Folder, environment,
                    script.TimeoutMs),
                host, onOutput, onError, token);
        }
        finally
        {
            Tidy(folder);
        }
    }

    /// <summary>
    /// Runs an AutoHotkey script with the macro in reach of it. Two files of this program's own go
    /// beside the script: the five names a script calls, and nothing else — the calling itself is
    /// done by a copy of this program, because AutoHotkey cannot write to the pipe the other
    /// languages are served over. A machine with no AutoHotkey, or with the first version of it,
    /// never starts anything, and says which of the two it was.
    /// </summary>
    public ScriptResult RunAutoHotkey(AutoHotkeyScript script, IScriptHost host,
        Action<string> onOutput, Action<string> onError, CancellationToken token)
    {
        var folder = Path.Combine(ScriptFolder(), $"script-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var running = Path.Combine(folder, "script.ahk");
        var prelude = Path.Combine(folder, "whalegenie.ahk");

        try
        {
            File.WriteAllText(running, AutoHotkeyBridge.Top(prelude) + script.Script, Mark);
            File.WriteAllText(prelude, Filled(prelude, running), Mark);

            var named = script.Command.Trim();
            var program = named.Length > 0 ? named : AutoHotkey.Program();
            if (program is null)
            {
                throw new DeviceActionException("Run.NoAutoHotkey", "AutoHotkey");
            }

            // Only the second version reads what this step writes. The first one reads a script of
            // its own shape, and a machine that has only that one has to hear so, rather than watch
            // a window full of its complaints about a script this program wrote.
            if (AutoHotkey.MajorVersion(program) == 1)
            {
                throw new DeviceActionException("Run.AhkNeedsV2", program);
            }

            var arguments = new List<string> { Quoted(running) };
            if (script.Arguments.Length > 0)
            {
                arguments.Add(script.Arguments);
            }

            return Serve(
                new Serving(program, string.Join(' ', arguments), script.Folder,
                    new Dictionary<string, string>(), script.TimeoutMs, Missing: "Run.NoAutoHotkey"),
                host, onOutput, onError, token);
        }
        finally
        {
            Tidy(folder);
        }
    }

    /// <summary>
    /// The bridge with everything only this run knows written into it: the program that is to be
    /// started as the bridge, the two files a call travels through, the script's own file and how
    /// many lines stand in front of it.
    /// </summary>
    private string Filled(string prelude, string running)
        => AutoHotkeyBridge.Prelude
            .Replace("__EXE__", _host, StringComparison.Ordinal)
            .Replace("__ASKED__", Path.ChangeExtension(prelude, ".call"), StringComparison.Ordinal)
            .Replace("__ANSWERED__", Path.ChangeExtension(prelude, ".answer"),
                StringComparison.Ordinal)
            .Replace("__SCRIPT__", running, StringComparison.Ordinal)
            .Replace("__OFFSET__", AutoHotkeyBridge.Ahead(prelude).ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

    /// <summary>
    /// Compiles a C# script and runs it in a process of this very program: the script is written
    /// out, compiled against this machine's own assemblies and the packages the macro downloaded,
    /// and then handed to a second copy of WhaleGenie that has no window of its own. A script that
    /// does not compile, or that has nothing to run, never gets that far, and what the compiler
    /// said about it is handed back as the step's error message.
    /// </summary>
    public ScriptResult RunCSharp(CSharpScript script, IScriptHost host, Action<string> onOutput,
        Action<string> onError, CancellationToken token)
    {
        var folder = Path.Combine(ScriptFolder(), $"script-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "script.cs");

        try
        {
            File.WriteAllText(source, script.Script, NoMark);
            var (built, failure) = Compile(source, folder, token);
            if (failure is not null)
            {
                return failure;
            }

            var arguments = new List<string> { ScriptHost.Flag, Quoted(built!) };
            if (script.Arguments.Length > 0)
            {
                arguments.Add(script.Arguments);
            }

            // A script is run by this program itself, which is what makes a stack overflow or an
            // exit of its own something the macro survives; the one thing the host is told is where
            // the way back into the macro is.
            return Serve(
                new Serving(Quoted(_host), string.Join(' ', arguments), script.Folder, new
                    Dictionary<string, string>(), script.TimeoutMs,
                    Missing: "Run.ProgramFailed", Lost: "Run.ScriptBridgeLost"),
                host, onOutput, onError, token);
        }
        finally
        {
            Tidy(folder);
        }
    }

    /// <summary>
    /// Compiles one script into a file a process can load. A script with nothing to run is told
    /// apart from one that does not compile: the first has no entry point, and the second is what
    /// the compiler complained about, in the compiler's own words and on the line they are about.
    /// </summary>
    private (string? Built, ScriptResult? Failure) Compile(string source, string folder,
        CancellationToken token)
    {
        var trees = new[]
        {
            // The encoding is written down with the source: without it the compiler refuses to
            // write down which line anything is on, and a mistake a script makes while it runs
            // would come back as a frame with no line in it.
            CSharpSyntaxTree.ParseText(CSharpServices.Source,
                new CSharpParseOptions(LanguageVersion.Latest), path: "injected.cs",
                encoding: NoMark, cancellationToken: token),
            CSharpSyntaxTree.ParseText(File.ReadAllText(source),
                new CSharpParseOptions(LanguageVersion.Latest), path: source,
                encoding: NoMark, cancellationToken: token),
        };

        var compilation = CSharpCompilation.Create("script", trees, References(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                optimizationLevel: OptimizationLevel.Release));

        // A program with no Main is a program the compiler complains about; that complaint is the
        // step's answer here rather than a compilation error, because it is the script's shape
        // rather than a mistake in it.
        var complaining = compilation.GetDiagnostics(token)
            .Where(complaint => complaint.Severity == DiagnosticSeverity.Error
                && complaint.Id != NoEntryPointId)
            .ToList();

        if (complaining.Count > 0)
        {
            return (null, Failed("Run.ScriptCompileFailed", Said(complaining, folder)));
        }

        if (compilation.GetEntryPoint(token) is null)
        {
            return (null, Failed("Run.NoEntryPoint", string.Empty));
        }

        var built = Path.Combine(folder, "script.dll");
        using (var image = File.Create(built))
        using (var symbols = File.Create(Path.Combine(folder, "script.pdb")))
        {
            // The lines are written down with the symbols, so a mistake a script makes while it
            // runs is reported against the line it was written on, the way a compiled program is.
            var wrote = compilation.Emit(image, symbols,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
                cancellationToken: token);

            if (!wrote.Success)
            {
                return (null, Failed("Run.ScriptCompileFailed", Said(
                    [.. wrote.Diagnostics.Where(complaint =>
                        complaint.Severity == DiagnosticSeverity.Error)], folder)));
            }
        }

        return (built, null);
    }

    /// <summary>
    /// What the compiler said, one complaint to a line, in the shape a compiler says it: the file
    /// and the place in it, then the error's own name and words. The copy of the script is in a
    /// temporary folder of this program's making, which is nobody's business, so it is left out of
    /// the path.
    /// </summary>
    private static string Said(IReadOnlyList<Diagnostic> complaining, string folder)
    {
        const int MostSaid = 10;
        var lines = complaining.Take(MostSaid)
            .Select(complaint => complaint.ToString()
                .Replace(folder + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal))
            .ToList();

        if (complaining.Count > MostSaid)
        {
            lines.Add($"(and {complaining.Count - MostSaid} more)");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>What one script is compiled against: this machine's own assemblies and the packages.</summary>
    private IEnumerable<MetadataReference> References()
        => [.. Machine.Value, .. NuGetStore.References(NuGetPackages)
            .Select(path => MetadataReference.CreateFromFile(path))];

    /// <summary>
    /// Everything the runtime this program is running on says is trustworthy. Reading it from the
    /// runtime rather than from a list written here is what keeps a script able to name what the
    /// program itself can name, on whatever version of .NET it was built for.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<MetadataReference>> Machine = new(() =>
    {
        var listed = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        return [.. listed
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))];
    });

    /// <summary>What the compiler calls a program whose entry point is not there.</summary>
    private const string NoEntryPointId = "CS5001";

    /// <summary>What one script came to when it never got as far as running.</summary>
    private static ScriptResult Failed(string key, string said)
        => new(1, string.Empty, said, Value.Null, key);

    /// <summary>How one run of a script is started, and what to say when it cannot be.</summary>
    /// <param name="Command">The program to run, with any flags of its own in front.</param>
    /// <param name="Arguments">What it is run with, after the program's own flags.</param>
    /// <param name="Folder">Where it runs, or empty for wherever the macro is.</param>
    /// <param name="Environment">What the child is told beyond what it inherits.</param>
    /// <param name="TimeoutMs">How long it may run, or zero for as long as it takes.</param>
    /// <param name="Missing">What it means when that program cannot be started at all.</param>
    /// <param name="Lost">What it means when it runs but never reaches the macro, if anything.</param>
    private sealed record Serving(string Command, string Arguments, string Folder,
        IDictionary<string, string> Environment, int TimeoutMs, string Missing = "Run.NoPython",
        string Lost = "");

    /// <summary>
    /// Runs a program over one of the little programs above and serves it until it stops: the
    /// requests it makes are answered here, on the thread that is walking the macro, and what it
    /// prints is handed over line by line as it arrives. The interpreter is named the way a macro
    /// writes a command line, so a virtual environment's own Python — a path with spaces in it —
    /// and the flags it needs in front both work.
    /// </summary>
    private ScriptResult Serve(Serving how, IScriptHost host, Action<string> onOutput,
        Action<string> onError, CancellationToken token)
    {
        var (program, flags) = CommandLine.Split(how.Command);
        var arguments = how.Arguments;
        if (flags.Length > 0)
        {
            arguments = $"{flags} {arguments}";
        }

        var name = $"whalegenie-{Guid.NewGuid():N}";
        how.Environment[ScriptHost.PipeVariable] = name;

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

        if (how.Folder.Length > 0)
        {
            info.WorkingDirectory = how.Folder;
        }

        foreach (var (key, value) in how.Environment)
        {
            info.Environment[key] = value;
        }

        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var asked = new BlockingCollection<JsonObject>();
        var reached = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new ConcurrentQueue<string>();
        var error = new ConcurrentQueue<string>();
        var said = new List<string>();
        var complained = new List<string>();

        using var process = Start(info, program, how.Missing);
        var reading = new[]
        {
            Task.Run(() => Pump(process.StandardOutput, output)),
            Task.Run(() => Pump(process.StandardError, error)),
            Task.Run(() => Listen(pipe, asked, reached)),
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

                if (how.TimeoutMs > 0
                    && Stopwatch.GetElapsedTime(started).TotalMilliseconds > how.TimeoutMs)
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

            // A script that ran but never reached the macro is a run that could not be served at
            // all, which is a different thing from a script that failed and said why.
            if (how.Lost.Length > 0 && !reached.Task.IsCompletedSuccessfully)
            {
                throw new DeviceActionException(how.Lost,
                    complained.Count > 0 ? complained[^1] : program);
            }
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
    /// Starts the program a script runs in, saying in the engine's own words what it means when
    /// Windows reports that it will not start: for Python that this machine has none, and for this
    /// program's own script host that it would not run.
    /// </summary>
    private static Process Start(ProcessStartInfo info, string program, string missing)
    {
        try
        {
            return Process.Start(info)
                ?? throw new DeviceActionException(missing, program);
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception
            or InvalidOperationException or PlatformNotSupportedException)
        {
            throw new DeviceActionException(missing, program);
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
    private static void Listen(NamedPipeServerStream pipe, BlockingCollection<JsonObject> into,
        TaskCompletionSource<bool> reached)
    {
        try
        {
            pipe.WaitForConnection();
            reached.TrySetResult(true);
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

        Directory.CreateDirectory(PythonPackages);
        var arguments = new List<string>
        {
            "-m", "pip", "install", "--disable-pip-version-check", "--upgrade",
            "--target", Quoted(PythonPackages),
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
    /// Reads which NuGet packages a macro names and brings the ones that are not there yet down
    /// from the official feed, dependencies and all, so that a C# script can be compiled against
    /// them. What went in is handed over as a list, and everything that went wrong — a package that
    /// is not on the feed, a feed that cannot be reached, a run that went on too long — is handed
    /// back as the step's own error message rather than thrown, because the three results of the
    /// step are read after its failure rule has let the macro carry on.
    /// </summary>
    public ScriptResult InstallNuGet(NuGetRequest request, Action<string> onOutput,
        Action<string> onError, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var store = new NuGetStore(NuGetPackages);
        using var limit = request.TimeoutMs > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(token)
            : null;
        limit?.CancelAfter(request.TimeoutMs);

        try
        {
            var wanted = NuGetStore.Written(request.Packages);
            var missing = wanted.Where(package => !store.There(package)).ToList();

            // Nothing to download is still a run worth making: the list of what a C# script is
            // compiled against is written from what is on disk, which is what puts a package back
            // into that list after the folders were moved about by hand.
            var added = Task.Run(() => store.Fetch(missing, NullLogger.Instance, onOutput,
                limit?.Token ?? token)).GetAwaiter().GetResult();

            return new ScriptResult(0, string.Empty, string.Empty,
                Value.FromList([.. added.Select(Value.FromText)]));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new DeviceActionException("Run.CommandTimeout", Feed);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return new ScriptResult(1, string.Empty,
                failure is DeviceActionException known ? known.Detail : failure.Message,
                Value.FromList([]), "Run.NuGetFailed");
        }
    }

    /// <summary>Where NuGet packages come from, for the line a run that went on too long writes.</summary>
    private const string Feed = "nuget.org";

    /// <summary>
    /// Whether a package is already in the folder pip installs into. The folder says so by itself:
    /// pip writes a <c>.dist-info</c> folder beside the code it unpacks, named after the package and
    /// its version, and reading that costs nothing where asking pip would mean starting an
    /// interpreter and going to the index this check exists to avoid.
    /// </summary>
    private bool PipInstalled(string package)
    {
        if (!Directory.Exists(PythonPackages))
        {
            return false;
        }

        var (name, version) = Wanted(package);
        var wanted = Normalised(name);

        foreach (var folder in Directory.EnumerateDirectories(PythonPackages, "*.dist-info"))
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

    /// <summary>
    /// UTF-8 with a mark, which is what AutoHotkey reads a script of its own language as: without
    /// one it reads the system code page, and a script with Chinese text in it comes out as rubbish.
    /// </summary>
    private static readonly UTF8Encoding Mark = new(encoderShouldEmitUTF8Identifier: true);
}
