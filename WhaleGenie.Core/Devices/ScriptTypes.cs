using System.Collections.Generic;
using System.Threading;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// What a script step hands its text to. The three command kinds and the two file kinds all end up
/// at cmd.exe and differ in their file ending and in what their window does; the rest name the
/// program that reads them.
/// </summary>
public enum ScriptKind
{
    /// <summary>A command run with its window left open once it has finished.</summary>
    CommandKeepWindow,

    /// <summary>A command run in a window that closes when the command is done.</summary>
    CommandCloseWindow,

    /// <summary>A command run with no window at all.</summary>
    CommandHidden,

    /// <summary>A batch file, which is the same as a hidden command under a .bat name.</summary>
    BatchFile,

    /// <summary>The same under a .cmd name.</summary>
    CommandFile,

    /// <summary>A Windows PowerShell script.</summary>
    PowerShell,

    /// <summary>An AutoHotkey script, which needs AutoHotkey on the machine.</summary>
    AutoHotkey,

    /// <summary>A program of the macro's own choosing, named with its flags and a file ending.</summary>
    Custom,
}

/// <summary>
/// One run of a Python script. The text has already had <c>{{name}}</c> filled in, and what is left
/// for the device to do is write it out, start the interpreter with a way back into the macro, and
/// wait for it.
/// </summary>
public sealed record PythonScript(
    string Script,
    string Arguments = "",
    string Folder = "",
    int TimeoutMs = 60000,
    string Command = "python");

/// <summary>One run of the package steps: which packages, and how long the whole thing may take.</summary>
public sealed record PipRequest(
    IReadOnlyList<string> Packages,
    string Command = "python",
    int TimeoutMs = 0);

/// <summary>The same for the NuGet side, which has no interpreter of its own to name.</summary>
public sealed record NuGetRequest(IReadOnlyList<string> Packages, int TimeoutMs = 0);

/// <summary>
/// One run of a C# script. The text has already had <c>{{name}}</c> filled in; it is compiled
/// against this machine's own assemblies — and the packages the macro asked for — and then run by
/// this program a second time, in a process that never builds a window.
/// </summary>
public sealed record CSharpScript(
    string Script,
    string Arguments = "",
    string Folder = "",
    int TimeoutMs = 60000);

/// <summary>
/// What a script came to. <see cref="ExitCode"/> is the program's own ending, which is what "did
/// this work" is answered by; <see cref="FailureKey"/> is set when the script never ran at all or
/// never got as far as having an exit code that means anything, and names the reason in the
/// engine's own words.
/// </summary>
public sealed record ScriptResult(
    int ExitCode,
    string Output,
    string Error,
    Value Result,
    string FailureKey = "");

/// <summary>
/// The macro as a script sees it while it runs: the values it may read and write, the steps it may
/// run, and somewhere to say something. A script is a language of its own, so this is the whole of
/// what it can reach — everything else it does, it does on the machine the way any program does.
/// </summary>
public interface IScriptHost
{
    /// <summary>A value the macro holds, or null for a name it does not know.</summary>
    Value? Read(string name);

    /// <summary>Writes a value into the macro being run.</summary>
    void Write(string name, Value value);

    /// <summary>
    /// Runs one step of the macro being run, found by the four-character identity written in the
    /// step list, and reports whether it ended well. A step that fails is the step's own failure:
    /// its own rule decides what happens to the rest of the macro, and this answer is what the
    /// script does about it.
    /// </summary>
    bool RunStep(string id);

    /// <summary>Puts a line into the run log.</summary>
    void Print(string text);
}

/// <summary>
/// Scripts and the packages they need. This is the one place a script of any language is run, and
/// it is behind the device layer for the same reason the browser is: a script outlives the step
/// that started it, and what a macro does with the answer is the engine's business rather than this
/// one's.
/// </summary>
public interface IScriptDevice
{
    /// <summary>
    /// Runs a Python script to the end, serving the ways back into the macro while it goes,
    /// <paramref name="onOutput"/> and <paramref name="onError"/> being handed each line as it
    /// arrives. A script that runs past its time is stopped where it stands.
    /// </summary>
    ScriptResult RunPython(PythonScript script, IScriptHost host, Action<string> onOutput,
        Action<string> onError, CancellationToken token);

    /// <summary>
    /// Reads which of these PIP packages are already there and installs the ones that are not,
    /// answering with the packages this run put there.
    /// </summary>
    ScriptResult InstallPip(PipRequest request, Action<string> onOutput, Action<string> onError,
        CancellationToken token);

    /// <summary>
    /// Compiles a C# script and runs it in a process of its own, serving the ways back into the
    /// macro while it goes. A script that does not compile, or one with nothing to run, never gets
    /// that far: the answer says which of the two it was, with the compiler's own words.
    /// </summary>
    ScriptResult RunCSharp(CSharpScript script, IScriptHost host, Action<string> onOutput,
        Action<string> onError, CancellationToken token);

    /// <summary>
    /// Reads which of these NuGet packages are already there and downloads the ones that are not,
    /// dependencies and all, answering with the packages this run put there. What was downloaded is
    /// also what a C# script is compiled against from then on.
    /// </summary>
    ScriptResult InstallNuGet(NuGetRequest request, Action<string> onOutput, Action<string> onError,
        CancellationToken token);
}
