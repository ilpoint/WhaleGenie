using System.Threading;
using System.Threading.Tasks;
using Viktor.Core.Execution;
using Viktor.Execution;
using Viktor.ViewModels;

namespace Viktor.Tests;

/// <summary>
/// A macro a trigger started has no debugger behind it, but a step whose failure rule is "ask me"
/// still has to reach the person. These cover the host that carries the question out to the window
/// and the wording the debugger and the trigger share.
/// </summary>
public class FailedStepAskTests
{
    [Fact]
    public async Task The_question_goes_out_and_the_answer_comes_back()
    {
        var asked = new List<(string Step, string Reason, string Detail)>();
        var host = new AskRunHost((step, reason, detail) =>
        {
            asked.Add((step, reason, detail));
            return Task.FromResult(StepErrorChoice.Skip);
        });

        var choice = await host.Ask(
            "input.keyPress", "Run.NoDevice", "input.keyPress", CancellationToken.None);

        Assert.Equal(StepErrorChoice.Skip, choice);
        var question = Assert.Single(asked);
        Assert.Equal("input.keyPress", question.Step);
        Assert.Equal("Run.NoDevice", question.Reason);
        Assert.Equal("input.keyPress", question.Detail);
    }

    [Fact]
    public async Task With_nobody_to_ask_a_triggered_run_stops()
    {
        var host = new AskRunHost(null);

        var choice = await host.Ask("control.delay", "Run.Failed", "", CancellationToken.None);

        Assert.Equal(StepErrorChoice.Stop, choice);
    }

    [Fact]
    public async Task A_run_that_has_been_stopped_does_not_ask()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var asked = false;
        var host = new AskRunHost((_, _, _) =>
        {
            asked = true;
            return Task.FromResult(StepErrorChoice.Retry);
        });

        var choice = await host.Ask("control.delay", "Run.Failed", "", cancelled.Token);

        Assert.False(asked);
        Assert.Equal(StepErrorChoice.Stop, choice);
    }

    [Fact]
    public async Task A_step_that_asks_gets_its_answer_from_the_window()
    {
        var asked = 0;
        var host = new AskRunHost((_, _, _) =>
        {
            asked++;
            return Task.FromResult(StepErrorChoice.Skip);
        });

        // A key press with no device layer behind it is the failure the tests lean on: the engine
        // reports that the keyboard is missing.
        var broken = new ExecutableStep
        {
            Type = "input.keyPress",
            Parameters = [new ExecutableParameter { Name = "key", Text = "F5" }],
            Meta = new StepMeta { OnError = StepErrorAction.AskUser, RetryDelayMs = 0 },
        };

        var result = await new MacroRunner(MacroVariables.Seed(), host).RunAsync([broken]);

        Assert.Equal(1, asked);
        Assert.Equal(RunStatus.Completed, result.Status);
    }

    [Fact]
    public void The_question_names_the_step_and_fills_in_what_went_wrong()
    {
        var question = FailedStepPrompt.Question("input.keyPress", "Run.NoDevice", "keyboard");

        Assert.Contains(FailedStepPrompt.Name("input.keyPress"), question);
        Assert.Contains("keyboard", question);
        Assert.DoesNotContain("{0}", question);
    }
}
