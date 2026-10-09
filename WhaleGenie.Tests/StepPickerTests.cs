using Avalonia.Threading;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// A field that names a step of the macro. The name is four characters nobody should have to copy
/// by eye, so it is picked out of a list, and each entry says which step it is by its note, its
/// action and its name.
/// </summary>
public class StepPickerTests
{
    private static MacroStep Step(string id, string type, string comment = "")
        => new()
        {
            Type = type,
            Id = id,
            Meta = new StepMeta { Comment = comment },
        };

    private static (AddActionViewModel ViewModel, AddActionWindow Window) Open(
        MacroStep? editing, IReadOnlyList<ActionParameterOption> steps)
    {
        var window = new AddActionWindow(editing, ActionCatalog.Conditions, [], [], null, null, steps);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        return (viewModel, window);
    }

    [Fact]
    public void A_step_is_offered_by_its_note_its_action_and_its_name()
    {
        Ui.Run(() =>
        {
            var choices = new List<ActionParameterOption>
            {
                new("k3f9", Step("k3f9", "control.log", "把这一行写进日志").PickerLabel),
            };

            var (viewModel, window) = Open(null, choices);
            viewModel.SelectAction("condition.stepResult");
            Dispatcher.UIThread.RunJobs();

            var field = viewModel.Parameters.First(parameter => parameter.Definition.Name == "step");

            Assert.True(field.IsStepChoice);
            Assert.False(field.IsChoice);
            Assert.Single(field.Choices);
            Assert.Contains("k3f9", field.Choices[0].Display, StringComparison.Ordinal);
            Assert.Contains("把这一行写进日志", field.Choices[0].Display, StringComparison.Ordinal);

            // Nothing is picked to start with, and a required field that names a step cannot be
            // left that way.
            Assert.Null(field.Option);
            Assert.True(field.IsMissing);
            field.Option = field.Choices[0];
            Assert.Equal("k3f9", field.CurrentText);

            window.Close();
        });
    }

    [Fact]
    public void The_step_being_edited_is_not_offered_as_something_it_can_ask_about()
    {
        Ui.Run(() =>
        {
            var choices = new List<ActionParameterOption>
            {
                new("aaaa", Step("aaaa", "control.log", "第一步").PickerLabel),
                new("bbbb", Step("bbbb", "control.log", "第二步").PickerLabel),
            };

            // The step with this name is the one being written, so asking about it would always
            // be asking before it has an ending of its own.
            var (viewModel, window) = Open(
                new MacroStep
                {
                    Type = "condition.stepResult",
                    Id = "aaaa",
                    Parameters =
                    [
                        new StepParameter { Name = "step", Kind = ActionParameterKind.Step, Value = "" },
                    ],
                },
                choices);
            var field = viewModel.Parameters.First(parameter => parameter.Definition.Name == "step");

            Assert.Single(field.Choices);
            Assert.Equal("bbbb", field.Choices[0].Value);

            window.Close();
        });
    }

    [Fact]
    public void A_name_that_is_not_in_the_macro_is_shown_rather_than_replaced()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(
                new MacroStep
                {
                    Type = "condition.stepResult",
                    Parameters =
                    [
                        new StepParameter { Name = "step", Kind = ActionParameterKind.Step, Value = "zzzz" },
                    ],
                },
                ActionCatalog.Conditions, [], [], null, null,
                [new ActionParameterOption("aaaa", Step("aaaa", "control.log").PickerLabel)]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            var field = viewModel.Parameters.First(parameter => parameter.Definition.Name == "step");

            Assert.Equal("zzzz", field.CurrentText);
            Assert.Equal(2, field.Choices.Count);

            window.Close();
        });
    }
}
