using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Tests;

// The languages a step can be written in: AutoHotkey, Python and C#, and their packages.
public partial class DeviceActionTests
{
    [Fact]
    public async Task An_AutoHotkey_step_is_run_where_and_with_what_it_says()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.PathExists = true;
        await RunAsync(
        [
            Within(5000, Step("script.ahk", Param("script", "Print(\"hi\")"),
                Param("scriptArguments", "--quiet"),
                Param("ahk", @"C:\Tools\AutoHotkey64.exe"))),
        ], devices);

        Assert.Contains(devices.Calls,
            call => call.StartsWith(@"ahk C:\Tools\AutoHotkey64.exe|--quiet|G:\fake|5000"));
    }

    [Fact]
    public async Task An_AutoHotkey_script_that_names_no_program_runs_with_this_machines_own()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync([Step("script.ahk", Param("script", "Print(\"hi\")"))], devices);

        Assert.Contains(devices.Calls, call => call.StartsWith("ahk |"));
    }

    [Fact]
    public async Task An_AutoHotkey_script_reaches_the_macro_the_way_the_other_two_languages_do()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.AutoHotkey = new ScriptResult(0, "printed\n", string.Empty, Value.FromText("handed"));
        devices.Files.PathExists = true;

        var asked = new List<string>();
        devices.Script.AutoHotkeyBody = (host, _) =>
        {
            asked.Add(host.Read("who")?.AsText() ?? "nothing");
            host.Write("answer", Value.FromText("Ann!"));
            host.Print("a line from the script");
            asked.Add(host.RunStep("a7k2") ? "ran" : "refused");
        };

        var (result, _, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "who"), Param("value", "Ann")),
            Step("script.ahk", Param("script", "Print(\"hi\")"),
                Returns("value=handed", "error=why", "success=ok")),
            Called("a7k2", Step("control.setVariable", Param("name", "after"), Param("value", "yes"))),
        ], devices);

        // The five names this language reaches the macro by arrive the same way the other two
        // languages' do, so what is checked here is that a step written in this one reaches them at
        // all rather than how each of them is spelled.
        Assert.Equal(["Ann", "ran"], asked);
        Assert.Equal("Ann!", store.Local.Values["answer"].AsText());
        Assert.Equal("yes", store.Local.Values["after"].AsText());
        Assert.Equal("handed", store.Local.Values["handed"].AsText());
        Assert.True(store.Local.Values["ok"].Flag);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task An_AutoHotkey_script_that_names_a_step_this_macro_has_not_got_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.AutoHotkeyBody = (host, _) => host.RunStep("zzzz");

        var (result, _, _) = await RunAsync(
            [Step("script.ahk", Param("script", "StepExecute(\"zzzz\")"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NoSuchStep", result.Key);
    }

    [Fact]
    public async Task An_AutoHotkey_step_with_nothing_written_in_it_fails()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("script.ahk", Param("script", "  \n"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingScript", result.Key);
    }

    [Fact]
    public async Task An_AutoHotkey_script_that_stops_with_an_error_fails_the_step_and_hands_over_what_it_said()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.AutoHotkey = new ScriptResult(1, string.Empty, "no (line 2)", Value.Null);

        var (result, _, store) = await RunAsync(
        [
            Step("script.ahk", Param("script", "throw Error(\"no\")"),
                Returns("value=handed", "error=why", "success=ok")),
        ], devices);

        // AutoHotkey's own report of a mistake is what the step failed with, and what the script
        // came to is handed over all the same.
        Assert.False(result.Succeeded);
        Assert.Equal("Run.ScriptFailed", result.Key);
        Assert.Equal("no (line 2)", store.Local.Values["why"].AsText());
        Assert.False(store.Local.Values["ok"].Flag);
        Assert.Equal(Value.Null, store.Local.Values["handed"]);
    }

    [Fact]
    public async Task A_python_script_reads_and_writes_the_values_of_the_macro_while_it_runs()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.Python = new ScriptResult(1, "printed\n", string.Empty, Value.Null);

        var asked = new List<string>();
        var said = new List<string>();
        var handed = new List<string>();
        devices.Script.PythonBody = (host, _) =>
        {
            asked.Add(host.Read("who")?.AsText() ?? "nothing");
            host.Write("answer", Value.FromText("Ann!"));
            host.Print("a line from the script");
            said.Add(host.RunStep("a7k2") ? "ran" : "refused");
            handed.Add(host.Read("after")?.AsText() ?? "nothing");
        };

        var (result, _, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "who"), Param("value", "Ann")),
            Step("script.python", Param("script", "WhaleGenie.Print('hi')"),
                Returns("value=handed", "error=why", "success=ok")),
            Called("a7k2", Step("control.setVariable", Param("name", "after"), Param("value", "yes"))),
        ], devices);

        // What the script asked for arrived where a macro keeps it, in both directions, and a step
        // it named by its own identity ran the way any other step of the macro does.
        Assert.Equal("Ann", asked.Single());
        Assert.Equal(["ran"], said);
        Assert.Equal("Ann!", store.Local.Values["answer"].AsText());
        Assert.Equal("yes", store.Local.Values["after"].AsText());
        Assert.Equal("yes", handed.Single());

        // The script ended with a code that is not zero, so the step failed — and what it came to
        // was handed over all the same.
        Assert.False(result.Succeeded);
        Assert.Equal("Run.ScriptFailed", result.Key);
        Assert.False(store.Local.Values["ok"].Flag);
        Assert.Equal(string.Empty, store.Local.Values["why"].AsText());
    }

    [Fact]
    public async Task A_python_script_hands_back_what_it_set_as_the_result()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.Python = new ScriptResult(0, "one\ntwo", string.Empty, Value.FromText("handed"));

        var (result, _, store) = await RunAsync(
        [
            Step("script.python", Param("script", "WhaleGenie.SetResult('handed')"),
                Returns("value=answer", "success=ok")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("handed", store.Local.Values["answer"].AsText());
        Assert.True(store.Local.Values["ok"].Flag);
    }

    [Fact]
    public async Task A_python_step_is_run_where_and_with_what_it_says()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.PathExists = true;
        await RunAsync(
        [
            Within(5000, Step("script.python", Param("script", "print('hi')"),
                Param("scriptArguments", "--quiet"), Param("python", "py -3.12"))),
        ], devices);

        Assert.Contains(devices.Calls, call => call.StartsWith("python py -3.12|--quiet|G:\\fake|5000"));
    }

    [Fact]
    public async Task A_script_cannot_write_a_value_whale_genie_provides()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.Python = new ScriptResult(0, string.Empty, string.Empty, Value.Null);
        devices.Script.PythonBody = (host, _) =>
        {
            host.Write("sys.date", Value.FromText("1999-01-01"));
            host.Write("mine", Value.FromText("kept"));
        };

        var store = new VariableStore();
        store.System.Set("sys.date", Value.FromText("2026-11-15"));
        var result = await new MacroRunner(store, new AgreeingHost(), devices).RunAsync(
            [Step("script.python", Param("script", "WhaleGenie.SetProperty('sys.date', 'x')"))]);

        // The script asked to write something it cannot have: the write is dropped rather than
        // shadowing the clock, and everything else it wrote is still there.
        Assert.True(result.Succeeded);
        Assert.True(store.TryGet("sys.date", out var date));
        Assert.Equal("2026-11-15", date.AsText());
        Assert.Equal("kept", store.Local.Values["mine"].AsText());
    }

    [Fact]
    public async Task A_python_script_that_names_no_interpreter_runs_with_this_machines_own()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync([Step("script.python", Param("script", "print('hi')"))], devices);

        Assert.Contains(devices.Calls, call => call.StartsWith("python python|"));
    }

    [Fact]
    public async Task A_python_script_that_names_a_step_this_macro_has_not_got_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.PythonBody = (host, _) => host.RunStep("zzzz");

        var (result, _, _) = await RunAsync(
        [
            Step("script.python", Param("script", "WhaleGenie.StepExecute('zzzz')")),
        ], devices);

        // A name that is in no step of this macro is a mistake in the script, and it is reported in
        // the engine's own words rather than as a traceback from inside the interpreter.
        Assert.False(result.Succeeded);
        Assert.Equal("Run.NoSuchStep", result.Key);
    }

    [Fact]
    public async Task A_python_step_with_nothing_in_it_fails()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("script.python", Param("script", "   "))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingScript", result.Key);
    }

    [Fact]
    public async Task Installing_packages_hands_back_what_went_in()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.Packages = new ScriptResult(0, string.Empty, string.Empty,
                Value.FromList([Value.FromText("requests")]));

        var (result, _, store) = await RunAsync(
        [
            Step("script.pip", Param("packages", "requests\n# a word about it\nnumpy==2.1.3"),
                Param("python", "py -3.12"), Returns("packages=added")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(["requests"], store.Local.Values["added"].AsList().Select(item => item.AsText()));

        // The lines that name a package are read as they are, a comment is not, and the step's own
        // timeout is what the download is allowed: this kind of step has no number of its own.
        Assert.Contains(devices.Calls, call =>
            call == @"pip py -3.12|0|requests,numpy==2.1.3");
    }

    [Fact]
    public async Task A_package_step_that_names_nothing_says_so()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("script.pip", Param("packages", "  \n# nothing here\n"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NoPackages", result.Key);
    }

    [Fact]
    public async Task A_C_sharp_script_reaches_the_macro_the_way_the_python_one_does()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.CSharp = new ScriptResult(0, "printed\n", string.Empty, Value.FromText("handed"));
        devices.Files.PathExists = true;

        var asked = new List<string>();
        devices.Script.CSharpBody = (host, _) =>
        {
            asked.Add(host.Read("who")?.AsText() ?? "nothing");
            host.Write("answer", Value.FromText("Ann!"));
            host.Print("a line from the script");
            asked.Add(host.RunStep("a7k2") ? "ran" : "refused");
        };

        var (result, _, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "who"), Param("value", "Ann")),
            Within(5000, Step("script.csharp", Param("script", "Console.WriteLine(\"hi\");"),
                Param("scriptArguments", "--quiet"),
                Returns("value=handed", "error=why", "success=ok"))),
            Called("a7k2", Step("control.setVariable", Param("name", "after"), Param("value", "yes"))),
        ], devices);

        // The two languages reach the macro through the same four calls, so what is checked here is
        // that the step an author writes reaches them at all rather than how they are spelled.
        Assert.Equal(["Ann", "ran"], asked);
        Assert.Equal("Ann!", store.Local.Values["answer"].AsText());
        Assert.Equal("yes", store.Local.Values["after"].AsText());
        Assert.Equal("handed", store.Local.Values["handed"].AsText());
        Assert.True(store.Local.Values["ok"].Flag);
        Assert.True(result.Succeeded);

        // What the step says about where and how long is handed to the device as it was written.
        Assert.Contains(devices.Calls, call => call.StartsWith("csharp --quiet|G:\\fake|5000"));
    }

    [Fact]
    public async Task A_script_that_does_not_compile_fails_the_step_with_what_the_compiler_said()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.CSharp = new ScriptResult(1, string.Empty, "script.cs(3,9): error CS0103: oops",
                Value.Null, "Run.ScriptCompileFailed");

        var (result, _, store) = await RunAsync(
        [
            Step("script.csharp", Param("script", "oops();"),
                Returns("value=handed", "error=why", "success=ok")),
        ], devices);

        // The step fails in the engine's own words for a script that will not compile, and the
        // compiler's own words are what it failed with: they are the message, not a number.
        Assert.False(result.Succeeded);
        Assert.Equal("Run.ScriptCompileFailed", result.Key);
        Assert.Contains("CS0103", result.Detail, StringComparison.Ordinal);
        Assert.Equal("script.cs(3,9): error CS0103: oops", store.Local.Values["why"].AsText());
        Assert.False(store.Local.Values["ok"].Flag);
        Assert.Equal(Value.Null, store.Local.Values["handed"]);
    }

    [Fact]
    public async Task A_C_sharp_step_with_nothing_to_run_says_so_rather_than_non_compiling()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.CSharp = new ScriptResult(1, string.Empty, string.Empty, Value.Null, "Run.NoEntryPoint");

        var (result, _, _) = await RunAsync(
            [Step("script.csharp", Param("script", "class Nothing { }"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NoEntryPoint", result.Key);
    }

    [Fact]
    public async Task A_C_sharp_step_with_nothing_written_in_it_fails()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("script.csharp", Param("script", "  \n"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingScript", result.Key);
    }

    [Fact]
    public async Task Downloading_packages_hands_back_what_went_in()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.NuGet = new ScriptResult(0, string.Empty, string.Empty,
                Value.FromList([Value.FromText("PyYAML 6.0.0")]));

        var (result, _, store) = await RunAsync(
        [
            Within(90000, Step("script.nuget",
                Param("packages", "PyYAML 6.0.0\n# a word about it\nSerilog"),
                Returns("packages=added", "error=why", "success=ok"))),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(["PyYAML 6.0.0"],
            store.Local.Values["added"].AsList().Select(item => item.AsText()));

        // The lines that name a package are read as they are, a comment is not, and the step's own
        // timeout is what the download is allowed: this kind of step has no number of its own.
        Assert.Contains(devices.Calls, call => call == "nuget 90000|PyYAML 6.0.0,Serilog");
    }

    [Fact]
    public async Task A_download_that_goes_wrong_fails_the_step_and_still_hands_the_three_over()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.NuGet = new ScriptResult(1, string.Empty, "there is no Such.Package on nuget.org",
                Value.FromList([]), "Run.NuGetFailed");

        var (result, _, store) = await RunAsync(
        [
            Step("script.nuget", Param("packages", "Such.Package"),
                Returns("packages=added", "error=why", "success=ok")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NuGetFailed", result.Key);
        Assert.Empty(store.Local.Values["added"].AsList());
        Assert.Contains("Such.Package", store.Local.Values["why"].AsText(), StringComparison.Ordinal);
        Assert.False(store.Local.Values["ok"].Flag);
    }

    [Fact]
    public async Task A_nuget_step_that_names_nothing_says_so()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("script.nuget", Param("packages", "\n# nothing here\n"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NoPackages", result.Key);
    }

    [Fact]
    public void Installing_packages_leaves_the_ones_that_are_already_there_alone()
    {
        var folder = Directory.CreateTempSubdirectory("whalegenie-pip").FullName;
        try
        {
            // What pip leaves behind when a package is installed: a folder named after the package
            // and the version it was installed at, under the one packages of this program live in.
            Directory.CreateDirectory(Path.Combine(folder, "python", "PyYAML-6.0.dist-info"));

            var devices = new FakeDeviceLayer();
            var scripts = new ScriptDevice(devices.Processes, folder, string.Empty);
            scripts.InstallPip(new PipRequest(["PyYAML==6.0", "requests"]), _ => { }, _ => { },
                default);

            // Only what is missing is asked for, pip is told to put it in the folder the scripts
            // import from, and the version a step pinned is part of what "already there" means.
            Assert.Contains(devices.Calls, call =>
                call.StartsWith("run python|-m pip install")
                && call.Contains(Path.Combine(folder, "python")));
            Assert.Contains(devices.Calls, call => call.Contains("requests"));
            Assert.DoesNotContain(devices.Calls, call => call.Contains("PyYAML"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
