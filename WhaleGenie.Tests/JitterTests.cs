using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The give a length of time can be written with: how it is typed, what the step stores, what the
/// step list says about it, and that it reaches the engine. Everything that is not a length of
/// time goes on using its value exactly as written.
/// </summary>
public class JitterTests
{
    private static AddActionViewModel Open(string key)
    {
        var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction(key);
        Dispatcher.UIThread.RunJobs();
        return viewModel;
    }

    private static StepParameterViewModel Editor(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == name);

    [Fact]
    public void A_length_of_time_carries_the_box_that_gives_it_slack()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.mouseClick");
            Dispatcher.UIThread.RunJobs();

            // A box carried by a parameter that is not a length of time is still in the tree, just
            // out of sight, so what counts is the ones actually drawn.
            var boxes = window.GetVisualDescendants().OfType<NumericUpDown>()
                .Where(control => control.Name == "JitterBox" && control.IsEffectivelyVisible)
                .ToList();
            var times = viewModel.Parameters.Where(parameter => parameter.IsDuration).ToList();

            // One for each length of time the step carries, and they start empty: a step only
            // moves when it has been told to.
            Assert.NotEmpty(times);
            Assert.Equal(times.Count, boxes.Count);
            Assert.All(boxes, box => Assert.Contains((StepParameterViewModel)box.DataContext!, times));
            Assert.All(boxes, box => Assert.Null(box.Value));

            // A count is not a length of time: "click 3 times" means three times.
            Assert.False(Editor(viewModel, "clicks").IsDuration);
        });
    }

    [Fact]
    public void A_time_with_slack_says_what_it_will_really_wait()
    {
        Ui.Run(() =>
        {
            var millis = Editor(Open("control.delay"), "ms");
            millis.NumberValue = 500;

            // Nothing to say while the wait is fixed.
            Assert.False(millis.HasJitterRange);

            millis.JitterPercent = 20;

            // 500 ±20% is written out, rather than left for the user to work out in their head.
            Assert.True(millis.HasJitterRange);
            Assert.Contains(DurationUnit.Written(400), millis.JitterRange);
            Assert.Contains(DurationUnit.Written(600), millis.JitterRange);

            // A field with nothing in it has no range to talk about.
            millis.NumberValue = null;
            Assert.False(millis.HasJitterRange);
        });
    }

    [Fact]
    public void A_time_with_slack_keeps_it_through_saving_and_reopening()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.delay");
            Editor(viewModel, "ms").JitterPercent = 20;

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            var stored = saved!.Parameters.First(parameter => parameter.Name == "ms");

            // A fraction, not a percentage: the file reads the same in every language, and the
            // number in it is the proportion of the wait rather than a unit someone has to guess.
            Assert.Equal(0.2m, stored.Jitter);
            Assert.Equal(0.2m, saved.ToJson()["jitter"]!["ms"]!.GetValue<decimal>());

            // The step list says so as well, so a wait with slack is visible without opening it.
            Assert.Contains(Strings.Format("Editor.JitterDetail", "20"), saved.Detail);

            var reopened = new AddActionWindow(saved, ActionCatalog.Definitions, [], []);
            reopened.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(20m, Editor((AddActionViewModel)reopened.DataContext!, "ms").JitterPercent);
        });
    }

    [Fact]
    public void A_step_written_before_the_slack_existed_reads_back_unchanged()
    {
        var step = MacroStep.FromJson(JsonNode.Parse(
            """
            { "type": "control.delay", "params": { "ms": 500 } }
            """)!.AsObject());

        Assert.False(Assert.Single(step.Parameters).HasJitter);

        // And a step without slack does not grow a place for it on the way out.
        Assert.False(step.ToJson().ContainsKey("jitter"));
    }

    [Fact]
    public void The_slack_of_a_time_reaches_the_engine_with_the_step()
    {
        var step = new MacroStep
        {
            Type = "control.delay",
            Parameters =
            [
                new StepParameter
                {
                    Name = "ms",
                    Kind = ActionParameterKind.Number,
                    Value = "500",
                    Jitter = 0.2m,
                },
            ],
        };

        // The runnable step carries it, which is what the runner bends the wait by.
        Assert.Equal(0.2m, Assert.Single(new[] { step }.ToExecutable()).Jitter("ms"));
    }

    [Fact]
    public void The_slack_of_a_time_survives_the_macro_package()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var package = Path.Combine(folder, "slack.wgmacro");
            var macro = new MacroItem { Name = "Waits about half a second" };
            macro.Steps.Add(new MacroStep
            {
                Type = "control.delay",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "ms",
                        Kind = ActionParameterKind.Number,
                        Value = "500",
                        Jitter = 0.2m,
                    },
                ],
            });

            MacroPackage.Save(package, [macro], [], "test");

            // The package is where a macro actually lives, so the give has to come back out of it.
            var loaded = MacroPackage.Load(package);
            var step = Assert.Single(Assert.Single(loaded.Macros).Steps);

            Assert.Equal(0.2m, Assert.Single(step.Parameters).Jitter);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
