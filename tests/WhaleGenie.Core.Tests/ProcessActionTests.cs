using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The program family on its own. The fake process table records what was started, what is running
/// and what a command printed, so each step's request can be read without a real program.
/// </summary>
public class ProcessActionTests
{
    [Fact]
    public async Task Starting_a_program_passes_its_fields_to_the_machine()
    {
        var context = new FakeRunContext();

        await Run(context, "process.start",
            ("programFile", "notepad.exe"),
            ("commandArguments", "hello.txt"),
            ("workingDirectory", @"C:\work"));

        Assert.Contains(context.Machine.Calls,
            call => call == @"start notepad.exe|hello.txt|C:\work|False");
    }

    [Fact]
    public async Task Listing_programs_hands_over_the_names_it_found()
    {
        var context = new FakeRunContext();
        context.Machine.Processes.Running["notepad"] = [42];
        context.Machine.Processes.Running["calc"] = [7];

        await Run(context, "process.list");

        Assert.Equal(["calc", "notepad"],
            context.Written["processes"].Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task Running_a_command_keeps_what_it_printed_and_what_it_returned()
    {
        var context = new FakeRunContext();
        context.Machine.Processes.Command = new CommandResult(3, "printed", "complained");

        await Run(context, "command.run", ("commandFile", "cmd.exe"));

        Assert.Equal("printed", context.Written["output"].AsText());
        Assert.Equal("complained", context.Written["error"].AsText());
        Assert.Equal(3, context.Written["exitCode"].AsNumber());
    }

    private static Task<Signal> Run(FakeRunContext context, string type,
        params (string Name, string Text)[] fields)
        => ProcessActions.Map[type](context, Step(type, fields), 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params (string Name, string Text)[] fields)
        => new()
        {
            Type = type,
            Parameters = [.. fields.Select(field => Fields.Text(field.Name, field.Text))],
        };
}
