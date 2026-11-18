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


namespace WhaleGenie.Platform.Windows;

// How a script is served: the command, the folder and the host it may call back into.
public sealed partial class ScriptDevice : IScriptDevice
{

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
        IDictionary<string, string> Environment, int TimeoutMs, string Missing = RunKeys.NoPython,
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
        how.Environment[ScriptProtocol.PipeVariable] = name;

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

        // Start waiting before the child so a short-lived script cannot connect and exit before
        // the listener has had a chance to run. The task is a normal pool task, so it cannot keep
        // the process alive if the pipe is disposed while it waits.
        // The listener and the readers deliberately outlive a cancelled step: the pipe and the
        // process are what end them, and a token that fired while they were queued would leave the
        // script talking to nobody.
        _ = Task.Run(() => Listen(pipe, asked, reached), CancellationToken.None);
        using var process = Start(info, program, how.Missing);
        var reading = new[]
        {
            Task.Run(() => Pump(process.StandardOutput, output), CancellationToken.None),
            Task.Run(() => Pump(process.StandardError, error), CancellationToken.None),
        };

        var started = Stopwatch.GetTimestamp();
        var handed = Value.Null;
        var exitCode = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                if (asked.TryTake(out var request, 25, token))
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
                    throw new DeviceActionException(RunKeys.CommandTimeout, program);
                }
            }

            process.WaitForExit();
            exitCode = process.ExitCode;

            // Everything the script said is read before the step is over: a line that arrives just
            // as it exits is still something it said. A stream a program of its own is still
            // holding open is left alone rather than waited on for ever.
            // Task.WaitAll and Task.Wait take a timeout but no token, so there is nothing to hand
            // the step's cancellation to here; the wait itself is what the clean-up is bounded by.
#pragma warning disable CA2016
            Task.WaitAll([reading[0], reading[1]], StopWaitMs);

            // A script that ends immediately can leave the process before the pipe listener has
            // marked its connection. Give that listener the same short window the output streams
            // get; otherwise a perfectly good run is reported as one that never reached us.
            reached.Task.Wait(TimeSpan.FromMilliseconds(10000));
#pragma warning restore CA2016

            // A script that ran but never reached the macro is a run that could not be served at
            // all, which is a different thing from a script that failed and said why.
            if (how.Lost.Length > 0 && !reached.Task.IsCompletedSuccessfully)
            {
                // What the process said is worth more than anything this side knows, and when it
                // said nothing at all the exit code is what is left: a copy of the program that
                // could not start at all is a different story from one that started and ended.
                throw new DeviceActionException(how.Lost, complained.Count > 0
                    ? complained[^1]
                    : $"{program} ({exitCode})");
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
}
