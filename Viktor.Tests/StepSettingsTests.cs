using Avalonia.Threading;
using Viktor.Core.Execution;
using Viktor.Localization;
using Viktor.Models;
using Viktor.ViewModels;
using Viktor.Views;

namespace Viktor.Tests;

/// <summary>
/// The step-settings dialog, driven the way a user drives it: the window is built for real, and
/// what it hands back is what the engine reads. A step that is flaky needs to be retried, given
/// longer, or allowed to fail without stopping the macro, and none of that used to have anywhere
/// to be set.
/// </summary>
public class StepSettingsTests
{
    private static StepSettingsWindow Open(StepMeta meta)
    {
        var window = new StepSettingsWindow(new StepSettingsViewModel(meta));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    public void The_dialog_opens_with_the_settings_the_step_already_has()
    {
        Ui.Run(() =>
        {
            var window = Open(new StepMeta
            {
                Comment = "note",
                IsEnabled = false,
                DelayBeforeMs = 120,
                TimeoutMs = 4000,
                RetryCount = 2,
                RetryDelayMs = 250,
                RetryBackoff = RetryBackoff.Doubling,
                OnError = StepErrorAction.Continue,
            });

            var viewModel = (StepSettingsViewModel)window.DataContext!;
            Assert.Equal("note", viewModel.Comment);
            Assert.False(viewModel.IsStepEnabled);
            Assert.Equal(120, viewModel.DelayBeforeMs);
            Assert.Equal(4000, viewModel.TimeoutMs);
            Assert.Equal(2, viewModel.RetryCount);
            Assert.Equal(250, viewModel.RetryDelayMs);
            Assert.Equal((int)RetryBackoff.Doubling, viewModel.BackoffIndex);
            Assert.Equal((int)StepErrorAction.Continue, viewModel.FailureIndex);

            // The two pickers list the rules in the order the engine's own enums are written in,
            // which is what lets an index stand in for the setting.
            Assert.Equal(4, viewModel.FailureOptions.Count);
            Assert.Equal(3, viewModel.BackoffOptions.Count);
        });
    }

    [Fact]
    public void Confirming_hands_back_the_settings_the_engine_reads()
    {
        Ui.Run(() =>
        {
            var window = Open(StepMeta.Empty);
            var viewModel = (StepSettingsViewModel)window.DataContext!;
            viewModel.RetryCount = 3;
            viewModel.RetryDelayMs = 100;
            viewModel.BackoffIndex = (int)RetryBackoff.Doubling;
            viewModel.FailureIndex = (int)StepErrorAction.NextIteration;

            StepMeta? kept = null;
            viewModel.CloseRequested += settings => kept = settings;
            viewModel.ConfirmCommand.Execute(null);

            Assert.NotNull(kept);
            Assert.Equal(3, kept!.RetryCount);
            Assert.Equal(RetryBackoff.Doubling, kept.RetryBackoff);
            Assert.Equal(StepErrorAction.NextIteration, kept.OnError);
        });
    }

    [Fact]
    public void Cancelling_hands_back_nothing()
    {
        Ui.Run(() =>
        {
            var window = Open(StepMeta.Empty);
            var viewModel = (StepSettingsViewModel)window.DataContext!;

            var answered = false;
            viewModel.CloseRequested += settings =>
            {
                answered = true;
                Assert.Null(settings);
            };
            viewModel.CancelCommand.Execute(null);

            Assert.True(answered);
        });
    }

    [Fact]
    public void A_step_without_retries_says_so_rather_than_showing_a_plan()
    {
        Ui.Run(() => Assert.Equal(Strings.Get("StepSettings.Plan.None"),
            new StepSettingsViewModel(StepMeta.Empty).RetryPlan));
    }

    [Fact]
    public void A_long_plan_stops_listing_the_waits_one_by_one()
    {
        Ui.Run(() =>
        {
            var viewModel = new StepSettingsViewModel(new StepMeta
            {
                RetryCount = 9,
                RetryDelayMs = 100,
                RetryBackoff = RetryBackoff.Doubling,
            });

            Assert.EndsWith("…", viewModel.RetryPlan);
        });
    }

    [Fact]
    public void A_number_left_empty_is_read_as_no_wait_at_all()
    {
        Ui.Run(() =>
        {
            var viewModel = new StepSettingsViewModel(StepMeta.Empty);
            viewModel.TimeoutMs = null;
            viewModel.RetryDelayMs = null;
            viewModel.RetryCount = null;

            var settings = viewModel.Build();
            Assert.Equal(0, settings.TimeoutMs);
            Assert.Equal(0, settings.RetryDelayMs);
            Assert.Equal(0, settings.RetryCount);
        });
    }

    [Fact]
    public void The_editor_keeps_the_settings_and_shows_them_on_the_row()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            var step = new MacroStep { Type = "input.keyPress" };
            viewModel.AddStep(step);

            viewModel.ApplyStepSettings(step, new StepMeta
            {
                RetryCount = 2,
                RetryDelayMs = 500,
                RetryBackoff = RetryBackoff.Doubling,
                OnError = StepErrorAction.Continue,
            });

            Assert.Equal(2, step.Meta.RetryCount);
            Assert.True(step.HasMetaSummary);
            Assert.Contains(Strings.Format("Editor.Meta.RetryDoubling", 2), step.MetaSummary);
            Assert.Contains(Strings.Get("Editor.Meta.OnErrorContinue"), step.MetaSummary);

            // Changing the settings is an edit like any other, so one Ctrl + Z takes it back.
            Assert.True(viewModel.CanUndo);
            viewModel.UndoCommand.Execute(null);
            Assert.Equal(0, viewModel.Steps[0].Meta.RetryCount);
        });
    }
}
