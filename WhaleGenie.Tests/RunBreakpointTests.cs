using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The breakpoint's own menu, driven the way a user drives it: the dot on a step's row is
/// right-clicked, and what the menu offers is really opened, filled in and taken up. A menu item
/// pointing at the wrong handler looks right in the markup and does nothing when it is clicked,
/// so the wiring is checked here rather than read.
/// </summary>
public class RunBreakpointTests
{
    [Fact]
    public void The_condition_written_here_arms_the_breakpoint_with_it()
    {
        Ui.Run(() =>
        {
            var (window, viewModel, row) = Open(Set("n", "1"));

            Click(window, row, "condition");

            // The form is the one the editor writes conditions in, held to the conditions: what a
            // breakpoint waits for and what an if waits for are the same question.
            var dialog = window.OwnedWindows.OfType<AddActionWindow>().Single();
            var form = (AddActionViewModel)dialog.DataContext!;
            Assert.All(form.AvailableActions,
                action => Assert.StartsWith("condition.", action.Key, System.StringComparison.Ordinal));

            form.SelectAction("condition.compare");
            Dispatcher.UIThread.RunJobs();
            form.Parameters.First(parameter => parameter.Definition.Name == "variable").Text = "$n";
            form.Parameters.First(parameter => parameter.Definition.Name == "operator").Option =
                form.Parameters.First(parameter => parameter.Definition.Name == "operator")
                    .Choices.First(choice => choice.Value == "greaterThan");
            form.Parameters.First(parameter => parameter.Definition.Name == "value").Text = "3";
            Assert.True(form.CanSave, form.ValidationMessage);
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            // The dialog is gone and the step's dot is on: setting a condition on a step that had
            // no breakpoint means "stop here when this holds".
            Assert.Empty(window.OwnedWindows.OfType<AddActionWindow>());
            Assert.True(row.HasBreakpoint);
            Assert.Equal("condition.compare", viewModel.BreakpointCondition(row.Id)!.Type);

            // And the row says what it waits for, so a run that goes past it is not a mystery.
            Assert.Contains("greaterThan", row.ConditionNote, System.StringComparison.Ordinal);
            Assert.True(row.HasCondition);

            // Opening it again shows what was written rather than a blank form.
            Click(window, row, "condition");
            var again = (AddActionViewModel)window.OwnedWindows.OfType<AddActionWindow>().Single().DataContext!;
            Assert.Equal("condition.compare", again.SelectedDefinition!.Key);
            Assert.Equal("$n", again.Parameters
                .First(parameter => parameter.Definition.Name == "variable").Text);
            again.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Clearing the condition leaves the breakpoint standing: it is back to stopping every
            // time it is reached.
            Click(window, row, "clear");
            Assert.Null(viewModel.BreakpointCondition(row.Id));
            Assert.True(row.HasBreakpoint);
            Assert.False(row.HasCondition);

            window.Close();
        });
    }

    /// <summary>
    /// A condition is asked by the step it belongs to rather than run on its own, so no run stops
    /// in front of one. Its row therefore has no dot: a breakpoint that can never fire is worse than
    /// none at all, and stopping before the question is asked is what the dot on the step itself
    /// does.
    /// </summary>
    [Fact]
    public void A_condition_row_has_no_breakpoint_dot()
    {
        Ui.Run(() =>
        {
            var (window, viewModel, step) = Open(new MacroStep
            {
                Type = "control.if",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "condition",
                        Kind = ActionParameterKind.Condition,
                        Condition = new MacroStep
                        {
                            Type = "condition.compare",
                            Parameters =
                            [
                                new StepParameter
                                {
                                    Name = "variable",
                                    Kind = ActionParameterKind.Text,
                                    Value = "$n",
                                },
                                new StepParameter
                                {
                                    Name = "operator",
                                    Kind = ActionParameterKind.Choice,
                                    Value = "equals",
                                },
                                new StepParameter
                                {
                                    Name = "value",
                                    Kind = ActionParameterKind.Text,
                                    Value = "1",
                                },
                            ],
                        },
                    },
                    new StepParameter
                    {
                        Name = "then",
                        Kind = ActionParameterKind.Steps,
                        Steps = [Set("m", "1")],
                    },
                ],
            });

            // The step, the question it asks, one under the other, and the step the answer runs.
            Assert.Equal(3, viewModel.Steps.Count);
            Assert.True(step.CanBreak);
            Assert.False(viewModel.Steps[1].CanBreak);
            Assert.True(viewModel.Steps[2].CanBreak);

            // And only the steps carry a dot on the window, not the question.
            var dots = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("Breakpoint") && button.IsEffectivelyVisible)
                .ToList();

            Assert.Equal(2, dots.Count);
            Assert.Contains(dots, dot => ReferenceEquals(dot.DataContext, step));
            Assert.DoesNotContain(dots, dot => ReferenceEquals(dot.DataContext, viewModel.Steps[1]));

            window.Close();
        });
    }

    /// <summary>
    /// The skip item belongs to a run that is standing in front of a step. Clicked at any other
    /// moment it must do nothing: leaving the flag set would leave out whichever step ran next.
    /// </summary>
    [Fact]
    public void Skipping_while_no_run_is_paused_leaves_the_next_step_alone()
    {
        var answer = Ui.RunAsync(async () =>
        {
            var (window, viewModel, row) = Open(Set("n", "1"), Set("m", "1"));

            Assert.False(viewModel.SkipStepCommand.CanExecute(null));
            Click(window, row, "skip");

            viewModel.RunCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            // A run says it has finished as soon as the last step is done, and what that step came
            // to reaches the rows a moment later, on this same thread. Reading the rows without
            // letting that arrive first is what made this check pass or fail on its own timing.
            Dispatcher.UIThread.RunJobs();

            return viewModel.Steps.Select(step => step.HasRun).ToList();
        });

        Assert.Equal([true, true], answer);
    }

    /// <summary>A run window on steps that do nothing but set a variable, with no devices behind it.</summary>
    private static (RunWindow Window, RunViewModel ViewModel, RunStepViewModel Row) Open(
        params MacroStep[] steps)
    {
        StepIds.Settle(steps);

        var window = new RunWindow(steps, NullDeviceLayer.Instance);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (RunViewModel)window.DataContext!;
        return (window, viewModel, viewModel.Steps[0]);
    }

    /// <summary>Right-clicks a step's breakpoint dot and picks one of the three things it offers.</summary>
    private static void Click(RunWindow window, RunStepViewModel row, string tag)
    {
        var dot = window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Classes.Contains("Breakpoint")
                && ReferenceEquals(button.DataContext, row));

        dot.ContextMenu!.Open(dot);
        Dispatcher.UIThread.RunJobs();

        var item = dot.ContextMenu.Items.OfType<MenuItem>()
            .Single(entry => Equals(entry.Tag, tag));

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A step that sets a variable, so a run has something to do that cannot fail.</summary>
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
