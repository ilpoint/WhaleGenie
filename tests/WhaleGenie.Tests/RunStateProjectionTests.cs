using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
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

        // The five pieces of debugging data the panel gained read as "nothing yet" rather than as
        // null: the page draws them without asking whether a session was ever loaded.
        Assert.Equal(0, state.InputRate);
        Assert.Equal(string.Empty, state.InputRateOwner);
        Assert.Equal(string.Empty, state.SwapNote);
        Assert.NotNull(state.Triggers);
        Assert.Empty(state.Triggers!);
        Assert.NotNull(state.Watches);
        Assert.Empty(state.Watches!);
        Assert.NotNull(state.Looks);
        Assert.Empty(state.Looks!);
        Assert.Equal(string.Empty, state.ActiveLook);
        Assert.NotNull(state.BreakpointChoices);
        Assert.Empty(state.BreakpointChoices!);
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

    /// <summary>
    /// A step that has run says how it ended and what it worked out, and a step that has not says
    /// neither: an empty row is what the page draws nothing for, so an invented word would be a
    /// claim about a step that has not happened.
    /// </summary>
    [Fact]
    public void A_step_that_has_run_says_how_it_ended_and_what_it_worked_out()
    {
        Ui.Run(() =>
        {
            var run = new RunViewModel([], NullDeviceLayer.Instance);
            var ran = new RunStepViewModel { Id = "s1", Label = "Find", Detail = "control.findImage", Depth = 0, CanBreak = true };
            var waiting = new RunStepViewModel { Id = "s2", Label = "Click", Detail = "input.click", Depth = 0, CanBreak = true };
            run.Steps.Add(ran);
            run.Steps.Add(waiting);

            ran.Took(new StepRun
            {
                StepId = "s1",
                StepType = "control.findImage",
                Outcome = "ok",
                ElapsedMs = 12,
                Results = [new StepResult("hit", "found", "12, 34")],
            });

            var steps = RunStateProjection.Project(run, null).Steps!;

            Assert.Equal(ran.OutcomeLabel, steps[0].Outcome);
            Assert.NotEqual(string.Empty, steps[0].Outcome);
            Assert.Contains("found", steps[0].Result, StringComparison.Ordinal);
            Assert.Equal(string.Empty, steps[1].Outcome);
            Assert.Equal(string.Empty, steps[1].Result);
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

    /// <summary>
    /// The frames a session kept travel with the state, and the one on show is named by its step so
    /// the page knows which of the thumbnails it is drawing large.
    /// </summary>
    [Fact]
    public void The_frames_a_session_kept_travel_with_the_one_on_show()
    {
        Ui.Run(() =>
        {
            var run = new RunViewModel([], NullDeviceLayer.Instance);
            var first = new RunLook("s1", "control.findImage", "screen", "data:image/bmp;base64,AA==");
            var second = new RunLook("s2", "control.findImage", "window", "data:image/bmp;base64,BB==");

            var state = RunStateProjection.Project(run, second, [first, second]);

            var looks = state.Looks!;
            Assert.Equal(2, looks.Count);
            Assert.Same(first, looks[0]);
            Assert.Same(second, looks[1]);
            Assert.Equal("s2", state.ActiveLook);
            Assert.Equal(string.Empty, RunStateProjection.Project(run, null, []).ActiveLook);
        });
    }

    /// <summary>
    /// Why the panel left one macro for another is kept until the next run starts, and the rate and
    /// the run holding the input door are read beside it: the three answers to "why did my macro
    /// not do what I meant" are all here.
    /// </summary>
    [Fact]
    public void The_swap_note_the_rate_and_the_door_come_from_the_model()
    {
        var (note, rate, owner, carried) = Ui.RunAsync(async () =>
        {
            var slow = new MacroItem { Name = "slow" };
            slow.Steps.Add(new MacroStep
            {
                Type = "control.delay",
                Parameters = [new StepParameter
                {
                    Name = "ms",
                    Kind = ActionParameterKind.Number,
                    Written = Formula.Number(400),
                }],
            });
            var other = new MacroItem { Name = "other" };
            StepIds.Settle([.. slow.Steps]);

            var run = new RunViewModel([], NullDeviceLayer.Instance);
            await run.DebugAsync(slow);
            run.RunCommand.Execute(null);
            while (!run.IsBusy)
            {
                await Task.Delay(1);
            }

            await run.DebugAsync(other);

            return (run.SwapNote, run.InputRate, run.InputRateOwner, RunStateProjection.Project(run, null).SwapNote);
        });

        Assert.Equal(Strings.Format("Run.DebugSwitched", "slow", "other"), note);
        Assert.Equal(note, carried);
        Assert.Equal(0, rate);
        Assert.Equal(string.Empty, owner);
    }

    /// <summary>
    /// What the macro watches and the names a breakpoint may ask about are read off the macro, so the
    /// page offers the names that exist rather than the ones a reader remembers.
    /// </summary>
    [Fact]
    public void The_watched_values_and_the_breakpoint_choices_come_from_the_macro()
    {
        var (watches, choices, values) = Ui.RunAsync(async () =>
        {
            var step = new MacroStep
            {
                Type = "control.setVariable",
                Parameters =
                [
                    new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Written = Fields.Shape("count") },
                    new StepParameter { Name = "value", Kind = ActionParameterKind.Text, Written = Formula.Number(3) },
                ],
            };
            StepIds.Settle([step]);

            var run = new RunViewModel([step], NullDeviceLayer.Instance);
            run.RunCommand.Execute(null);
            while (run.IsBusy)
            {
                await Task.Delay(10);
            }

            var state = RunStateProjection.Project(run, null);
            return (state.Watches!, state.BreakpointChoices!, run.Variables);
        });

        var count = Assert.Single(watches, watch => watch.Name == "count");
        Assert.Equal("3", count.Value);
        Assert.Equal(values.First(variable => variable.Name == "count").LocalType, count.Type);
        Assert.NotEmpty(choices);

        // Every step that runs leaves its ending behind, so that name is always one a condition can
        // ask about.
        Assert.Contains(choices, name => name.EndsWith(".outcome", StringComparison.Ordinal));
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

            // What a step came to is on its row, so a step that has just run is a tree change: a push
            // that left it out would draw the row as it was before the step began.
            row.Took(new StepRun
            {
                StepId = "s1",
                StepType = "control.delay",
                Outcome = "ok",
                ElapsedMs = 500,
                Results = [new StepResult("key", string.Empty, "value")],
            });
            var ran = Signature();
            Assert.NotEqual(initial, ran);

            row.IsCurrent = true;
            Assert.NotEqual(ran, Signature());
        });
    }
}
