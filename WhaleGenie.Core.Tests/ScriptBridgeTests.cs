using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A script of one of the languages the program runs itself, against a real interpreter. Everything
/// else about these actions is checked through the double device layer, which says what the engine
/// asked for; whether a script can read a macro's values, run one of its steps and leave a line in
/// its log is only true or false against an interpreter that is really there. No screen and no
/// person are needed, so unlike the script kinds a macro writes itself this is a check rather than
/// something to do by hand.
/// </summary>
public class RealPythonTests : IDisposable
{
    /// <summary>
    /// A folder of packages of this check's own, so that a machine which has run the package step
    /// for real neither sees nor changes what is checked here.
    /// </summary>
    private readonly string _packages =
        Path.Combine(Path.GetTempPath(), $"wg-python-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_packages, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was installed, which is the usual case.
        }

        GC.SuppressFinalize(this);
    }

    [RealPythonFact]
    public void A_python_script_reads_the_macro_writes_it_and_hands_back_a_result()
    {
        var host = new RecordingScriptHost();
        var log = new List<string>();
        var device = new ScriptDevice(new WindowsProcessDevice(), _packages, string.Empty);

        var outcome = device.RunPython(
            new PythonScript(
                "text = WhaleGenie.GetProperty(\"who\")\n"
                + "WhaleGenie.SetProperty(\"answer\", text + \"!\")\n"
                + "WhaleGenie.Print(\"a line for the log\")\n"
                + "print(\"printed by the script\")\n"
                + "ran = WhaleGenie.StepExecute(\"a7k2\")\n"
                + "WhaleGenie.SetResult({\"said\": text, \"ran\": ran, \"n\": 2})\n"),
            host, log.Add, _ => { }, default);

        // The script ran to its own end, so nothing of the two sides failed to reach the other.
        Assert.Equal(0, outcome.ExitCode);

        // What it read came from the macro, what it wrote is what the macro holds now, and the step
        // it named was run by the macro rather than by anything on this side of the pipe.
        Assert.Equal(["who"], host.ReadNames);
        Assert.Equal("Ann!", host.Written["answer"].AsText());
        Assert.Equal(["a7k2"], host.Ran);

        // A line it left in the run log, and the ones it printed, all arrive where a macro's own
        // log goes.
        Assert.Equal(["a line for the log"], host.Said);
        Assert.Contains("printed by the script", log);
        Assert.Contains("printed by the script", outcome.Output);

        // And what it handed back is a record, with the fields a script writes as a dictionary.
        Assert.Equal("Ann", outcome.Result.Field("said")?.AsText());
        Assert.True(outcome.Result.Field("ran")?.Flag);
        Assert.Equal(2, outcome.Result.Field("n")?.AsNumber());
    }

    [RealPythonFact]
    public void A_python_script_that_fails_says_what_went_wrong_and_where()
    {
        var device = new ScriptDevice(new WindowsProcessDevice(), _packages, string.Empty);

        var outcome = device.RunPython(
            new PythonScript("raise ValueError(\"no\")\n"), new RecordingScriptHost(),
            _ => { }, _ => { }, default);

        // A mistake in a script is the interpreter's own report, and it is handed over as it stands:
        // the line it was written on is the one thing that says which line to look at.
        Assert.NotEqual(0, outcome.ExitCode);
        Assert.Contains("ValueError", outcome.Error, StringComparison.Ordinal);
        Assert.Contains("line 1", outcome.Error, StringComparison.Ordinal);
    }

    [RealPythonFact]
    public void A_python_script_that_runs_too_long_is_stopped_where_it_stands()
    {
        var device = new ScriptDevice(new WindowsProcessDevice(), _packages, string.Empty);

        var clock = Stopwatch.StartNew();
        var failure = Assert.Throws<DeviceActionException>(() => device.RunPython(
            new PythonScript("import time\ntime.sleep(30)\n", TimeoutMs: 1000),
            new RecordingScriptHost(), _ => { }, _ => { }, default));

        // The step is over in the time it was given rather than when the script would have got
        // round to it, which is what says the process was stopped rather than merely waited out.
        Assert.Equal("Run.CommandTimeout", failure.Key);
        Assert.True(clock.ElapsedMilliseconds < 20000, $"the wait took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A_machine_with_no_Python_says_so_rather_than_failing_the_command_line()
    {
        // Neither of these names is a program on any machine, which is exactly what a machine with
        // no Python on its search path looks like from here.
        const string Nothing = "whalegenie-no-such-python";
        var device = new ScriptDevice(new WindowsProcessDevice(), _packages, string.Empty);

        var ran = Assert.Throws<DeviceActionException>(() => device.RunPython(
            new PythonScript("print(1)\n", Command: Nothing), new RecordingScriptHost(),
            _ => { }, _ => { }, default));
        Assert.Equal("Run.NoPython", ran.Key);

        var installed = Assert.Throws<DeviceActionException>(() => device.InstallPip(
            new PipRequest(["requests"], Command: Nothing), _ => { }, _ => { }, default));
        Assert.Equal("Run.NoPython", installed.Key);
    }
}

/// <summary>
/// The macro, as a script of a check sees it: every value it asks for is one this hands back, and
/// everything it does is written down rather than done.
/// </summary>
internal sealed class RecordingScriptHost : IScriptHost
{
    /// <summary>The names the script asked for, in the order it asked.</summary>
    public List<string> ReadNames { get; } = [];

    /// <summary>What the script wrote, by name.</summary>
    public Dictionary<string, Value> Written { get; } = [];

    /// <summary>The steps the script asked for, in the order it asked.</summary>
    public List<string> Ran { get; } = [];

    /// <summary>The lines the script left in the log.</summary>
    public List<string> Said { get; } = [];

    public Value? Read(string name)
    {
        ReadNames.Add(name);
        return name == "who" ? Value.FromText("Ann") : null;
    }

    public void Write(string name, Value value) => Written[name] = value;

    public bool RunStep(string id)
    {
        Ran.Add(id);
        return true;
    }

    public void Print(string text) => Said.Add(text);
}

/// <summary>
/// The C# side of the same bridge, as far as it goes without running anything: whether a script
/// compiles at all, whether it has anything to run, and what the folders of a downloaded package
/// say about what a script is compiled against. All of it is this program's own work — no process
/// is started and nothing is downloaded — so it is a check rather than one of the things left to a
/// person with the program in front of them.
/// </summary>
public class CompilingTests : IDisposable
{
    /// <summary>
    /// A folder of packages of this check's own, so that what a machine has really downloaded is
    /// neither read nor written here.
    /// </summary>
    private readonly string _packages =
        Path.Combine(Path.GetTempPath(), $"wg-csharp-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_packages, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was downloaded, which is the usual case.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_script_that_does_not_compile_says_where_and_what_it_is()
    {
        var outcome = Device().RunCSharp(
            new CSharpScript("class Program\n{\n    static void Main()\n    {\n        oops();\n    }\n}\n"),
            new RecordingScriptHost(), _ => { }, _ => { }, default);

        // A script that will not compile is not run at all, and what the compiler said is what the
        // step hands back as its error message: the line it is about, and the mistake's own name.
        Assert.Equal("Run.ScriptCompileFailed", outcome.FailureKey);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("script.cs(5", outcome.Error, StringComparison.Ordinal);
        Assert.Contains("CS0103", outcome.Error, StringComparison.Ordinal);
        Assert.True(outcome.Error.Length < 2000, "the whole of a compiler's complaints is not a message");
    }

    [Fact]
    public void A_script_with_nothing_to_run_is_told_apart_from_one_that_does_not_compile()
    {
        var outcome = Device().RunCSharp(
            new CSharpScript("class Nothing\n{\n    void Work() { }\n}\n"),
            new RecordingScriptHost(), _ => { }, _ => { }, default);

        // There is nothing wrong with a class that has no entry point written yet, so it is not a
        // compilation failure: it is this step saying there is nothing to run.
        Assert.Equal("Run.NoEntryPoint", outcome.FailureKey);
        Assert.Equal(string.Empty, outcome.Error);
    }

    [Fact]
    public void A_package_that_is_already_there_is_not_looked_for_on_the_feed()
    {
        // What the package step leaves behind: one folder per package, one per version, and the
        // libraries it brought in a folder named after what they were built for.
        var library = Path.Combine(_packages, "nuget", "PyYAML", "6.0.0", "lib", "net8.0", "yaml.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        File.WriteAllText(library, string.Empty);

        var outcome = Device().InstallNuGet(new NuGetRequest(["PyYAML"]), _ => { }, _ => { }, default);

        // Nothing went in because nothing was missing, and the question the feed would have been
        // asked was never asked: a machine that has already downloaded these needs no network to
        // write a macro or to run it.
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(string.Empty, outcome.FailureKey);
        Assert.Empty(outcome.Result.AsList());

        // The list of what a C# script is compiled against is written from what is on disk, so a
        // script that names the package's library is compiled against it.
        var written = File.ReadAllText(Path.Combine(_packages, "nuget", "references.json"));
        Assert.Contains("yaml.dll", written, StringComparison.Ordinal);
    }

    private ScriptDevice Device() => new(new WindowsProcessDevice(), _packages, string.Empty);
}

/// <summary>
/// A check that runs a real Python script. Python is not part of Windows, so a machine without it
/// says the check was skipped rather than failing one about scripts.
/// </summary>
public sealed class RealPythonFactAttribute : FactAttribute
{
    public RealPythonFactAttribute()
    {
        if (!There())
        {
            Skip = "This machine has no Python 3 to run a script with.";
        }
    }

    /// <summary>
    /// Whether the word a script step uses by default runs anything. Asking the machine is the only
    /// answer that counts: a Python that is installed but not on the search path is a Python the
    /// step cannot find either.
    /// </summary>
    private static bool There()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("python", "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            return process is not null && process.WaitForExit(10000) && process.ExitCode == 0;
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException
            or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
