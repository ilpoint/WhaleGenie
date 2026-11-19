using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The script family on its own: the pretend devices answer for the interpreter and the process,
/// so running a command and letting a script reach back into the macro can be checked without
/// starting one.
/// </summary>
public class ScriptActionTests
{
    [Fact]
    public async Task Running_a_command_hands_over_what_it_printed()
    {
        var context = new FakeRunContext();
        context.Machine.Processes.Command = new CommandResult(0, "hello\r\n", string.Empty);

        await Run(context, "script.run", Step("script.run",
            Fields.Text("script", "echo hello"), Fields.Text("kind", "cmdhidden"),
            SampleFiles.Returns("output", "error", "exitCode", "success")));

        Assert.Equal("hello", context.Written["output"].AsText());
        Assert.Equal(0, context.Written["exitCode"].AsNumber());
        Assert.True(context.Written["success"].AsBool());
    }

    [Fact]
    public async Task A_command_that_ends_badly_fails_the_step_with_what_it_said()
    {
        var context = new FakeRunContext();
        context.Machine.Processes.Command = new CommandResult(3, string.Empty, "no such file");

        var failure = await Assert.ThrowsAsync<StepFailure>(() => Run(context, "script.run",
            Step("script.run", Fields.Text("script", "dir missing"), Fields.Text("kind", "cmdhidden"),
                SampleFiles.Returns("output", "error", "exitCode", "success"))));

        Assert.Equal(RunKeys.ScriptFailed, failure.Key);
        Assert.Contains("3", failure.Detail, System.StringComparison.Ordinal);
        Assert.Contains("no such file", failure.Detail, System.StringComparison.Ordinal);
        Assert.Equal(3, context.Written["exitCode"].AsNumber());
        Assert.False(context.Written["success"].AsBool());
    }

    [Fact]
    public async Task A_script_reaches_the_macros_values_and_its_steps()
    {
        var context = new FakeRunContext();
        context.Machine.Script.PythonBody = (host, _) =>
        {
            Assert.True(host.RunStep("k3f9"));
            host.Write("answer", Value.FromNumber(42));
        };
        context.Machine.Script.Python = new ScriptResult(0, "done", string.Empty,
            Value.FromText("fine"));

        await Run(context, "script.python", Step("script.python",
            Fields.Text("script", "print(1)"),
            SampleFiles.Returns("value", "error", "exitCode", "success")));

        Assert.Equal(("k3f9", "script.python"), Assert.Single(context.StepsRun));
        Assert.True(((IRunContext)context).Variables.TryGet("answer", out var answer));
        Assert.Equal(42, answer.AsNumber());
        Assert.Equal("fine", context.Written["value"].AsText());
    }

    [Fact]
    public async Task A_step_a_script_runs_can_ask_the_macro_to_stop()
    {
        var context = new FakeRunContext();
        context.Machine.Script.PythonBody = (host, _) =>
        {
            host.RunStep("stop1");
            context.ScriptOutcome = Signal.Stop;
        };

        var signal = await Run(context, "script.python", Step("script.python",
            Fields.Text("script", "stop()"), SampleFiles.Returns("value")));

        Assert.Equal(Signal.Stop, signal);
    }

    [Fact]
    public async Task Asking_for_no_packages_is_refused_rather_than_installing_nothing()
    {
        var context = new FakeRunContext();

        var failure = await Assert.ThrowsAsync<StepFailure>(() => Run(context, "script.pip",
            Step("script.pip", Fields.Text("packages", "# nothing here"))));

        Assert.Equal(RunKeys.NoPackages, failure.Key);
    }

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => ScriptActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
