using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A step that cannot be put back is asked about before it is reached, and a refusal is that step's
/// own failure so the rule written on the step decides what happens next. Which steps those are is
/// said once in the catalogue, so the editor, the run window and a macro a trigger started all come
/// to the same answer.
/// </summary>
public class IrreversibleStepTests
{
    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Id = "aaaa", Parameters = parameters };

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    private static ExecutableParameter Body(string name, params ExecutableStep[] steps)
        => new() { Name = name, Steps = steps };

    [Fact]
    public async Task A_step_that_cannot_be_undone_does_nothing_when_the_person_says_no()
    {
        var devices = new FakeDeviceLayer();
        var host = new ConfirmingHost(agree: false);

        var result = await new MacroRunner(new VariableStore(), host, devices)
            .RunAsync([Step("system.power", Param("powerAction", "shutDown"))]);

        Assert.Equal(RunKeys.NotConfirmed, result.Key);

        // Nothing was asked of the machine: the question is not a formality on the way to doing it.
        // The file a failed run leaves behind is the run's own ending rather than a request of the
        // step, so it is not counted here.
        Assert.DoesNotContain(devices.Calls,
            call => !call.Contains(@"logs\failure-", StringComparison.Ordinal));
        Assert.Equal(1, host.Asked);
    }

    [Fact]
    public async Task A_loop_asks_once_and_runs_every_round()
    {
        var devices = new FakeDeviceLayer();
        var host = new ConfirmingHost(agree: true);

        var result = await new MacroRunner(new VariableStore(), host, devices).RunAsync(
        [
            Step("control.repeat", Param("times", "3"),
                Body("body", Step("system.power", Param("powerAction", "monitorOff")))),
        ]);

        Assert.True(result.Succeeded);

        // Monitor off is reversible, so this loop is not asked about at all.
        Assert.Equal(0, host.Asked);
        Assert.Equal(3, devices.Calls.Count);

        var asked = new ConfirmingHost(agree: true);
        await new MacroRunner(new VariableStore(), asked, devices).RunAsync(
        [
            Step("control.repeat", Param("times", "3"),
                Body("body", Step("file.delete", Param("path", "old.txt")))),
        ]);

        // One step repeated three times is one question: the question is about the step.
        Assert.Equal(1, asked.Asked);
    }

    [Fact]
    public async Task Locking_the_screen_is_not_asked_about()
    {
        var devices = new FakeDeviceLayer();
        var host = new ConfirmingHost(agree: false);

        var result = await new MacroRunner(new VariableStore(), host, devices)
            .RunAsync([Step("system.power", Param("powerAction", "lock"))]);

        Assert.True(result.Succeeded);
        Assert.Equal(0, host.Asked);
        Assert.Equal(["power Lock 0"], devices.Calls);
    }

    [Fact]
    public async Task A_step_with_nobody_to_ask_does_not_run()
    {
        // A triggered run with no window to ask in, and the machine-facing tools, reach a host that
        // answers no. Saying nothing is not the same as saying yes.
        var devices = new FakeDeviceLayer();

        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
            .RunAsync([Step("file.deleteFolder", Param("path", "old"), Param("recurse", "true"))]);

        Assert.Equal(RunKeys.NotConfirmed, result.Key);
        Assert.DoesNotContain(devices.Calls,
            call => !call.Contains(@"logs\failure-", StringComparison.Ordinal));
    }

    /// <summary>Answers the "cannot be undone" question and counts how often it was put.</summary>
    private sealed class ConfirmingHost(bool agree) : IRunHost
    {
        public int Asked { get; private set; }

        public void Log(LogEntry entry)
        {
        }

        public string Text(LogEntry entry) => entry.Key;

        public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
            => Task.FromResult(true);

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
            => Task.FromResult(StepErrorChoice.Stop);

        public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
            => Task.FromResult(AskAnswer.Cancel);

        public Task<bool> ConfirmIrreversible(ExecutableStep step, CancellationToken token)
        {
            Asked++;
            return Task.FromResult(agree);
        }
    }
}
