using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Tests;

/// <summary>
/// An AutoHotkey script run by the engine, against the AutoHotkey this machine has. AutoHotkey
/// cannot open the pipe a script of the other two languages talks over, so its calls go through a
/// second copy of this program started with <c>--script-bridge</c> — and only a real process can
/// say whether that works, so it is a check rather than one of the things left to a person.
/// </summary>
public class AutoHotkeyRunTests
{
    /// <summary>A folder of packages of this check's own, so the machine's own are left alone.</summary>
    private readonly string _packages =
        Path.Combine(Path.GetTempPath(), $"wg-ahk-{Guid.NewGuid():N}");

    [RealAutoHotkeyFact]
    public void An_AutoHotkey_script_reaches_the_macro_the_way_the_other_languages_do()
    {
        var reaching = new ReachingMacro();
        var complained = new List<string>();

        var outcome = Device().RunAutoHotkey(
            new AutoHotkeyScript(
                """
                Print("a line from the script")
                who := GetProperty("who")
                SetProperty("answer", who . "!")
                ran := StepExecute("a7k2")
                SetResult(who . "?" . (ran ? "ran" : "no"))
                """,
                TimeoutMs: 120000),
            reaching, _ => { }, complained.Add, default);

        // The script ran to its own end, so nothing of the two sides failed to reach the other
        // through the bridge process in between them.
        Assert.Empty(complained);
        Assert.Equal(0, outcome.ExitCode);

        // All five calls arrived at the macro: a value read, a value written, a step run by its
        // identity, a line left in the log, and a result handed back.
        Assert.Equal(["who"], reaching.Read);
        Assert.Equal("Ann!", reaching.Written["answer"].AsText());
        Assert.Equal(["a7k2"], reaching.Ran);
        Assert.Equal(["a line from the script"], reaching.Said);
        Assert.Equal("Ann?ran", outcome.Result.AsText());
    }

    [RealAutoHotkeyFact]
    public void A_mistake_in_a_script_is_what_the_step_says_rather_than_a_window()
    {
        var complained = new List<string>();
        var outcome = Device().RunAutoHotkey(
            new AutoHotkeyScript(
                "x := 1\nthrow Error(\"no\")\n", TimeoutMs: 120000),
            new ReachingMacro(), _ => { }, complained.Add, default);

        // AutoHotkey puts a mistake into a dialog of its own by default, which would leave the macro
        // waiting for a window nobody is there to close: the step hears about it as text instead,
        // and the line it names is the line of the script as it was written rather than one
        // counted through what the program puts in front of it.
        Assert.NotEqual(0, outcome.ExitCode);
        Assert.Contains("no", outcome.Error, StringComparison.Ordinal);
        Assert.Contains("(line 2)", outcome.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The program's own file, which is the process a script's calls are carried by. It sits beside
    /// the tests because this project's tests run the program that was just built.
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
/// A check that runs a real AutoHotkey script. AutoHotkey is not part of Windows, and only its
/// second version reads a script of this shape, so a machine without one says the check was skipped
/// rather than failing one about scripts.
/// </summary>
public sealed class RealAutoHotkeyFactAttribute : FactAttribute
{
    public RealAutoHotkeyFactAttribute()
    {
        var program = AutoHotkey.Found();
        if (program is null)
        {
            Skip = "This machine has no AutoHotkey to run a script with.";
        }
        else if (AutoHotkey.MajorVersion(program) == 1)
        {
            Skip = $"{program} is AutoHotkey version 1, and a script of this step's shape needs "
                + "version 2.";
        }
    }
}

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

    [Fact]
    public void A_script_runs_with_a_package_from_the_devices_folder()
    {
        var library = PutPackageAssembly();
        var outcome = Device().RunCSharp(
            new CSharpScript(
                """
                using System;
                class Program
                {
                    static void Main()
                    {
                        Console.WriteLine(PackageProbe.Value.Text());
                    }
                }
                """,
                TimeoutMs: 120000),
            new ReachingMacro(), _ => { }, _ => { }, default);

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal("package reached", outcome.Output);
        Assert.True(File.Exists(library));
    }

    [RealNuGetFact]
    public void A_downloaded_nuget_package_compiles_and_runs_in_the_script_host()
    {
        var device = Device();
        var installed = device.InstallNuGet(
            new NuGetRequest(["Newtonsoft.Json 13.0.3"], 120000),
            _ => { }, _ => { }, default);
        Assert.Equal(0, installed.ExitCode);
        Assert.Equal(string.Empty, installed.FailureKey);

        var outcome = device.RunCSharp(
            new CSharpScript(
                """
                using System;
                using Newtonsoft.Json.Linq;
                class Program
                {
                    static void Main()
                    {
                        var parsed = JObject.Parse("{\"name\":\"WhaleGenie\",\"value\":7}");
                        Console.WriteLine(parsed["name"] + ":" + parsed["value"]);
                    }
                }
                """,
                TimeoutMs: 120000),
            new ReachingMacro(), _ => { }, _ => { }, default);

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal("WhaleGenie:7", outcome.Output);
    }

    private string PutPackageAssembly()
    {
        var path = Path.Combine(_packages, "nuget", "PackageProbe", "1.0.0", "lib",
            "net10.0", "PackageProbe.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var source = CSharpSyntaxTree.ParseText(
            "namespace PackageProbe { public static class Value { public static string Text() => \"package reached\"; } }");
        var trusted = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists)
            .Select(file => (MetadataReference)MetadataReference.CreateFromFile(file));
        var compilation = CSharpCompilation.Create("PackageProbe", [source], trusted,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine,
            emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        File.WriteAllText(Path.Combine(_packages, "nuget", "references.json"),
            """["PackageProbe/1.0.0/lib/net10.0/PackageProbe.dll"]""");
        return path;
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

/// <summary>Runs the real feed check only when someone explicitly asks for it.</summary>
public sealed class RealNuGetFactAttribute : FactAttribute
{
    public RealNuGetFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("WHALEGENIE_REAL_NUGET"),
                "1", StringComparison.Ordinal))
        {
            Skip = "Set WHALEGENIE_REAL_NUGET=1 to download a package and run it through the script host.";
        }
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
