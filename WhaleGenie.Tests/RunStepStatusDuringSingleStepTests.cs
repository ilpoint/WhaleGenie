using System;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// What the run window's step status says while the run is paused in front of a step. Every step is
/// written as "skipped" before the run starts, so a list built from "this step has a value" shows
/// the whole macro as skipped at the first pause — including steps the run may never reach.
/// </summary>
public class RunStepStatusDuringSingleStepTests
{
    [Fact]
    public void Single_stepping_lists_only_the_steps_the_run_has_gone_past()
    {
        var (atFirstPause, atSecondPause, atEnd) = Ui.RunAsync(async () =>
        {
            var steps = new[]
            {
                Set("n", "1"),
                Set("n", "2"),
                Set("n", "3"),
            };
            StepIds.Settle(steps);

            var viewModel = new RunViewModel(steps, NullDeviceLayer.Instance);

            viewModel.StepCommand.Execute(null);
            await Paused(viewModel);
            var first = viewModel.StepOutcomes.Select(row => row.Value).ToList();

            viewModel.StepCommand.Execute(null);
            await Paused(viewModel);
            var second = viewModel.StepOutcomes.Select(row => row.Value).ToList();

            viewModel.ContinueCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            return (first, second, viewModel.StepOutcomes.Select(row => row.Value).ToList());
        });

        // Paused in front of the first step: nothing has run, so nothing is reported — the old list
        // said "skipped" for all three.
        Assert.Empty(atFirstPause);

        // One step behind it: exactly that step, and it worked.
        Assert.Equal(["ok"], atSecondPause);

        // And once the run is over, every step it went past.
        Assert.Equal(["ok", "ok", "ok"], atEnd);
    }

    private static async Task Paused(RunViewModel viewModel)
    {
        for (var waited = 0; waited < 5000 && !viewModel.IsPaused; waited += 10)
        {
            await Task.Delay(10);
        }

        Assert.True(viewModel.IsPaused, "the run did not stop in front of a step");
    }

    private static MacroStep Set(string name, string value) => new()
    {
        Type = "control.setVariable",
        Parameters =
        [
            new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = name },
            new StepParameter { Name = "scope", Kind = ActionParameterKind.Text, Value = "local" },
            new StepParameter { Name = "value", Kind = ActionParameterKind.Text, Value = value },
        ],
    };
}
