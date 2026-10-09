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
}
