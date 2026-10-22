using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// The other end of a script's bridge: the program started a second time to run a script a macro
/// compiled. It connects to the pipe the macro is listening on, hands the script the way back into
/// the macro, and calls the entry point it was written with.
/// </summary>
/// <remarks>
/// This runs before anything of the window is built and never touches it: a script that calls
/// <c>Environment.Exit</c>, that overflows its stack or that never ends is a program of its own
/// failing, and the macro watching its exit code is what decides what that means. Nothing here is
/// a device: it is the far side of one, which is why it lives with the interfaces rather than
/// behind them.
/// </remarks>
public static class ScriptHost
{
    /// <summary>The environment variable the child is told the pipe by.</summary>
    internal const string PipeVariable = "WHALEGENIE_PIPE";

    /// <summary>The environment variable that tells the child where this run keeps script packages.</summary>
    internal const string PackagesVariable = "WHALEGENIE_PACKAGES";

    /// <summary>What the program is asked with when it is being run as a script rather than as itself.</summary>
    internal const string Flag = "--script-host";

    /// <summary>How long the host waits for the macro's pipe before giving up on it.</summary>
    private const int ConnectWaitMs = 10000;

    /// <summary>Whether this run of the program is a script being run rather than the program itself.</summary>
    public static bool Asked(IReadOnlyList<string> arguments)
        => arguments.Count > 0 && string.Equals(arguments[0], Flag, StringComparison.Ordinal);

    /// <summary>
    /// Runs the script named after the flag and reports how it ended: the code the script's own
    /// entry point gave, or 1 when something on this side went wrong and it was said why. The exit
    /// code is what the macro reads, so a script that ends well has to end in zero.
    /// </summary>
    public static int Run(IReadOnlyList<string> arguments)
    {
        try
        {
            if (arguments.Count < 2)
            {
                Console.Error.WriteLine($"{Flag} needs the file of the script to run after it.");
                return 1;
            }

            var name = Environment.GetEnvironmentVariable(PipeVariable) ?? string.Empty;
            if (name.Length == 0)
            {
                Console.Error.WriteLine("This script was not started by WhaleGenie: no pipe to answer on.");
                return 1;
            }

            // The pipe first, so that a macro waiting to hear from this process hears about a
            // script that cannot even be loaded rather than waiting for a connection that never
            // comes.
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut);
            pipe.Connect(ConnectWaitMs);

            var path = Path.GetFullPath(arguments[1]);
            Reachable(Path.GetDirectoryName(path) ?? string.Empty);

            var script = Assembly.LoadFrom(path);
            Fill(script, new Bridge(pipe));

            var entry = script.EntryPoint
                ?? throw new InvalidOperationException("this script has nothing to run");

            var passed = arguments.Skip(2).ToArray();
            var ended = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : [passed]);

            // A script whose entry point answers with a number is saying how it ended, the way a
            // program started from a command line does.
            return ended is int code ? code : 0;
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine(failure is TargetInvocationException { InnerException: { } inside }
                ? inside.ToString()
                : failure.ToString());
            return 1;
        }
    }

    /// <summary>Puts the language's way back into the macro where the script looks for it.</summary>
    private static void Fill(Assembly script, Bridge bridge)
    {
        var services = script.GetType("__InjectedServices")
            ?? throw new InvalidOperationException("this script has no way back into the macro");
        var slot = services.GetField("DynamicObject", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("this script has no way back into the macro");

        slot.SetValue(null, bridge);
    }

    /// <summary>
    /// Makes the packages a macro downloaded reachable: the ones a script was compiled against are
    /// found by name, and the folders the packages keep their native libraries in are put on the
    /// path this process searches for them.
    /// </summary>
    private static void Reachable(string beside)
    {
        var root = Environment.GetEnvironmentVariable(PackagesVariable);
        var nuget = Path.Combine(root is { Length: > 0 } ? root : ScriptFolders.Packages, "nuget");
        var packages = NuGetStore.References(nuget);
        var folders = new List<string> { beside };
        folders.AddRange(packages.Select(reference => Path.GetDirectoryName(reference) ?? string.Empty));

        var searching = folders.Where(folder => folder.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        AssemblyLoadContext.Default.Resolving += (_, wanted) =>
        {
            foreach (var folder in searching)
            {
                var candidate = Path.Combine(folder, $"{wanted.Name}.dll");
                if (File.Exists(candidate))
                {
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
                }
            }

            return null;
        };

        var native = NuGetStore.NativeFolders(nuget);
        if (native.Count > 0)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Environment.SetEnvironmentVariable("PATH",
                string.Join(Path.PathSeparator, native) + Path.PathSeparator + path);
        }
    }

    /// <summary>
    /// What a script finds as <c>__InjectedServices.DynamicObject</c>: every call it makes is one
    /// line of JSON out and one line back, written under a lock because a script may start threads
    /// of its own.
    /// </summary>
    private sealed class Bridge(NamedPipeClientStream pipe) : DynamicObject
    {
        private readonly object _oneAtATime = new();
        private readonly StreamReader _hearing = new(pipe, NoMark, false, 4096, leaveOpen: true);
        private readonly StreamWriter _saying = new(pipe, NoMark, 4096, leaveOpen: true) { AutoFlush = true };

        public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? arguments,
            out object? result)
        {
            var passed = arguments ?? [];
            var message = binder.Name switch
            {
                "GetProperty" => new JsonObject { ["call"] = "get", ["name"] = Text(passed, 0) },
                "SetProperty" => new JsonObject
                {
                    ["call"] = "set",
                    ["name"] = Text(passed, 0),
                    ["value"] = JsonSerializer.SerializeToNode(At(passed, 1)),
                },
                "StepExecute" => new JsonObject { ["call"] = "step", ["id"] = Text(passed, 0) },
                "Print" => new JsonObject { ["call"] = "print", ["text"] = Text(passed, 0) },
                "SetResult" => new JsonObject
                {
                    ["call"] = "result",
                    ["value"] = JsonSerializer.SerializeToNode(At(passed, 0)),
                },
                _ => null,
            };

            if (message is null)
            {
                throw new MissingMethodException($"WhaleGenie has no {binder.Name}");
            }

            var said = Ask(message);
            result = binder.Name switch
            {
                "GetProperty" => Plain(said["value"]),
                "StepExecute" => said["value"]?.GetValue<bool>() ?? false,
                _ => null,
            };
            return true;
        }

        /// <summary>One request out, one answer back, with nothing else getting in between.</summary>
        private JsonObject Ask(JsonObject message)
        {
            string line;
            lock (_oneAtATime)
            {
                _saying.WriteLine(message.ToJsonString());
                line = _hearing.ReadLine()
                    ?? throw new IOException("the macro is no longer listening for this script");
            }

            return JsonNode.Parse(line) as JsonObject
                ?? throw new IOException("the macro answered with something that is not an answer");
        }

        private static object? At(object?[] passed, int index)
            => index < passed.Length ? passed[index] : null;

        private static string Text(object?[] passed, int index)
            => At(passed, index)?.ToString() ?? string.Empty;

        /// <summary>
        /// A value the macro holds, as a script of this language wants it: a record becomes a
        /// dictionary, a list a list, and the rest are the numbers, the words and the flags they
        /// were.
        /// </summary>
        private static object? Plain(JsonNode? node)
        {
            if (node is null)
            {
                return null;
            }

            if (node is JsonArray list)
            {
                return list.Select(Plain).ToList();
            }

            if (node is JsonObject record)
            {
                return record.ToDictionary(field => field.Key, field => Plain(field.Value),
                    StringComparer.Ordinal);
            }

            var element = node.GetValue<JsonElement>();
            return element.ValueKind switch
            {
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
                JsonValueKind.Null => null,
                _ => element.GetString(),
            };
        }
    }

    /// <summary>UTF-8 that leaves the byte-order mark off, which is how both ends write.</summary>
    private static readonly UTF8Encoding NoMark = new(encoderShouldEmitUTF8Identifier: false);
}
