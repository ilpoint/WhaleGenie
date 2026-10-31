using System;
using System.Collections.Generic;
using System.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend script hosts: what running a Python, C# or AutoHotkey script answers, what the script
/// does while it runs, and what installing packages reports.
/// </summary>
internal sealed class FakeScript(FakeDesk desk) : IScriptDevice
{
    /// <summary>What running a script in the fake reports back.</summary>
    public ScriptResult Python { get; set; } = new(0, string.Empty, string.Empty, Value.Null);

    /// <summary>
    /// What the script does while it runs, in place of running one: a check that is about what a
    /// script can reach hands in the calls it would make, so that reading a value, writing one,
    /// running a step and leaving a line in the log all arrive at the engine the way they would.
    /// </summary>
    public Action<IScriptHost, string>? PythonBody { get; set; }

    /// <summary>What installing packages reports back.</summary>
    public ScriptResult Packages { get; set; } = new(0, string.Empty, string.Empty, Value.FromList([]));

    /// <summary>What running a C# script in the fake reports back.</summary>
    public ScriptResult CSharp { get; set; } = new(0, string.Empty, string.Empty, Value.Null);

    /// <summary>
    /// What a C# script does while it runs, in place of being compiled and run: this language
    /// reaches the macro the same way the Python one does, so a check hands in the calls a script
    /// would make.
    /// </summary>
    public Action<IScriptHost, string>? CSharpBody { get; set; }

    /// <summary>What downloading NuGet packages reports back.</summary>
    public ScriptResult NuGet { get; set; } = new(0, string.Empty, string.Empty, Value.FromList([]));

    /// <summary>What running an AutoHotkey script in the fake reports back.</summary>
    public ScriptResult AutoHotkey { get; set; } = new(0, string.Empty, string.Empty, Value.Null);

    /// <summary>
    /// What an AutoHotkey script does while it runs, in place of being run: this language reaches
    /// the macro through the same bridge the other two do, so a check hands in the calls a script
    /// would make.
    /// </summary>
    public Action<IScriptHost, string>? AutoHotkeyBody { get; set; }

    public ScriptResult RunPython(PythonScript script, IScriptHost host,
        Action<string> onOutput, Action<string> onError, CancellationToken token)
    {
        desk.Note($"python {script.Command}|{script.Arguments}|{script.Folder}|{script.TimeoutMs}");
        PythonBody?.Invoke(host, script.Script);

        foreach (var line in Lines(Python.Output))
        {
            onOutput(line);
        }

        foreach (var line in Lines(Python.Error))
        {
            onError(line);
        }

        return Python;
    }

    public ScriptResult InstallPip(PipRequest request, Action<string> onOutput,
        Action<string> onError, CancellationToken token)
    {
        desk.Note($"pip {request.Command}|{request.TimeoutMs}|{string.Join(",", request.Packages)}");

        foreach (var line in Lines(Packages.Output))
        {
            onOutput(line);
        }

        foreach (var line in Lines(Packages.Error))
        {
            onError(line);
        }

        return Packages;
    }

    public ScriptResult RunCSharp(CSharpScript script, IScriptHost host,
        Action<string> onOutput, Action<string> onError, CancellationToken token)
    {
        desk.Note($"csharp {script.Arguments}|{script.Folder}|{script.TimeoutMs}");
        CSharpBody?.Invoke(host, script.Script);

        foreach (var line in Lines(CSharp.Output))
        {
            onOutput(line);
        }

        foreach (var line in Lines(CSharp.Error))
        {
            onError(line);
        }

        return CSharp;
    }

    public ScriptResult InstallNuGet(NuGetRequest request, Action<string> onOutput,
        Action<string> onError, CancellationToken token)
    {
        desk.Note($"nuget {request.TimeoutMs}|{string.Join(",", request.Packages)}");

        foreach (var line in Lines(NuGet.Output))
        {
            onOutput(line);
        }

        foreach (var line in Lines(NuGet.Error))
        {
            onError(line);
        }

        return NuGet;
    }

    public ScriptResult RunAutoHotkey(AutoHotkeyScript script, IScriptHost host,
        Action<string> onOutput, Action<string> onError, CancellationToken token)
    {
        desk.Note($"ahk {script.Command}|{script.Arguments}|{script.Folder}|{script.TimeoutMs}");
        AutoHotkeyBody?.Invoke(host, script.Script);

        foreach (var line in Lines(AutoHotkey.Output))
        {
            onOutput(line);
        }

        foreach (var line in Lines(AutoHotkey.Error))
        {
            onError(line);
        }

        return AutoHotkey;
    }

    /// <summary>The lines a stream holds, the way reading it a line at a time would find them.</summary>
    private static IEnumerable<string> Lines(string text)
        => text.Length == 0
            ? []
            : text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
}
