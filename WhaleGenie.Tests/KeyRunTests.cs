using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// A run of key combinations is a list of presses rather than one long line of keys, so the dialog
/// has to build it, hand it to the engine, and open what was written back into rows.
/// </summary>
public class KeyRunTests
{
    private static AddActionViewModel Open(string key)
    {
        var viewModel = new AddActionViewModel(ActionCatalog.Definitions,
            VariableChoicesForChecks.Named("count"), []);
        viewModel.SelectAction(key);
        return viewModel;
    }

    private static StepParameterViewModel Run(AddActionViewModel viewModel)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == "keys");

    [Fact]
    public void The_run_the_dialog_built_reaches_the_engine()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keySequence");
            var run = Run(viewModel);

            run.AddKeyRow();
            run.KeyRows[0].AddKey("Ctrl");
            run.KeyRows[0].AddKey("A");
            run.AddKeyRow(new KeyRowViewModel { Keys = "B", HoldMs = 30m, GapMs = 0m });

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var rows = Assert.Single(new[] { saved! }.ToExecutable()).Rows("keys");
            Assert.Equal(2, rows.Count);
            Assert.Equal("Ctrl+A", rows[0]["keys"]);
            Assert.Equal("B", rows[1]["keys"]);
            Assert.Equal("30", rows[1]["holdMs"]);
            Assert.Equal("0", rows[1]["gapMs"]);
        });
    }

    [Fact]
    public void A_run_written_into_the_macro_opens_as_its_rows()
    {
        Ui.Run(() =>
        {
            var definition = ActionCatalog.Find("input.keySequence")!.Parameters
                .First(parameter => parameter.Name == "keys");
            var parameter = new StepParameterViewModel(definition);

            parameter.ApplyValue(new StepParameter
            {
                Name = "keys",
                Kind = ActionParameterKind.KeySequence,
                Rows =
                [
                    StepParameterRow.Of("keys", "Ctrl+A"),
                    new StepParameterRow
                    {
                        Columns = new Dictionary<string, string>
                        {
                            ["keys"] = "B",
                            ["holdMs"] = "30",
                        },
                    },
                ],
            });

            Assert.Equal(2, parameter.KeyRows.Count);
            Assert.Equal("Ctrl+A", parameter.KeyRows[0].Keys);
            Assert.Null(parameter.KeyRows[0].HoldMs);
            Assert.Equal(30m, parameter.KeyRows[1].HoldMs);
        });
    }

    /// <summary>
    /// The run is the one part of a step that is a list, so it goes into the macro file as a list of
    /// rows and has to come back out of it the same way: a run that is only in the editor is a run
    /// the user loses the moment the macro is saved and opened again.
    /// </summary>
    [Fact]
    public void A_run_survives_the_macro_file()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keySequence");
            var run = Run(viewModel);
            run.AddKeyRow(new KeyRowViewModel { Keys = "Ctrl+A" });
            run.AddKeyRow(new KeyRowViewModel { Keys = "B", HoldMs = 30m });

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var written = saved!.ToJson().ToJsonString();
            var back = MacroStep.FromJson((JsonObject)JsonNode.Parse(written)!);
            var rows = back.Parameters.First(parameter => parameter.Name == "keys").Rows;

            Assert.Equal(2, rows.Count);
            Assert.Equal("Ctrl+A", rows[0].Text("keys"));
            Assert.Equal("B", rows[1].Text("keys"));
            Assert.Equal("30", rows[1].Text("holdMs"));
        });
    }

    [Fact]
    public void The_dialog_adds_and_removes_a_press()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("count"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.keySequence");
            Dispatcher.UIThread.RunJobs();

            var run = Run(viewModel);
            FindButton(window, run, Strings.Get("Add.KeyRunAdd"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(run.KeyRows);

            var remove = FindButton(window, null, Strings.Get("Add.KeyRunRemove"));
            remove.Command!.Execute(remove.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(run.KeyRows);
        });
    }

    /// <summary>
    /// A run is what the step sends, so the step is ready to save once the run holds a press. The
    /// field is required and holds no text of its own — reading it as text would call every run
    /// empty and leave the step unsaveable however many presses were written into it.
    /// </summary>
    [Fact]
    public void A_run_is_ready_to_save_once_it_holds_a_press()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keySequence");
            var run = Run(viewModel);

            // A row that is there but says nothing is not a press yet.
            run.AddKeyRow();
            Assert.False(viewModel.CanSave);
            Assert.NotEqual(string.Empty, viewModel.ValidationMessage);

            run.KeyRows[0].AddKey("F5");
            Assert.True(viewModel.CanSave);
            Assert.Equal(string.Empty, viewModel.ValidationMessage);

            // Taking the last press out leaves nothing to send, which is missing again.
            run.RemoveKeyRow(run.KeyRows[0]);
            Assert.False(viewModel.CanSave);
            Assert.NotEqual(string.Empty, viewModel.ValidationMessage);
        });
    }

    /// <summary>
    /// A beat written into a row is part of what the step says, so the dialog has to hear about it:
    /// the row is not where the dialog is looking, the field is.
    /// </summary>
    [Fact]
    public void A_beat_written_into_a_row_reaches_the_step()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keySequence");
            var run = Run(viewModel);
            run.AddKeyRow(new KeyRowViewModel { Keys = "Ctrl+A" });

            // The run's own hold is a field of the step and is written either way; what is checked
            // here is the hold of one row, which only reaches the step once the row says it.
            Assert.DoesNotContain("\"holdMs\": 31", viewModel.JsonPreview);

            run.KeyRows[0].HoldMs = 31m;
            Assert.Contains("\"holdMs\": 31", viewModel.JsonPreview);
        });
    }

    private static Button FindButton(Window window, object? data, string content)
        => window.GetVisualDescendants().OfType<Button>().Single(candidate =>
            candidate.IsEffectivelyVisible
            && Equals(candidate.Content, content)
            && (data is null || Equals(candidate.DataContext, data)));
}
