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

// What a run leaves behind when it ends: the log file, its name, and the lines in it.
public partial class DeviceActionTests
{
    [Fact]
    public async Task A_run_that_stops_on_a_failure_leaves_its_log_behind()
    {
        var devices = new FakeDeviceLayer();
        var host = new SilentRunHost();

        var result = await new MacroRunner(new VariableStore(), host, devices)
        {
            RunName = "订单导出",
        }.RunAsync([Step("nope.unknown")]);

        Assert.Equal(RunStatus.Failed, result.Status);

        // The whole run is written as text into the log folder, under the macro's own name, in the
        // encoding a person can open by double-clicking.
        var written = Assert.Single(devices.Files.Store);
        Assert.StartsWith("logs", written.Key);
        Assert.Contains("failure-订单导出-", written.Key);
        Assert.EndsWith(".log", written.Key);
        Assert.Contains(devices.Calls,
            call => call.StartsWith("writeFile logs", StringComparison.Ordinal)
                && call.EndsWith(" False utf8bom", StringComparison.Ordinal));

        // Which macro it was and when come first, then every line the run wrote under its own time.
        var lines = written.Value.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Log.LogFileHeader 订单导出 ", lines[0]);
        Assert.Contains(lines, line => line.Contains("Log.Start", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Log.Aborted", StringComparison.Ordinal));
        Assert.All(lines.Skip(1), line => Assert.Matches(@"^\d\d:\d\d:\d\d  ", line));

        // The log says where it went, which is how the user finds it.
        Assert.Contains(host.Entries, entry => entry.Key == "Log.FailureLog");
    }

    [Fact]
    public async Task Lines_of_a_block_are_indented_by_their_depth()
    {
        var devices = new FakeDeviceLayer();

        await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
            .RunAsync([Step("control.repeat", Param("times", "1"), Body("body", Step("nope.unknown")))]);

        var lines = Assert.Single(devices.Files.Store).Value
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        // The step inside the block is one level in, which is two more spaces after the time.
        Assert.Contains(lines, line => line[10..].StartsWith("  Run.", StringComparison.Ordinal));
        Assert.Contains(lines, line => line[10..].StartsWith("Log.Start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_log_goes_to_the_folder_the_program_asks_for()
    {
        // The application hands in a folder of its own, so the logs land beside the program instead
        // of inside a project that somebody may well be sharing.
        var devices = new FakeDeviceLayer();
        var folder = Path.Combine("C:", "whalegenie", "logs");

        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
        {
            FailureLogFolder = folder,
        }.RunAsync([Step("nope.unknown")]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.StartsWith(folder, Assert.Single(devices.Files.Store).Key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_that_finishes_leaves_no_log()
    {
        var devices = new FakeDeviceLayer();

        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
            .RunAsync([Step("control.delay", Param("ms", "0"))]);

        Assert.True(result.Succeeded);
        Assert.Empty(devices.Files.Store);
    }

    [Fact]
    public async Task A_run_the_user_stopped_leaves_no_log()
    {
        var devices = new FakeDeviceLayer();

        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
            .RunAsync([Step("control.stop")]);

        Assert.Equal(RunStatus.Stopped, result.Status);
        Assert.Empty(devices.Files.Store);
    }

    [Fact]
    public async Task A_log_that_cannot_be_written_does_not_bring_the_run_down()
    {
        // Everything the fake is asked for is refused, so writing the log fails too. The run has
        // already failed, and recording that must not be able to make it worse.
        var devices = new FakeDeviceLayer { Unavailable = true };
        var host = new SilentRunHost();

        var result = await new MacroRunner(new VariableStore(), host, devices)
            .RunAsync([Step("nope.unknown")]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Empty(devices.Files.Store);
        Assert.Contains(host.Entries, entry => entry.Key == "Log.FailureLogFailed");
    }

    [Fact]
    public async Task A_macro_name_a_file_cannot_carry_is_made_into_one()
    {
        var devices = new FakeDeviceLayer();

        await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
        {
            RunName = "a/b:c*?d",
        }.RunAsync([Step("nope.unknown")]);

        Assert.Contains("failure-a_b_c__d-", Assert.Single(devices.Files.Store).Key);
    }

    [Fact]
    public async Task An_overlong_macro_name_is_cut_short_in_the_file_name()
    {
        var devices = new FakeDeviceLayer();

        await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
        {
            RunName = new string('x', 60),
        }.RunAsync([Step("nope.unknown")]);

        Assert.Contains("failure-" + new string('x', 40) + "-", Assert.Single(devices.Files.Store).Key);
    }

    [Fact]
    public async Task A_second_log_of_the_same_second_is_written_beside_the_first()
    {
        var devices = new FakeDeviceLayer();

        // Two logs in one second would take the same name. The clock cannot be moved, so the timing
        // is waited out instead: the seeding and the run after it have to fall inside one second,
        // and starting just after a second began leaves the whole of it for them.
        while (DateTime.Now.Millisecond > 250)
        {
            await Task.Delay(20);
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var taken = Path.Combine("logs", $"failure-{stamp}.log");
        devices.Files.Store[taken] = "an older log";

        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
            .RunAsync([Step("nope.unknown")]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains(devices.Files.Store.Keys,
            path => path.EndsWith($"failure-{stamp}-1.log", StringComparison.Ordinal));
        Assert.Equal("an older log", devices.Files.Store[taken]);
    }

    /// <summary>
    /// The log goes through the real file device as well, so what it claims about its encoding is
    /// what lands on disk: a person double-clicking the file must read the macro's name and the
    /// steps it took, not a page of question marks where the writing was.
    /// </summary>
    [Fact]
    public async Task The_log_of_a_failed_run_is_a_text_file_a_person_can_open()
    {
        var folder = Directory.CreateTempSubdirectory("whalegenie-log-");
        try
        {
            var devices = new FakeDeviceLayer(new LocalFileDevice(folder.FullName));

            var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
            {
                FailureLogFolder = folder.FullName,
                RunName = "订单导出",
            }.RunAsync([Step("nope.unknown")]);

            Assert.Equal(RunStatus.Failed, result.Status);

            var which = Assert.Single(Directory.GetFiles(folder.FullName, "*.log"));
            Assert.Contains("failure-订单导出-", Path.GetFileName(which), StringComparison.Ordinal);

            // UTF-8 with a byte-order mark, which is what a double-click has to be able to read.
            var bytes = File.ReadAllBytes(which);
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

            var text = Encoding.UTF8.GetString(bytes[3..]);
            Assert.StartsWith("Log.LogFileHeader 订单导出 ", text, StringComparison.Ordinal);
            Assert.Contains("Log.Aborted", text, StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
