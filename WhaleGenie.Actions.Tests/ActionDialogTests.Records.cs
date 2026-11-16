using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Key = Avalonia.Input.Key;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace WhaleGenie.Tests;

// Records and lengths of time: what a field writes, and the unit beside a number.
public partial class ActionDialogTests
{
    /// <summary>
    /// 记录格里写的东西是这一步自己的一部分：填进去的那条记录就写进步骤，数字没写就不写进文件。
    /// </summary>
    [Fact]
    public void A_record_written_in_the_field_goes_into_the_step_as_it_stands()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            var record = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "place");

            record.WriteValue(Fields.Shape("命中"));
            Dispatcher.UIThread.RunJobs();

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var written = saved!.Parameters.ToDictionary(parameter => parameter.Name,
                parameter => parameter.Value, StringComparer.Ordinal);
            Assert.Equal("命中", written["place"]);

            // 两格数字没写就是没写：这一步的位置由那条记录说，不该在文件里留下空的数字。
            Assert.DoesNotContain("x", written.Keys);
            Assert.DoesNotContain("y", written.Keys);
        });
    }

    /// <summary>
    /// 框选与「取指针位置」写下来的数就是用户要的答案，所以写完之后记录格是空的：留着它，运行时
    /// 还是旧记录说了算，屏幕上点的那一下等于白点。
    /// </summary>
    [Fact]
    public void Picking_a_rectangle_empties_the_record_fields()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseDrag");
            foreach (var name in new[] { "startPlace", "endPlace" })
            {
                viewModel.Parameters.First(parameter => parameter.Definition.Name == name).Text =
                    "旧记录";
            }

            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));

            foreach (var name in new[] { "startPlace", "endPlace" })
            {
                Assert.Equal(string.Empty, viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == name).CurrentText);
            }

            Assert.Equal("10", Value(viewModel, "startX"));
            Assert.Equal("60", Value(viewModel, "endY"));

            var capture = Open("vision.capture");
            capture.Parameters.First(parameter => parameter.Definition.Name == "rect").WriteValue(Fields.Shape("旧块"));
            Dispatcher.UIThread.RunJobs();

            Assert.True(capture.ApplyRegion(10, 20, 30, 40));
            Assert.Equal(string.Empty, capture.Parameters
                .First(parameter => parameter.Definition.Name == "rect").CurrentText);
            Assert.Equal("30", Value(capture, "width"));
        });
    }

    /// <summary>The same for the place a pointer reading fills in, rather than a rectangle.</summary>
    [Fact]
    public void Reading_the_pointer_empties_the_record_field()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            var record = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "place");
            record.WriteValue(Fields.Shape("旧记录"));
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.ApplyCursorPosition(300, 400));

            Assert.Equal(string.Empty, record.CurrentText);
            Assert.Equal("300", Value(viewModel, "x"));
            Assert.Equal("400", Value(viewModel, "y"));
        });
    }

    [Fact]
    public void A_wait_until_uses_the_expression_condition_editor()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.waitUntil");
            var condition = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "condition");

            Assert.True(condition.IsCondition);

            // How long the wait may last is not the action's business: the step's own timeout
            // says it, and this action names the "waiting on something outside" length, ten
            // minutes, so a fresh step is neither endless nor done in a minute.
            Assert.DoesNotContain(viewModel.Parameters,
                parameter => parameter.Definition.Name is "timeoutMs" or "onTimeout");
            Assert.Equal(600_000, viewModel.MetaTimeout.Milliseconds);
            Assert.Equal("200", Value(viewModel, "intervalMs"));
            Assert.True(viewModel.ReturnsRows
                .First(row => row.Key == "elapsed").Ticked);
        });
    }

    [Fact]
    public void A_move_offers_the_three_ways_a_pointer_can_travel()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");
            var style = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "style");

            Assert.True(style.IsChoice);
            Assert.Equal("direct", Value(viewModel, "style"));
            Assert.Equal(["direct", "smooth", "human"], style.Choices.Select(choice => choice.Value));
        });
    }

    [Fact]
    public void A_step_settings_section_offers_a_backoff_and_says_what_it_waits()
    {
        Ui.Run(() =>
        {
            // A step that was already told to back off opens on that choice, and the line under
            // it spells out the waits, so the setting is never a guess about what will happen.
            var step = new MacroStep
            {
                Type = "input.keyPress",
                Parameters =
                [
                    new StepParameter { Name = "key", Kind = ActionParameterKind.Text, Written = Fields.Shape("F5") },
                ],
                Meta = new StepMeta
                {
                    RetryCount = 2,
                    RetryDelayMs = 500,
                    RetryBackoff = RetryBackoff.Doubling,
                },
            };

            var window = new ActionEditorWindow(step, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            Assert.Equal("doubling", viewModel.MetaRetryBackoff.Value);
            Assert.Contains("500", viewModel.RetryPlan);
            Assert.Contains("→", viewModel.RetryPlan);

            // Picking another one changes both the plan and what is written back to the step.
            var plan = viewModel.RetryPlan;
            viewModel.MetaRetryBackoff = viewModel.BackoffChoices.First(choice => choice.Value == "jitter");
            Assert.NotEqual(plan, viewModel.RetryPlan);

            MacroStep? saved = null;
            viewModel.CloseRequested += result => saved = result;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal(RetryBackoff.Jitter, saved!.Meta.RetryBackoff);
            Assert.Equal(2, saved.Meta.RetryCount);
        });
    }

    [Fact]
    public void A_step_without_retries_says_it_is_not_retried()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");

            Assert.Equal("fixed", viewModel.MetaRetryBackoff.Value);
            Assert.Equal(Strings.Get("Add.StepRetryPlanNone"), viewModel.RetryPlan);
        });
    }

    [Fact]
    public void A_pause_typed_into_a_step_settings_box_can_run_on_for_days()
    {
        Ui.Run(() =>
        {
            // A wait of a few hours is normal for "release this key for a while" macro work,
            // so the ceiling has to sit well past the old one hour.
            var delay = Open("control.delay");
            Assert.Equal(StepMeta.LongestPauseMs, StoredMaximum(delay, "ms"));

            // The timeouts waiting actions keep in their own settings share the same ceiling.
            var wait = Open("vision.waitImage");
            Assert.Equal(StepMeta.LongestPauseMs, wait.MetaTimeout.Maximum);
        });
    }

    private static decimal StoredMaximum(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == name)
            .Definition.Maximum;

    /// <summary>
    /// A step that waits for something is allowed a length of time before anyone touches it: the
    /// action names one and the dialog writes it into the step's own settings, where it is on
    /// screen beside the rest of them and can be changed or cleared. Nothing is waited on by any
    /// other action, so nothing else starts with a clock.
    /// </summary>
    [Fact]
    public void A_step_that_waits_starts_out_allowed_the_length_the_action_names()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[]
                     {
                         "vision.waitImage", "vision.clickImage", "vision.waitColor",
                         "vision.waitStable", "ocr.clickText",
                     })
            {
                var viewModel = Open(key);
                Assert.Equal(10_000, viewModel.MetaTimeout.Milliseconds);

                MacroStep? saved = null;
                viewModel.CloseRequested += result => saved = result;
                viewModel.SaveCommand.Execute(null);
                Assert.Equal(10_000, saved!.Meta.TimeoutMs);
            }

            Assert.Equal(0, Open("vision.findColor").MetaTimeout.Milliseconds);
            Assert.Equal(0, Open("input.keyPress").MetaTimeout.Milliseconds);

            // A step that already exists brings its own answer, including having none: clearing the
            // field is how a wait is told to go on until the run itself is stopped.
            var cleared = Open("vision.waitImage");
            cleared.LoadFrom(new MacroStep
            {
                Type = "vision.waitImage",
                Meta = new StepMeta { TimeoutMs = 0 },
            });
            Assert.Equal(0, cleared.MetaTimeout.Milliseconds);
        });
    }

    [Fact]
    public void A_length_of_time_can_be_written_in_minutes_and_still_saves_milliseconds()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.delay");
            var ms = viewModel.Parameters.First(parameter => parameter.Definition.Name == "ms");

            ms.Unit = ms.DurationUnits.First(unit => unit.Key == "min");
            ms.NumberValue = 5;

            // The unit is a way of writing the number down; the macro still holds milliseconds.
            Assert.Equal("300000", Value(viewModel, "ms"));
        });
    }

    [Fact]
    public void A_time_opens_in_milliseconds_whatever_the_number_is()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("process.waitFor");
            var timeout = viewModel.MetaTimeout;

            // 10000 is 10 whole seconds, and it still opens as 10000: every time field in a step
            // reads in milliseconds, and converting is the user's own job.
            Assert.Equal("ms", timeout.Unit.Key);
            Assert.Equal(10_000m, timeout.Value);
            Assert.Equal(10_000, timeout.Milliseconds);
        });
    }

    [Fact]
    public void A_time_already_written_into_a_step_opens_in_milliseconds()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.delay");
            viewModel.LoadFrom(new MacroStep
            {
                Type = "control.delay",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "ms",
                        Kind = ActionParameterKind.Number,
                        Written = Formula.Number(300000),
                    },
                ],
            });

            var ms = viewModel.Parameters.First(parameter => parameter.Definition.Name == "ms");

            // Five minutes, and it still opens as the 300000 the step carries.
            Assert.Equal("ms", ms.Unit.Key);
            Assert.Equal(300_000m, ms.NumberValue);
            Assert.Equal("300000", Value(viewModel, "ms"));
        });
    }

    [Fact]
    public void A_time_written_as_an_expression_is_counted_in_milliseconds()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.delay");
            var ms = viewModel.Parameters.First(parameter => parameter.Definition.Name == "ms");

            ms.Unit = ms.DurationUnits.First(unit => unit.Key == "min");
            ms.NumberValue = 2;

            // What the expression builder is handed is the time in milliseconds rather than the 2
            // the box is showing: an expression is counted in milliseconds, so building one out of
            // the number on screen would quietly mean a different moment.
            Assert.Equal("120000", ms.CurrentText);

            ms.WriteValue(Formula.Binary("+", Formula.Name("count"), Formula.Number(1)));

            // The unit dropdown steps aside because the engine reads the expression in milliseconds.
            Assert.False(ms.IsDurationEditor);
            Assert.True(ms.IsDurationFormula);
            Assert.Equal("count + 1", Value(viewModel, "ms"));

            // Writing a plain number in place of the expression puts the spinner back, and it reads
            // in milliseconds the same as every other time field: what is shown is the number the
            // step holds, so nothing is quietly converted.
            ms.WriteValue(Fields.Shape("120000"));

            Assert.True(ms.IsNumberValue);
            Assert.Equal("ms", ms.Unit.Key);
            Assert.Equal(120_000m, ms.NumberValue);
            Assert.Equal("120000", Value(viewModel, "ms"));
        });
    }

    [Fact]
    public void Only_lengths_of_time_get_the_unit_dropdown()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");
            var hold = viewModel.Parameters.First(parameter => parameter.Definition.Name == "holdMs");

            Assert.False(x.IsDuration);
            Assert.False(x.IsDurationEditor);
            Assert.True(hold.IsDurationEditor);
        });
    }

    [Fact]
    public void The_unit_dropdown_is_really_on_screen_for_a_length_of_time()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("control.delay");
            Dispatcher.UIThread.RunJobs();

            var unitBox = window.GetVisualDescendants().OfType<ComboBox>()
                .First(box => box.DataContext is StepParameterViewModel { IsDuration: true });

            Assert.True(unitBox.IsVisible);
            var editor = (StepParameterViewModel)unitBox.DataContext!;
            Assert.Same(editor.DurationUnits, unitBox.ItemsSource);

            // The box shows the unit it is on. A unit the list does not hold would leave the box
            // looking empty, which is what a time field must never do.
            Assert.Equal(0, unitBox.SelectedIndex);

            // The step settings carry the same choice, so a long timeout is written the same way.
            // They live on a page of their own, so that page is the one opened to read them.
            viewModel.Select(viewModel.Pages.First(page => page.Key == AddActionViewModel.ExecPage));
            Dispatcher.UIThread.RunJobs();

            var settingBox = window.GetVisualDescendants().OfType<ComboBox>()
                .First(box => ReferenceEquals(box.DataContext, viewModel.MetaTimeout));
            Assert.Same(viewModel.MetaTimeout.Units, settingBox.ItemsSource);
            Assert.Equal(0, settingBox.SelectedIndex);

            // A plain number carries no unit, so its box steps out of the way. It is the one that
            // is leaving the screen that matters: a hidden box still sits in the tree.
            viewModel.SelectAction("uia.read");
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ComboBox>()
                    .Where(box => box.IsEffectivelyVisible),
                box => box.DataContext is StepParameterViewModel { IsDuration: true });
        });
    }

    [Fact]
    public void A_step_settings_pause_can_be_written_in_minutes_and_still_saves_milliseconds()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.delay");
            var timeout = viewModel.MetaTimeout;
            timeout.Unit = timeout.Units.First(unit => unit.Key == "min");
            timeout.Value = 5;

            MacroStep? saved = null;
            viewModel.CloseRequested += result => saved = result;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal(300_000, saved!.Meta.TimeoutMs);
        });
    }

    [Fact]
    public void A_step_settings_pause_opens_in_milliseconds()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            viewModel.LoadFrom(new MacroStep
            {
                Type = "input.keyPress",
                Meta = new StepMeta { TimeoutMs = 3_600_000 },
            });

            // An hour, and it opens as the 3600000 the step carries.
            Assert.Equal("ms", viewModel.MetaTimeout.Unit.Key);
            Assert.Equal(3_600_000m, viewModel.MetaTimeout.Value);
            Assert.Equal(3_600_000, viewModel.MetaTimeout.Milliseconds);
        });
    }
}
