using System;
using System.Collections.Generic;
using System.IO;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Tests;

/// <summary>
/// A C# script compiled by the engine and run by the program itself, the way a macro runs one.
/// Compiling is the engine's own work and is checked there; what needs the program is the other
/// half — a second copy of WhaleGenie.exe started with <c>--script-host</c>, which has to come up
/// without building a window, reach the macro over the pipe and call the entry point the script was
/// written with. Only a real process can say whether that half works, and it needs no screen and no
/// person, so it is a check rather than one of the things left to a person.
/// </summary>
public class ScriptHostRunTests : IDisposable
{
    /// <summary>A folder of packages of this check's own, so the machine's own are left alone.</summary>
    private readonly string _packages =
        Path.Combine(Path.GetTempPath(), $"wg-host-{Guid.NewGuid():N}");

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
    public void A_compiled_script_runs_inside_the_program_with_the_macro_in_reach()
    {
        var reaching = new ReachingMacro();
        var complained = new List<string>();

        var outcome = Device().RunCSharp(
            new CSharpScript(
                """
                using System;

                class Program
                {
                    static void Main()
                    {
                        var WhaleGenie = __InjectedServices.DynamicObject;
                        dynamic who = WhaleGenie.GetProperty("who");
                        WhaleGenie.SetProperty("answer", who + "!");
                        WhaleGenie.Print("a line from the script");
                        dynamic ran = WhaleGenie.StepExecute("a7k2");
                        Console.WriteLine("printed " + ran);
                        WhaleGenie.SetResult(who + "?");
                    }
                }
                """,
                TimeoutMs: 120000),
            reaching, _ => { }, complained.Add, default);

        // The script ran to its own end in a process of its own, so nothing of the two sides failed
        // to reach the other.
        Assert.Empty(complained);
        Assert.Equal(0, outcome.ExitCode);

        // The four calls a script may make all arrived at the macro: a value read, a value written,
        // a step run by its identity, a line left in the log, and a result handed back.
        Assert.Equal(["who"], reaching.Read);
        Assert.Equal("Ann!", reaching.Written["answer"].AsText());
        Assert.Equal(["a7k2"], reaching.Ran);
        Assert.Equal(["a line from the script"], reaching.Said);
        Assert.Contains("printed True", outcome.Output, StringComparison.Ordinal);
        Assert.Equal("Ann?", outcome.Result.AsText());
    }

    [Fact]
    public void A_script_that_throws_says_what_it_threw_and_where()
    {
        var complained = new List<string>();
        var outcome = Device().RunCSharp(
            new CSharpScript(
                "using System;\nclass Program\n{\n    static void Main()\n    {\n"
                + "        throw new InvalidOperationException(\"no\");\n    }\n}\n",
                TimeoutMs: 120000),
            new ReachingMacro(), _ => { }, complained.Add, default);

        // The mistake is the script's own, and it is what the step hands back: a script that threw
        // is answered the same way however the program it ran in was started.
        Assert.NotEqual(0, outcome.ExitCode);
        Assert.Contains("InvalidOperationException", outcome.Error, StringComparison.Ordinal);
        Assert.Contains("no", outcome.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_that_ends_the_process_ends_only_its_own()
    {
        // Ending the process is a script's own business: it takes down the copy of the program that
        // was running it, and this check — which is standing where the macro would be — reads the
        // code it gave and carries on.
        var outcome = Device().RunCSharp(
            new CSharpScript(
                "class Program\n{\n    static void Main()\n    {\n"
                + "        System.Environment.Exit(3);\n    }\n}\n",
                TimeoutMs: 120000),
            new ReachingMacro(), _ => { }, _ => { }, default);

        Assert.Equal(3, outcome.ExitCode);
    }

    /// <summary>
    /// The program's own file, which is the process a script is run by. It sits beside the tests
    /// because this project's tests run the program that was just built.
    /// </summary>
    private static string Program() => Path.Combine(AppContext.BaseDirectory, "WhaleGenie.exe");

    private ScriptDevice Device()
    {
        var program = Program();
        Assert.True(File.Exists(program), $"the program itself is not beside the tests: {program}");
        return new ScriptDevice(new WindowsProcessDevice(), _packages, program);
    }
}

/// <summary>
/// The macro, as a script of a check sees it: every value it asks for is one this hands back, and
/// everything it does is written down rather than done.
/// </summary>
internal sealed class ReachingMacro : IScriptHost
{
    /// <summary>The names the script asked for, in the order it asked.</summary>
    public List<string> Read { get; } = [];

    /// <summary>What the script wrote, by name.</summary>
    public Dictionary<string, Value> Written { get; } = [];

    /// <summary>The steps the script asked for, in the order it asked.</summary>
    public List<string> Ran { get; } = [];

    /// <summary>The lines the script left in the log.</summary>
    public List<string> Said { get; } = [];

    Value? IScriptHost.Read(string name)
    {
        Read.Add(name);
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
