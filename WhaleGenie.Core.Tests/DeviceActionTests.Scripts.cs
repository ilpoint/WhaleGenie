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

// Running a script: where it runs, what it is given, and what it hands back.
public partial class DeviceActionTests
{
    [Fact]
    public async Task Running_a_script_keeps_what_it_printed_and_cleans_up_after_itself()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Command = new CommandResult(0, "\n  hello from a script  \n\n", string.Empty);
        devices.Files.PathExists = true;

        var (result, _, store) = await RunAsync(
        [
            Step("script.run", Param("kind", "ps1"),
                Param("script", "Write-Host 'hello from a script'"),
                Returns("output=said")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("  hello from a script  ", store.Local.Values["said"].AsText());

        // The script lives in the macro, so the copy handed to the interpreter is removed again.
        Assert.Contains(devices.Calls, call => call.StartsWith("writeFile") && call.Contains(".ps1"));
        Assert.Contains(devices.Calls, call => call.StartsWith("deleteFile") && call.Contains(".ps1"));
        Assert.Empty(devices.Files.Store);
        Assert.Contains(devices.Calls,
            call => call.StartsWith("run powershell.exe|-NoProfile") && call.Contains("|G:\\fake|"));
    }

    [Fact]
    public async Task A_script_runs_where_the_macros_are_when_that_folder_is_there()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.PathExists = true;
        await RunAsync(
            [Within(60000, Step("script.run", Param("kind", "ps1"), Param("script", "echo hi")))],
            devices);

        Assert.Contains(devices.Calls,
            call => call.StartsWith("run powershell.exe") && call.Contains("|G:\\fake|60000"));
    }

    [Fact]
    public async Task A_script_still_runs_when_the_macros_folder_is_not_there_yet()
    {
        // A program cannot be started in a folder that does not exist, so a folder that is not
        // there yet is left to whoever started WhaleGenie rather than failing the step.
        var devices = new FakeDeviceLayer();
        await RunAsync(
            [Within(60000, Step("script.run", Param("kind", "ps1"), Param("script", "echo hi")))],
            devices);

        Assert.Contains(devices.Calls,
            call => call.StartsWith("run powershell.exe") && call.EndsWith("||60000|out system"));
    }

    [Fact]
    public async Task A_script_can_be_given_arguments_and_a_folder_of_its_own()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Within(5000, Step("script.run", Param("kind", "ps1"), Param("script", "echo hi"),
                Param("scriptArguments", "--quiet"),
                Param("folder", @"C:\work"))),
        ], devices);

        Assert.Contains(devices.Calls,
            call => call.StartsWith("run powershell.exe")
                && call.EndsWith("--quiet|C:\\work|5000|out system"));
    }

    [Fact]
    public async Task A_script_that_asks_for_no_limit_is_not_cut_short_by_a_clock()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Within(0, Step("script.run", Param("kind", "bat"), Param("script", "echo hi"))),
        ], devices);

        // A timeout of nought is "as long as it takes", so the command is given the longest wait the
        // process API has rather than the moment's worth of nought milliseconds it would mean.
        Assert.Contains(devices.Calls, call => call.Contains($"|{int.MaxValue}|"));
    }

    [Fact]
    public async Task A_script_can_have_a_macro_value_written_into_it()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("control.setVariable", Param("name", "who"), Param("value", "Ann")),
            Step("script.run", Param("kind", "ps1"), Param("script", "Write-Host {{who}}"),
                Param("scriptArguments", "--to {{who}} --x {{nope}}"), Returns("output=out")),
        ], devices);

        // The value goes in where it was asked for, in the script and on the command line alike,
        // and a name the macro does not know is left where it can be seen rather than quietly
        // turning into nothing.
        Assert.Contains(devices.Calls,
            call => call.StartsWith("writeFile") && call.EndsWith("Write-Host Ann False utf8bom"));
        Assert.Contains(devices.Calls, call =>
            call.StartsWith(@"run powershell.exe|-NoProfile")
            && call.Contains("--to Ann --x {{nope}}"));
    }

    [Fact]
    public async Task A_script_can_be_run_by_a_program_the_macro_names_itself()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Within(60000, Step("script.run", Param("kind", "custom"),
                Param("interpreter", "dotnet-script"),
                Param("extension", "csx"), Param("script", "Console.WriteLine(1);"))),
        ], devices);

        // The program is the macro's to name, the script's path goes after it, and an ending
        // written without a full stop gets one.
        Assert.Contains(devices.Calls, call =>
            call.StartsWith("run dotnet-script|") && call.Contains(".csx\"||60000"));
        Assert.Contains(devices.Calls, call =>
            call.StartsWith("writeFile") && call.EndsWith(".csx Console.WriteLine(1); False utf8"));
    }

    [Fact]
    public async Task A_script_for_another_program_says_so_when_it_names_no_program()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("script.run", Param("kind", "custom"), Param("extension", "csx"),
                Param("script", "echo hi")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingInterpreter", result.Key);
    }

    [Fact]
    public async Task A_script_for_another_program_says_so_when_it_names_no_ending()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("script.run", Param("kind", "custom"), Param("interpreter", "dotnet-script"),
                Param("script", "echo hi")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingScriptExtension", result.Key);
    }

    [Fact]
    public async Task A_script_that_stops_with_an_error_fails_the_step_and_hands_over_what_it_said()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Command = new CommandResult(1, "  what it printed  ", "cannot open the file");

        var (result, _, store) = await RunAsync(
        [
            Step("script.run", Param("kind", "ps1"), Param("script", "throw 'no'"),
                Returns("output=said", "error=why", "success=ok")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ScriptFailed", result.Key);
        Assert.Contains("cannot open the file", result.Detail);

        // The step is failed for the exit code, and what the script came to is handed over all the
        // same: that is what a macro reads on the steps after this one, once its failure rule — the
        // step's own, or the answer the user gave the question — has let it carry on.
        // Only the blank lines around it go: what the script printed is kept as it printed it.
        Assert.Equal("  what it printed  ", store.Local.Values["said"].AsText());
        Assert.Equal("cannot open the file", store.Local.Values["why"].AsText());
        Assert.False(store.Local.Values["ok"].Flag);
    }

    /// <summary>
    /// 退出码单独交回一份。脚本用不同的代码说不同的意思很常见——3 可能是「今天没有要做的事」——
    /// 只留一个「成功与否」时，这个意思就没了；失败规则放行之后，后面的步骤要按它分支。
    /// </summary>
    [Fact]
    public async Task A_script_hands_over_the_code_it_ended_with()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Command = new CommandResult(3, "nothing to do", string.Empty);

        var (result, _, store) = await RunAsync(
        [
            Step("script.run", Param("kind", "ps1"), Param("script", "exit 3"),
                Returns("exitCode=code", "success=ok")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ScriptFailed", result.Key);
        Assert.Equal(3d, store.Local.Values["code"].Number);
        Assert.False(store.Local.Values["ok"].Flag);
    }

    [Fact]
    public async Task A_script_step_with_nothing_in_it_fails()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("script.run", Param("kind", "ps1"), Param("script", "   "))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingScript", result.Key);
    }

    [Fact]
    public async Task A_kind_of_script_WhaleGenie_does_not_know_fails()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [Step("script.run", Param("kind", "brainfuck"), Param("script", "+++"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownScriptKind", result.Key);
    }

    [Fact]
    public async Task A_machine_with_no_AutoHotkey_says_so_rather_than_failing_the_command_line()
    {
        var devices = new FakeDeviceLayer();
        await WhereAutoHotkey(null,
        [
            Step("script.run", Param("kind", "ahk"), Param("script", "MsgBox 1")),
        ], devices, (result, _) =>
        {
            Assert.False(result.Succeeded);
            Assert.Equal("Run.NoAutoHotkey", result.Key);

            // Nothing was written and nothing was started: a machine without the program is not a
            // command line that failed, it is a step that could not be run.
            Assert.DoesNotContain(devices.Calls, call => call.StartsWith("start"));
            Assert.DoesNotContain(devices.Files.Store.Keys,
                path => path.EndsWith(".ahk", StringComparison.Ordinal));
        });
    }

    /// <summary>One step with the identity a script would name it by.</summary>
    private static ExecutableStep Called(string id, ExecutableStep step) => new()
    {
        Type = step.Type,
        Id = id,
        Parameters = step.Parameters,
        Meta = step.Meta,
    };

    [Fact]
    public async Task A_script_named_as_a_file_is_run_where_it_lies()
    {
        // The macro names a file rather than writing the script out: what it hands over is that
        // file's place on disk, taken from the macro's own folder when it is written as a path
        // relative to it, and nothing of the file is copied anywhere.
        var devices = new FakeDeviceLayer();
        devices.Files.PathExists = true;
        devices.Files.Store[@"G:\fake\deploy.ps1"] = "Write-Host {{who}}";

        var (result, _, _) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "who"), Param("value", "Ann")),
            Step("script.run", Param("scriptSource", "file"), Param("kind", "ps1"),
                Param("scriptFile", "deploy.ps1"), Param("scriptArguments", "--to {{who}}")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(devices.Calls, call => call.StartsWith("run powershell.exe")
            && call.Contains(@"-File ""G:\fake\deploy.ps1"" --to Ann"));

        // Not one byte of the file is touched: no copy of a script is written under any ending, and
        // a placeholder inside it stays what its author wrote, because what is run is the file
        // itself rather than something this program made out of it.
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("writeFile"));
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("deleteFile"));
        Assert.Equal("Write-Host {{who}}", devices.Files.Store[@"G:\fake\deploy.ps1"]);
    }

    [Fact]
    public async Task A_file_script_is_run_by_whatever_kind_of_script_it_was_said_to_be()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.PathExists = true;
        await RunAsync(
        [
            Step("script.run", Param("scriptSource", "file"), Param("kind", "bat"),
                Param("scriptFile", "work.bat")),
            Step("script.run", Param("scriptSource", "file"), Param("kind", "cmdHidden"),
                Param("scriptFile", "work.cmd")),
            Step("script.run", Param("scriptSource", "file"), Param("kind", "custom"),
                Param("interpreter", "cscript //nologo"), Param("scriptFile", "log.vbs")),
        ], devices);

        // cmd.exe is given the file when no window is wanted, and the program a macro names itself
        // is given it with its own flags in front — without an ending of the script's own being
        // asked for, because the file is already on disk with the ending its author gave it.
        Assert.Contains(devices.Calls, call => call.StartsWith("run cmd.exe|/c ")
            && call.Contains(@"""G:\fake\work.bat"""));
        Assert.Contains(devices.Calls, call => call.StartsWith("run cmd.exe|/c ")
            && call.Contains(@"""G:\fake\work.cmd"""));
        Assert.Contains(devices.Calls, call => call.StartsWith(@"run cscript|//nologo ")
            && call.Contains(@"""G:\fake\log.vbs"""));
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("writeFile"));
    }

    [Fact]
    public async Task The_window_that_stays_open_runs_a_named_file_through_a_little_batch_file_of_its_own()
    {
        // The one kind that has to be told the command is over is the kind whose script cannot be
        // touched: what is written instead is a two-line batch file that calls the macro's file and
        // then leaves the mark behind, and the file the macro named is left as it was.
        var devices = new FakeDeviceLayer();
        devices.Files.PathExists = true;
        devices.Files.Text = "0";
        var (result, _, _) = await RunAsync(
        [
            Step("script.run", Param("scriptSource", "file"), Param("kind", "cmdKeep"),
                Param("scriptFile", "deploy.bat"), Param("scriptArguments", "--quiet")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(devices.Calls, call => call.StartsWith("writeFile")
            && call.Contains(@"call ""G:\fake\deploy.bat"" --quiet")
            && call.Contains("@echo %errorlevel% >"));
        Assert.DoesNotContain(devices.Calls,
            call => call.StartsWith(@"writeFile G:\fake\deploy.bat "));

        // The window runs that batch file and is left standing, and the batch file is this step's
        // own business rather than something the macro keeps.
        Assert.Contains(devices.Calls, call => call.StartsWith("start cmd.exe|/k ")
            && call.Contains(@".cmd""") && call.EndsWith("|False"));
        Assert.Contains(devices.Calls, call => call.StartsWith("deleteFile")
            && call.Contains(".cmd"));
    }

    [Fact]
    public async Task A_named_script_that_is_not_there_says_so_where_it_would_have_been_run()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("script.run", Param("scriptSource", "file"), Param("kind", "ps1"),
                Param("scriptFile", @"C:\gone\deploy.ps1")),
        ], devices);

        // A file that is not there is this step's own answer rather than whatever the interpreter
        // would have said about it, and nothing is started to say it.
        Assert.False(result.Succeeded);
        Assert.Equal("Run.FileNotFound", result.Key);
        Assert.Contains(@"C:\gone\deploy.ps1", result.Detail);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("run "));
    }

    [Fact]
    public async Task A_script_step_naming_no_file_and_writing_no_script_fails()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("script.run", Param("scriptSource", "file"), Param("kind", "ps1"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingScript", result.Key);
    }
}
