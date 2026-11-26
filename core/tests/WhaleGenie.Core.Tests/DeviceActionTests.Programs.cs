using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

// Starting programs and commands, waiting for them, and what they report.
public partial class DeviceActionTests
{
    [Fact]
    public async Task Starting_a_program_hands_back_its_record()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.NextProcessId = 4321;
        var (result, _, store) = await RunAsync(
        [
            Step("process.start", Param("programFile", "notepad.exe"), Param("commandArguments", "/a"),
                Param("workingDirectory", @"C:\temp"), Param("hidden", "false"),
                Returns("program=program")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(4321d, Field(store, "program", "id").AsNumber());
        Assert.Equal("notepad", Field(store, "program", "process").AsText());
        Assert.Equal(string.Empty, Field(store, "program", "title").AsText());
        Assert.Equal(0d, Field(store, "program", "handle").AsNumber());
        Assert.Contains(@"start notepad.exe|/a|C:\temp|False", devices.Calls);
    }

    [Fact]
    public async Task Starting_a_program_hands_nothing_over_until_a_result_is_asked_for()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.NextProcessId = 7;
        var (_, _, store) = await RunAsync(
            [Step("process.start", Param("programFile", "calc.exe"))], devices);

        Assert.Empty(store.Local.Values);
    }

    [Fact]
    public async Task Starting_a_program_as_administrator_asks_for_the_elevation_prompt()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("process.start", Param("programFile", "cmd.exe"), Param("commandArguments", "/c whoami"),
                Param("runAsAdmin", "true")),
        ], devices);

        // The device is the one that decides how to raise the prompt; the step only says it wants
        // elevation, and the flag is on the request rather than left out of it.
        Assert.Contains(devices.Calls, call =>
            call.StartsWith("start cmd.exe|/c whoami|") && call.EndsWith("|admin"));
    }

    [Fact]
    public async Task Starting_a_program_normally_does_not_ask_for_elevation()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync([Step("process.start", Param("programFile", "calc.exe"))], devices);

        Assert.DoesNotContain(devices.Calls, call => call.EndsWith("|admin"));
        // A new step shows the program: "hidden" is the exception, not the starting answer.
        Assert.Contains(devices.Calls, call => call.StartsWith("start calc.exe|", StringComparison.Ordinal)
                                              && call.EndsWith("|False", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Starting_a_program_can_hand_it_its_own_environment()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("control.setVariable", Param("name", "who"), Param("value", "Ann")),
            Step("process.start", Param("programFile", "tool.exe"),
                Fields.Run("environment", Formula.Run([Formula.Text("LANG=zh_CN.UTF-8\n#TOKEN=off\n\nUSER="), Formula.Name("who")]))),
        ], devices);

        // Only the lines that name something are passed on, and a value can be built out of a
        // variable the same way every other text field can.
        Assert.Contains(devices.Calls, call => call.EndsWith("|env LANG=zh_CN.UTF-8;USER=Ann"));
    }

    [Fact]
    public async Task A_dollar_in_a_text_value_is_just_a_character()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("control.setVariable", Param("name", "who"), Param("value", "Ann")),
            Step("process.start", Param("programFile", "tool.exe"),
                Param("environment", "USER=$who")),
        ], devices);

        // A dollar in a text value is a character like any other: the language has no marker in
        // front of a name, so nothing here looks for one and no escape is needed to write it.
        Assert.Contains(devices.Calls, call => call.EndsWith("|env USER=$who"));
    }

    [Fact]
    public async Task Running_a_command_can_give_it_its_own_environment()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("command.run", Param("commandFile", "cmd.exe"), Param("commandArguments", "/c set TOKEN"),
                Param("environment", "TOKEN=abc\nTOKEN=def")),
        ], devices);

        // The last line wins when the same name is written twice, so a macro can override a
        // setting further up without editing that line.
        Assert.Contains(devices.Calls, call => call.Contains("|env TOKEN=def"));
        Assert.DoesNotContain(devices.Calls, call => call.Contains("TOKEN=abc"));
    }

    [Fact]
    public async Task A_line_that_is_not_a_name_and_a_value_stops_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("command.run", Param("commandFile", "cmd.exe"), Param("environment", "TOKEN")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadEnvironment", result.Key);
    }

    [Fact]
    public async Task Running_a_command_can_tell_it_what_to_read()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("control.setVariable", Param("name", "who"), Param("value", "Ann")),
            Step("command.run", Param("commandFile", "python"), Fields.Run("standardInput", Formula.Run([Formula.Text("hello\n"), Formula.Name("who")]))),
        ], devices);

        // The line ending the macro wrote is kept, and a value can be written into the input the
        // same way it can be written into any other field.
        Assert.Contains(devices.Calls, call => call.Contains("|in hello\\nAnn"));
    }

    [Fact]
    public async Task A_command_that_is_not_given_anything_to_read_is_given_nothing()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("command.run", Param("commandFile", "cmd.exe"), Param("standardInput", "  \n ")),
        ], devices);

        // A field that holds nothing but spaces is an untouched field, not a program that is meant
        // to be fed a blank line.
        Assert.Contains(devices.Calls, call => call.StartsWith("run cmd.exe|") && !call.Contains("|in "));
    }

    [Fact]
    public async Task A_command_can_report_what_it_prints_while_it_is_still_running()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Command = new CommandResult(0, "first\n\nthird", "watch out");
        var host = new SilentRunHost();
        var store = new VariableStore();

        await new MacroRunner(store, host, devices).RunAsync(
        [
            Step("command.run", Param("commandFile", "build.exe"), Param("streamOutput", "true"),
                Returns("output=out")),
        ]);

        // Every line is handed over as it arrives, blank lines included, and the result variable
        // still holds all of it.
        Assert.Equal(
            ["first", "", "third"],
            host.Entries.Where(entry => entry.Key == "Log.CommandOutput")
                .Select(entry => (string)entry.Arguments[0]).ToArray());
        Assert.Equal("watch out",
            Assert.Single(host.Entries, entry => entry.Key == "Log.CommandError").Arguments[0]);
        Assert.Equal("first\n\nthird", store.Local.Values["out"].AsText());
    }

    [Fact]
    public async Task A_command_that_is_not_watched_keeps_its_output_to_itself()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Command = new CommandResult(0, "first\nsecond", string.Empty);
        var host = new SilentRunHost();

        await new MacroRunner(new VariableStore(), host, devices).RunAsync(
        [
            Step("command.run", Param("commandFile", "build.exe"), Returns("output=out")),
        ]);

        Assert.DoesNotContain(host.Entries, entry => entry.Key == "Log.CommandOutput");
        Assert.DoesNotContain(host.Entries, entry => entry.Key == "Log.CommandStarted");
    }

    [Fact]
    public async Task A_command_is_read_in_this_machines_own_code_page_unless_it_says_otherwise()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("command.run", Param("commandFile", "cmd.exe")),
            Step("command.run", Param("commandFile", "node"), Param("outputEncoding", "utf8")),
        ], devices);

        // Left alone, the output is read in this machine's own code page — the one cmd.exe and
        // Windows PowerShell print Chinese in — because getting that wrong turns every Chinese word
        // into question marks, and a program that prints UTF-8 wherever it runs can say so.
        Assert.Contains(devices.Calls, call => call.StartsWith("run cmd.exe|") && call.EndsWith("|out default"));
        Assert.Contains(devices.Calls, call => call.StartsWith("run node|") && call.EndsWith("|out utf8"));
    }

    /// <summary>
    /// Runs a check with AutoHotkey found where the check says it is: whether the machine these
    /// checks happen to run on has one of its own must not change what the engine asks for.
    /// </summary>
    private static async Task WhereAutoHotkey(string? program, ExecutableStep[] steps,
        FakeDeviceLayer devices, Action<RunResult, VariableStore> check)
    {
        var found = devices.Script.AutoHotkeyProgram;
        devices.Script.AutoHotkeyProgram = program;
        try
        {
            var (result, _, store) = await RunAsync(steps, devices);
            check(result, store);
        }
        finally
        {
            devices.Script.AutoHotkeyProgram = found;
        }
    }

    [Fact]
    public async Task Each_kind_of_script_goes_to_the_program_that_reads_it()
    {
        var devices = new FakeDeviceLayer();
        await WhereAutoHotkey(@"C:\Tools\AutoHotkey64.exe",
        [
            Step("script.run", Param("kind", "ps1"), Param("script", "echo hi")),
            Step("script.run", Param("kind", "bat"), Param("script", "echo hi")),
            Step("script.run", Param("kind", "cmdFile"), Param("script", "echo hi")),
            Step("script.run", Param("kind", "ahk"), Param("script", "MsgBox 1")),
            Step("script.run", Param("kind", "custom"), Param("interpreter", "cscript //nologo"),
                Param("extension", "vbs"), Param("script", "WScript.Echo 1")),
        ], devices, (_, _) =>
        {
            // PowerShell is given the flags that stop it reading a profile or asking anything; the
            // two file kinds go to cmd.exe; AutoHotkey is whatever program this machine has; and a
            // program of the macro's own choosing keeps its own flags in front of the script's path.
            Assert.Contains(devices.Calls,
                call => call.StartsWith("run powershell.exe|-NoProfile") && call.Contains(".ps1\""));
            Assert.Contains(devices.Calls,
                call => call.StartsWith("run cmd.exe|/c") && call.Contains(".bat\""));
            Assert.Contains(devices.Calls,
                call => call.StartsWith("run cmd.exe|/c") && call.Contains(".cmd\""));
            Assert.Contains(devices.Calls,
                call => call.StartsWith(@"run C:\Tools\AutoHotkey64.exe|") && call.Contains(".ahk\""));
            Assert.Contains(devices.Calls,
                call => call.StartsWith(@"run cscript|//nologo """) && call.Contains(".vbs\""));

            // Each is written under the ending its own kind is named after, in the code page that
            // kind of script is read in.
            Assert.Contains(devices.Calls, call => call.EndsWith(".ps1 echo hi False utf8bom"));
            Assert.Contains(devices.Calls, call => call.EndsWith(".bat echo hi False system"));
            Assert.Contains(devices.Calls, call => call.EndsWith(".cmd echo hi False system"));
            Assert.Contains(devices.Calls, call => call.EndsWith(".ahk MsgBox 1 False utf8bom"));
            Assert.Contains(devices.Calls, call => call.EndsWith(".vbs WScript.Echo 1 False utf8"));

            // What those programs print is read in this machine's own code page, which is what the
            // command prompt and Windows PowerShell write in when nothing is looking at the screen.
            Assert.All(devices.Calls.Where(call => call.StartsWith("run ")),
                call => Assert.EndsWith("|out system", call));
        });
    }

    [Fact]
    public async Task A_script_can_say_which_encoding_it_wants()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("script.run", Param("kind", "custom"), Param("interpreter", "cscript //nologo"),
                Param("extension", ".vbs"), Param("encoding", "gbk"),
                Param("script", "WScript.Echo \"中文\"")),
        ], devices);

        // cscript reads a script through the system code page, so a Chinese script has to be written
        // in GBK rather than in the UTF-8 a program of the macro's own choosing gets by default.
        Assert.Contains(devices.Calls, call =>
            call.StartsWith("writeFile") && call.EndsWith("gbk"));
    }

    [Fact]
    public async Task Waiting_for_a_program_keeps_the_id_once_it_appears()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.NextProcessId = 55;
        devices.Processes.AppearsLater = "notepad";
        devices.Processes.AppearsAfter = 1;
        var (result, _, store) = await RunAsync(
        [
            Within(2000, Step("process.waitFor", Param("window", "notepad"),
                Param("how", WindowHow.ProcessEquals), Parts("process", "pid"))),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(55d, store.Local.Values["pid"].Number);
    }

    [Fact]
    public async Task Waiting_for_a_program_gives_up_after_the_timeout()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Within(100, Step("process.waitFor", Param("window", "ghost"),
                Param("how", WindowHow.ProcessEquals))),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ProgramNotFound", result.Key);
    }

    [Fact]
    public async Task Checking_whether_a_program_runs_leaves_true_or_false()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Running["notepad"] = [10];
        var (_, _, store) = await RunAsync(
        [
            Step("process.exists", Param("window", "notepad"), Param("how", WindowHow.ProcessEquals),
                Returns("running=here")),
            Step("process.exists", Param("window", "gone"), Param("how", WindowHow.ProcessEquals),
                Returns("running=there")),
        ], devices);

        Assert.True(store.Local.Values["here"].Flag);
        Assert.False(store.Local.Values["there"].Flag);
    }

    /// <summary>
    /// One field, three ways of naming what it is about: the title of the window it shows, the
    /// name of its program, or the handle of that window. All three answer the same question.
    /// </summary>
    [Fact]
    public async Task A_program_is_named_by_its_title_its_name_or_a_handle()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Running["notepad"] = [42];
        devices.Window.Open.Add(new WindowInfo(7, "Notepad - notes.txt", new ScreenPoint(0, 0),
            new ScreenSize(300, 200), false, false));
        devices.Window.Traits[7] = ("notepad", "Notepad");
        devices.Window.ProcessIds[7] = 42;

        var (result, _, store) = await RunAsync(
        [
            Step("process.exists", Param("window", "Notepad - notes.txt"),
                Param("how", WindowHow.TitleContains), Returns("running=byTitle")),
            Step("process.exists", Param("window", "notepad"),
                Param("how", WindowHow.ProcessEquals), Returns("running=byName")),
            Step("process.exists", Param("window", "7"), Param("how", WindowHow.Handle),
                Returns("running=byHandle")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.True(store.Local.Values["byTitle"].Flag);
        Assert.True(store.Local.Values["byName"].Flag);
        Assert.True(store.Local.Values["byHandle"].Flag);
    }

    /// <summary>
    /// A program named by its own name is answered by the process table alone, which is what makes
    /// a service or a console program, neither of which has a window, usable at all.
    /// </summary>
    [Fact]
    public async Task A_program_with_no_window_still_answers_by_its_name()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Running["svc"] = [9];

        var (result, _, store) = await RunAsync(
        [
            Within(1000, Step("process.waitFor", Param("window", "svc"),
                Param("how", WindowHow.ProcessEquals), Returns("process=pid"))),
            Step("process.info", Param("window", "svc"), Param("how", WindowHow.ProcessEquals),
                Parts("program", "prog")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(9d, store.Local.Values["pid"].Number);
        Assert.Equal("svc", Field(store, "prog", "name").AsText());
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("findWindow"));
    }

    [Fact]
    public async Task Listing_programs_names_them_without_repeats()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Running["notepad"] = [1, 2];
        devices.Processes.Running["calc"] = [3];
        var (_, _, store) = await RunAsync(
            [Step("process.list", Returns("processes=names"))], devices);

        var names = store.Local.Values["names"];
        Assert.Equal(2, names.Items.Count);
        Assert.Equal(["calc", "notepad"], names.Items.Select(item => item.AsText()));
    }

    /// <summary>
    /// One program is one record, the way one window is: what it is called, what it is, and what it
    /// is costing are answers to the same question and travel together, so a part of it is read by
    /// name rather than out of the right one of five separate results.
    /// </summary>
    [Fact]
    public async Task Reading_a_programs_details_hands_it_over_as_one_record()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Running["notepad"] = [42];
        var (result, _, store) = await RunAsync(
        [
            Step("process.info", Param("window", "notepad"), Param("how", WindowHow.ProcessEquals),
                Parts("program", "note")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.EndsWith("notepad.exe", Field(store, "note", "info").AsText());
        Assert.Equal(42d, Field(store, "note", "id").Number);
        Assert.Equal("notepad", Field(store, "note", "name").AsText());
        Assert.Equal(12.5d, Field(store, "note", "memoryMb").Number);
        Assert.Equal(3.4d, Field(store, "note", "cpuSeconds").Number);
    }

    [Fact]
    public async Task Asking_about_a_program_that_is_not_running_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, store) = await RunAsync(
        [
            Step("process.info", Param("window", "ghost"), Param("how", WindowHow.ProcessEquals),
                Parts("program", "note")),
        ], devices);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.NoSuchProcess", result.Key);
        Assert.Equal(string.Empty, store.Local.Values["note"].AsText());
    }

    /// <summary>
    /// A program named by its process name closes every program of that name, which is the answer
    /// a question asked that way wants.
    /// </summary>
    [Fact]
    public async Task Closing_a_program_closes_every_program_of_that_name()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Running["notepad"] = [1, 2];
        var (_, _, store) = await RunAsync(
        [
            Step("process.kill", Param("window", "notepad"), Param("how", WindowHow.ProcessEquals),
                Returns("stopped=closed")),
        ], devices);

        Assert.Equal(2d, store.Local.Values["closed"].Number);
        Assert.Contains("stopId 1|False", devices.Calls);
        Assert.Contains("stopId 2|False", devices.Calls);
    }

    /// <summary>A program named by a window is that one program, not every copy of it.</summary>
    [Fact]
    public async Task Closing_a_program_by_its_window_closes_only_that_one()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Running["notepad"] = [9];
        devices.Window.Open.Add(new WindowInfo(7, "Notepad - notes.txt", new ScreenPoint(0, 0),
            new ScreenSize(300, 200), false, false));
        devices.Window.ProcessIds[7] = 9;

        var (_, _, store) = await RunAsync(
        [
            Step("process.kill", Param("window", "7"), Param("how", WindowHow.Handle),
                Param("force", "true"), Returns("stopped=closed")),
        ], devices);

        Assert.Equal(1d, store.Local.Values["closed"].Number);
        Assert.Contains("stopId 9|True", devices.Calls);
    }

    [Fact]
    public async Task Waiting_for_exit_keeps_the_exit_code()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Running["notepad"] = [42];
        devices.Processes.Exited.Add(42);
        devices.Processes.ExitCodes[42] = 7;
        var (result, _, store) = await RunAsync(
        [
            Within(1000, Step("process.waitExit", Param("window", "notepad"),
                Param("how", WindowHow.ProcessEquals), Returns("exitCode=code"))),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(7d, store.Local.Values["code"].Number);
    }

    /// <summary>
    /// The record a start step hands over can be waited on as it stands: it names the program by
    /// its own name, which is all a console program has.
    /// </summary>
    [Fact]
    public async Task A_program_record_from_a_start_step_can_be_waited_on_as_it_stands()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Running["tool"] = [7];
        devices.Processes.Exited.Add(7);
        devices.Processes.ExitCodes[7] = 3;

        var (result, _, store) = await RunAsync(
        [
            Step("process.start", Param("programFile", "tool.exe"), Returns("program=started")),
            Within(1000, Step("process.waitExit", Fields.Reference("window", "started"),
                Returns("exitCode=code"))),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(3d, store.Local.Values["code"].Number);
    }

    [Fact]
    public async Task Waiting_for_exit_gives_up_after_the_timeout()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Running["notepad"] = [42];
        var (result, _, _) = await RunAsync(
        [
            Within(100, Step("process.waitExit", Param("window", "notepad"),
                Param("how", WindowHow.ProcessEquals))),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ProcessTimeout", result.Key);
    }

    /// <summary>
    /// Waiting starts from a program that is there. A name that is not running is said out loud
    /// rather than answered as "already finished", which would make a typo look like success.
    /// </summary>
    [Fact]
    public async Task Waiting_for_exit_on_a_program_that_is_not_running_says_so()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("process.waitExit", Param("window", "ghost"), Param("how", WindowHow.ProcessEquals)),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NoSuchProcess", result.Key);
    }

    /// <summary>
    /// Unlike a window step, a program step has no "whatever is in front" to fall back on: which
    /// program is meant cannot be guessed, so naming none is refused.
    /// </summary>
    [Fact]
    public async Task A_program_step_with_no_program_named_is_refused()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync([Step("process.exists")], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingProgram", result.Key);
    }

    [Fact]
    public async Task A_program_step_whose_target_is_not_a_rule_is_refused()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            new ExecutableStep { Type = "process.exists", Parameters = [Param("window", "notepad")] },
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadWindowRule", result.Key);
    }

    [Fact]
    public async Task Running_a_command_keeps_its_output_and_exit_code()
    {
        var devices = new FakeDeviceLayer();
        devices.Processes.Command = new CommandResult(3, "hello", "watch out");
        var (_, _, store) = await RunAsync(
        [
            Within(5000, Step("command.run", Param("commandFile", "cmd.exe"),
                Param("commandArguments", "/c echo hello"), Returns("output=out"),
                Returns("error=err"), Returns("exitCode=code"))),
        ], devices);

        Assert.Equal("hello", store.Local.Values["out"].AsText());
        Assert.Equal("watch out", store.Local.Values["err"].AsText());
        Assert.Equal(3d, store.Local.Values["code"].Number);
        Assert.Contains(@"run cmd.exe|/c echo hello||5000|out default", devices.Calls);
    }
}
