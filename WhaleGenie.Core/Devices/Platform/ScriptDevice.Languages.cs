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

// Running a script in each language: python, AutoHotkey and C#.
public sealed partial class ScriptDevice : IScriptDevice
{

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
                throw new DeviceActionException(RunKeys.NoAutoHotkey, "AutoHotkey");
            }

            // Only the second version reads what this step writes. The first one reads a script of
            // its own shape, and a machine that has only that one has to hear so, rather than watch
            // a window full of its complaints about a script this program wrote.
            if (AutoHotkey.MajorVersion(program) == 1)
            {
                throw new DeviceActionException(RunKeys.AhkNeedsV2, program);
            }

            var arguments = new List<string> { Quoted(running) };
            if (script.Arguments.Length > 0)
            {
                arguments.Add(script.Arguments);
            }

            return Serve(
                new Serving(program, string.Join(' ', arguments), script.Folder,
                    new Dictionary<string, string>(), script.TimeoutMs, Missing: RunKeys.NoAutoHotkey),
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
                Dictionary<string, string>
                {
                    [ScriptHost.PackagesVariable] = _packages,
                }, script.TimeoutMs,
                    Missing: RunKeys.ProgramFailed, Lost: RunKeys.ScriptBridgeLost),
                host, onOutput, onError, token);
        }
        finally
        {
            Tidy(folder);
        }
    }
}
