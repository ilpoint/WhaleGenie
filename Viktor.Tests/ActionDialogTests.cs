using Avalonia.Threading;
using Viktor.Core.Execution;
using Viktor.Localization;
using Viktor.Models;
using Viktor.ViewModels;
using Viktor.Views;

namespace Viktor.Tests;

/// <summary>
/// What the "Add action" dialog does when it is driven the way a user drives it: the window is
/// built for real, the controls are found, and the values are read back through the view model.
/// </summary>
public class ActionDialogTests
{
    private static AddActionViewModel Open(string key)
    {
        var window = new AddActionWindow(null, ActionCatalog.Definitions,
            ["match.x", "match.y", "spot"], []);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction(key);
        Dispatcher.UIThread.RunJobs();
        return viewModel;
    }

    private static string Value(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == name).CurrentText;

    [Fact]
    public void A_number_can_be_written_as_an_expression()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");
            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");

            Assert.True(x.IsNumberValue);
            x.ToggleFormula();
            x.Text = "$match.x";

            Assert.True(x.IsNumberFormula);
            Assert.Equal("$match.x", x.CurrentText);
            Assert.False(x.CanToggleFormula);
        });
    }

    [Fact]
    public void An_expression_survives_saving_and_reopening_the_step()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");
            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");
            x.ToggleFormula();
            x.Text = "$match.x";

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal("$match.x",
                saved!.Parameters.First(parameter => parameter.Name == "x").Value);

            var reopened = new AddActionWindow(saved, ActionCatalog.Definitions,
                ["match.x"], []);
            reopened.Show();
            Dispatcher.UIThread.RunJobs();

            var again = ((AddActionViewModel)reopened.DataContext!).Parameters
                .First(parameter => parameter.Definition.Name == "x");
            Assert.True(again.IsNumberFormula);
            Assert.Equal("$match.x", again.CurrentText);
        });
    }

    [Fact]
    public void The_pointer_shortcut_writes_plain_numbers()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");
            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");
            x.ToggleFormula();
            x.Text = "$match.x";

            Assert.True(viewModel.ApplyCursorPosition(120, 240));
            Assert.Equal("120", x.CurrentText);
            Assert.Equal("240",
                viewModel.Parameters.First(parameter => parameter.Definition.Name == "y").CurrentText);
        });
    }

    [Fact]
    public void A_vision_result_is_a_variable_the_macro_already_knows()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var result = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "resultVariable");

            Assert.True(result.IsVariable);
            Assert.Equal("match", result.Text);
            Assert.Contains("match.x", result.Variables);
        });
    }

    [Theory]
    [InlineData("vision.capture", "x")]
    [InlineData("ocr.recognize", "x")]
    [InlineData("input.mouseDrag", "startX")]
    [InlineData("ocr.findText", "region")]
    [InlineData("vision.findImage", "region")]
    [InlineData("condition.imageExists", "region")]
    public void The_region_picker_sits_on_the_row_that_names_a_rectangle(string key, string anchor)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(key);

            Assert.True(viewModel.HasRegion);
            Assert.True(viewModel.Parameters
                .First(parameter => parameter.Definition.Name == anchor).IsRegionAnchor);
        });
    }

    [Theory]
    [InlineData("input.mouseClick")]
    [InlineData("vision.getPixel")]
    [InlineData("input.keyPress")]
    public void An_action_that_only_needs_a_point_has_no_region_picker(string key)
    {
        Ui.Run(() => Assert.False(Open(key).HasRegion));
    }

    [Fact]
    public void A_rectangle_fills_x_y_width_and_height()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.capture");

            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));
            Assert.Equal("10", Value(viewModel, "x"));
            Assert.Equal("20", Value(viewModel, "y"));
            Assert.Equal("30", Value(viewModel, "width"));
            Assert.Equal("40", Value(viewModel, "height"));
        });
    }

    [Fact]
    public void A_rectangle_fills_a_single_region_field()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("ocr.findText");

            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));
            Assert.Equal("10,20,30,40", Value(viewModel, "region"));
        });
    }

    [Fact]
    public void A_rectangle_fills_both_corners_of_a_drag()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseDrag");

            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));
            Assert.Equal("10", Value(viewModel, "startX"));
            Assert.Equal("20", Value(viewModel, "startY"));
            Assert.Equal("40", Value(viewModel, "endX"));
            Assert.Equal("60", Value(viewModel, "endY"));
        });
    }

    [Fact]
    public void A_wait_until_is_edited_with_the_condition_editor()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.waitUntil");
            var condition = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "condition");

            // The wait borrows the same nested condition editor an if or a while uses, and it
            // offers nothing but conditions, so a stray action cannot be dropped in as one.
            Assert.True(condition.IsNested);
            Assert.NotNull(condition.List);
            Assert.True(condition.List!.IsCondition);
            Assert.Same(ActionCatalog.Conditions, condition.List.Catalog);

            Assert.Equal("10000", Value(viewModel, "timeoutMs"));
            Assert.Equal("200", Value(viewModel, "pollMs"));
            Assert.Equal("stop", Value(viewModel, "onTimeout"));
            Assert.True(viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "elapsedVariable").IsVariable);
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
                    new StepParameter { Name = "key", Kind = ActionParameterKind.Text, Value = "F5" },
                ],
                Meta = new StepMeta
                {
                    RetryCount = 2,
                    RetryDelayMs = 500,
                    RetryBackoff = RetryBackoff.Doubling,
                },
            };

            var window = new AddActionWindow(step, ActionCatalog.Definitions, [], []);
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
}
