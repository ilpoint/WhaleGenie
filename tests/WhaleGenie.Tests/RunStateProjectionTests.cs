using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The state the run page draws, read from the run model without a bridge or a window: every field
/// the page paints comes from the model, and a session that was never loaded reads as the empty
/// panel rather than as a missing answer.
/// </summary>
public class RunStateProjectionTests
{
    [Fact]
    public void An_unloaded_session_projects_the_empty_panel()
    {
        var state = RunStateProjection.Project(null, null);

        Assert.Equal(string.Empty, state.Loaded);
        Assert.Equal(string.Empty, state.Status);
        Assert.Equal(string.Empty, state.StatusKind);
        Assert.False(state.Busy);
        Assert.False(state.Paused);
        Assert.False(state.CanRun);
        Assert.False(state.CanStep);
        Assert.False(state.CanStop);
        Assert.Equal(string.Empty, state.Question);
        Assert.False(state.WantsTypedAnswer);
        Assert.Equal(string.Empty, state.AnswerText);
        Assert.False(state.IsAskingUser);
        Assert.False(state.IsAsking);
        Assert.Empty(state.Arguments);
        Assert.Null(state.Look);
        Assert.Empty(state.Globals);
        Assert.Empty(state.Locals);
        Assert.NotNull(state.Steps);
        Assert.Empty(state.Steps!);
        Assert.Empty(state.Log);
        Assert.Equal(0, state.LogFrom);
    }

    [Fact]
    public void A_loaded_run_projects_its_status_and_commands()
    {
        Ui.Run(() =>
        {
            var run = new RunViewModel([], NullDeviceLayer.Instance);

            var state = RunStateProjection.Project(run, null);

            Assert.Equal(run.Debugged, state.Loaded);
            Assert.Equal(run.Status, state.Status);
            Assert.Equal(run.StatusKindValue.ToString(), state.StatusKind);
            Assert.Equal(run.IsBusy, state.Busy);
            Assert.Equal(run.IsPaused, state.Paused);
            Assert.Equal(run.RunCommand.CanExecute(null), state.CanRun);
            Assert.Equal(run.StepCommand.CanExecute(null), state.CanStep);
            Assert.Equal(run.StopCommand.CanExecute(null), state.CanStop);
            Assert.Equal(run.Question, state.Question);
            Assert.Equal(run.WantsTypedAnswer, state.WantsTypedAnswer);
            Assert.Equal(run.AnswerText, state.AnswerText);
            Assert.Equal(run.IsAskingUser, state.IsAskingUser);
            Assert.Equal(run.IsAsking, state.IsAsking);
            Assert.Empty(state.Steps!);
            Assert.Empty(state.Log);
        });
    }

    [Fact]
    public void Every_step_field_the_page_draws_comes_from_its_row()
    {
        Ui.Run(() =>
        {
            var run = new RunViewModel([], NullDeviceLayer.Instance);
            var row = new RunStepViewModel
            {
                Id = "s1",
                Label = "Wait",
                Detail = "control.delay · 500",
                Depth = 2,
                IsContainer = true,
                IsShown = false,
                CanBreak = false,
            };
            run.Steps.Add(row);
            run.SetBreakpointRule(row, new BreakpointRule
            {
                HitTimes = 3,
                Expression = Formula.Text("ready"),
            });
            row.IsCurrent = true;
            row.TimeNote = "0.5 s";
            row.FailureNote = "Run.Timeout";
            row.ConditionNote = "ready";

            var step = Assert.Single(RunStateProjection.Project(run, null).Steps!);

            Assert.Equal("s1", step.Id);
            Assert.Equal("Wait", step.Label);
            Assert.Equal("control.delay · 500", step.Detail);
            Assert.Equal(2, step.Depth);
            Assert.False(step.Shown);
            Assert.True(step.Container);
            Assert.True(step.Current);
            Assert.True(step.Breakpoint);
            Assert.False(step.CanBreak);
            Assert.Equal("0.5 s", step.Time);
            Assert.Equal("Run.Timeout", step.Failure);
            Assert.Equal("ready", step.Condition);
            Assert.Equal(3, step.HitTimes);
            Assert.Equal("ready", step.Expression);
        });
    }

    [Fact]
    public void Log_lines_and_a_look_keep_their_own_fields()
    {
        Ui.Run(() =>
        {
            var run = new RunViewModel([], NullDeviceLayer.Instance);
            run.Lines.Add(new RunLineViewModel("12:00:01", LogLevel.Warn, "Slow step"));
            var look = new RunLook("s1", "control.findImage", "screen", "data:image/bmp;base64,AA==");

            var state = RunStateProjection.Project(run, look);
            var line = Assert.Single(state.Log);

            Assert.Equal("12:00:01", line.Time);
            Assert.Equal("Warn", line.Level);
            Assert.Equal("Slow step", line.Text);
            Assert.Same(look, state.Look);
            Assert.Equal("s1", state.Look!.StepId);
            Assert.Equal("control.findImage", state.Look.Type);
            Assert.Equal("screen", state.Look.Looking);
            Assert.Equal("data:image/bmp;base64,AA==", state.Look.Image);
            Assert.Null(RunStateProjection.Project(run, null).Look);
        });
    }

    [Fact]
    public void The_steps_signature_follows_the_tree_and_not_the_log_or_the_look()
    {
        Ui.Run(() =>
        {
            var run = new RunViewModel([], NullDeviceLayer.Instance);
            var row = new RunStepViewModel
            {
                Id = "s1",
                Label = "Wait",
                Detail = "control.delay",
                Depth = 0,
                CanBreak = true,
            };
            run.Steps.Add(row);

            string Signature() => RunStateProjection.StepsSignature(RunStateProjection.Project(run, null).Steps!);
            var initial = Signature();
            Assert.Equal(initial, Signature());

            run.Lines.Add(new RunLineViewModel("12:00:01", LogLevel.Info, "line"));
            Assert.Equal(initial, Signature());

            row.IsCurrent = true;
            Assert.NotEqual(initial, Signature());
        });
    }
}
