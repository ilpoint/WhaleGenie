using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The run window's list of how each step went. It is a list of its own, folded away until it is
/// wanted: a macro has one of these for every step of it, so mixed in with the macro's own values
/// it would be the only thing anybody could see.
/// </summary>
public class RunStepOutcomeTests
{
    [Fact]
    public void Every_step_the_run_went_past_is_listed_under_its_own_heading()
    {
        var (count, has, folded, header, name, wanted, value) = Ui.RunAsync(async () =>
        {
            var step = new MacroStep
            {
                Type = "control.setVariable",
                Parameters =
                [
                    new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = "n" },
                    new StepParameter { Name = "value", Kind = ActionParameterKind.Text, Value = "1" },
                ],
            };
            StepIds.Settle([step]);

            var viewModel = new RunViewModel([step], NullDeviceLayer.Instance);
            var wasFolded = viewModel.ShowStepOutcomes;

            viewModel.RunCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            return (viewModel.StepOutcomes.Count, viewModel.HasStepOutcomes, wasFolded,
                viewModel.StepOutcomeHeader, viewModel.StepOutcomes[0].Name,
                MacroRunner.OutcomeName(step.Id), viewModel.StepOutcomes[0].Value);
        });

        Assert.Equal(1, count);
        Assert.True(has);
        Assert.False(folded);
        Assert.Contains("1", header, StringComparison.Ordinal);
        Assert.Equal(wanted, name);
        Assert.Equal("ok", value);
    }

    [Fact]
    public void Nothing_is_listed_until_the_run_has_answered_for_a_step()
    {
        var (count, header) = Ui.Run(() =>
        {
            var step = new MacroStep { Type = "control.delay" };
            StepIds.Settle([step]);

            var viewModel = new RunViewModel([step], NullDeviceLayer.Instance);
            return (viewModel.StepOutcomes.Count, viewModel.StepOutcomeHeader);
        });

        Assert.Equal(0, count);
        Assert.Contains("0", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Why a step failed is a value the macro can read: the step that comes after it names
    /// <c>$step.&lt;id&gt;.error</c> and gets the engine's own name for the failure, which is what
    /// lets a macro tell one kind of failure from another without reading the log.
    /// </summary>
    [Fact]
    public void Why_a_step_failed_is_a_value_the_steps_after_it_can_read()
    {
        var why = Ui.RunAsync(async () =>
        {
            var broken = new MacroStep
            {
                Type = "input.keyPress",
                Parameters =
                [
                    new StepParameter { Name = "key", Kind = ActionParameterKind.Text, Value = "F5" },
                ],
                Meta = new StepMeta { OnError = StepErrorAction.Continue },
            };

            // Named first, because the step after it names the failed step's own reason.
            StepIds.Settle([broken]);

            var reads = new MacroStep
            {
                Type = "control.setVariable",
                Parameters =
                [
                    new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = "why" },
                    new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Value = "local" },
                    new StepParameter
                    {
                        Name = "value",
                        Kind = ActionParameterKind.Text,
                        Value = "$step." + broken.Id + ".error",
                    },
                ],
            };

            StepIds.Settle([broken, reads]);

            var viewModel = new RunViewModel([broken, reads], NullDeviceLayer.Instance);
            viewModel.RunCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            return (Ending: viewModel.StepOutcomes[0].Value,
                Reason: viewModel.Variables.FirstOrDefault(row => row.Name == "why")?.Value);
        });

        Assert.Equal("failed", why.Ending);
        Assert.Equal("Run.NoDevice", why.Reason);
    }
}
