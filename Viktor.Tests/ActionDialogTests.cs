using Avalonia.Threading;
using Viktor.Core.Devices;
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

    [Theory]
    [InlineData("input.mouseClick")]
    [InlineData("input.mouseDrag")]
    [InlineData("vision.capture")]
    [InlineData("vision.clickImage")]
    [InlineData("vision.findColor")]
    [InlineData("ocr.recognize")]
    [InlineData("condition.colorEquals")]
    [InlineData("condition.colorsMatch")]
    public void An_action_that_names_a_position_offers_something_to_measure_it_from(string key)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(key);

            var mode = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "anchorMode");
            Assert.True(mode.IsChoice);
            Assert.Equal("screen", mode.CurrentText);
            Assert.Equal(["screen", "window", "client"],
                mode.Choices.Select(choice => choice.Value));

            var window = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "anchorWindow");
            Assert.True(window.IsWindow);
            Assert.False(window.Definition.Required);
        });
    }

    [Fact]
    public void A_colour_search_offers_the_colour_the_place_and_which_hit()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findColor");

            Assert.True(viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "color").IsColor);
            Assert.Equal("5", Value(viewModel, "tolerance"));
            Assert.Equal("1", Value(viewModel, "matchIndex"));
            Assert.Equal("0", Value(viewModel, "timeoutMs"));
            Assert.True(viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "resultVariable").IsVariable);
        });
    }

    [Fact]
    public void Finding_an_image_offers_which_hit_and_whether_to_record_them_all()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");

            Assert.Equal("1", Value(viewModel, "matchIndex"));
            Assert.Equal("false", Value(viewModel, "allMatches"));
        });
    }

    [Fact]
    public void A_condition_can_be_written_as_an_expression()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("condition.expression");
            var expression = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "expression");

            // The field is the one with live checking and suggestions, not a plain text box.
            Assert.True(expression.IsExpression);
            Assert.Contains("$count", expression.Placeholder);
            Assert.Contains("$spot", expression.ExpressionSuggestions);
            Assert.Contains("contains(", expression.ExpressionSuggestions);
        });
    }

    [Fact]
    public void Conditions_are_offered_inside_a_step_and_not_as_one()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, null, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;

            // A condition says what has to be true rather than doing something, and the engine
            // only ever asks one inside an if, a while or a wait, so it is not on the step list.
            Assert.DoesNotContain(viewModel.AvailableActions,
                action => action.Category == ActionCategory.Condition);
            Assert.Contains(viewModel.AvailableActions, action => action.Key == "input.mouseClick");

            Assert.Contains(ActionCatalog.Conditions,
                condition => condition.Key == "condition.imageNotExists");
            Assert.Contains(ActionCatalog.Conditions,
                condition => condition.Key == "condition.textNotExists");
            Assert.Contains(ActionCatalog.Conditions,
                condition => condition.Key == "condition.uiaNotExists");
            Assert.Contains(ActionCatalog.Conditions,
                condition => condition.Key == "condition.expression");
        });
    }

    [Fact]
    public void A_case_is_only_reachable_through_the_switch_that_owns_it()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, null, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var picker = (AddActionViewModel)window.DataContext!;

            // A case is half of a branch: without a switch to belong to it would have nothing
            // to match against, so it is never offered as a step of its own.
            Assert.DoesNotContain(picker.AvailableActions, action => action.Key == "control.case");
            Assert.Contains(picker.AvailableActions, action => action.Key == "control.switch");

            var viewModel = Open("control.switch");
            var cases = viewModel.Parameters.First(parameter => parameter.Definition.Name == "cases");

            Assert.True(cases.IsNested);
            Assert.NotNull(cases.List);
            Assert.Equal(["control.case"], cases.List!.Catalog!.Select(action => action.Key));

            // The otherwise block holds ordinary steps, so it keeps the full list.
            var otherwise = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "otherwise");
            Assert.True(otherwise.IsNested);
            Assert.Null(otherwise.List!.Catalog);
        });
    }

    [Fact]
    public void A_multi_point_colour_condition_asks_for_points_and_how_they_are_met()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("condition.colorsMatch");

            Assert.True(viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "points").IsMultiline);

            var mode = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "mode");
            Assert.True(mode.IsChoice);
            Assert.Equal(["all", "any"], mode.Choices.Select(choice => choice.Value));
            Assert.Equal("all", mode.CurrentText);
        });
    }

    [Fact]
    public void A_position_picked_on_screen_is_stored_relative_to_the_windows_corner()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            viewModel.Windows = new OneOpenWindow();
            Anchor(viewModel, "window");

            Assert.True(viewModel.ApplyCursorPosition(1010, 520));

            Assert.Equal("10", Value(viewModel, "x"));
            Assert.Equal("20", Value(viewModel, "y"));
        });
    }

    [Fact]
    public void A_position_picked_on_screen_is_stored_relative_to_the_inside_of_the_border()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.capture");
            viewModel.Windows = new OneOpenWindow();
            Anchor(viewModel, "client");

            Assert.True(viewModel.ApplyRegion(1008, 530, 30, 40));

            Assert.Equal("8", Value(viewModel, "x"));
            Assert.Equal("10", Value(viewModel, "y"));
            Assert.Equal("30", Value(viewModel, "width"));
            Assert.Equal("40", Value(viewModel, "height"));
        });
    }

    [Fact]
    public void A_position_picked_on_screen_is_stored_relative_to_a_controls_corner()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            viewModel.Ui = new OneFoundElement();
            Anchor(viewModel, "element");
            viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "anchorSelector").Text =
                "Button[automationId='saveButton']";

            Assert.True(viewModel.ApplyCursorPosition(1010, 520));

            Assert.Equal("10", Value(viewModel, "x"));
            Assert.Equal("20", Value(viewModel, "y"));
        });
    }

    [Fact]
    public void A_step_measured_from_a_control_keeps_that_choice_when_it_is_saved()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");
            Anchor(viewModel, "element");
            viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "anchorSelector").Text =
                "Button[automationId='saveButton']";

            MacroStep? saved = null;
            viewModel.CloseRequested += result => saved = result;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Contains(saved!.Parameters, parameter =>
                parameter.Name == "anchorMode" && parameter.Value == "element");
            Assert.Contains(saved.Parameters, parameter =>
                parameter.Name == "anchorSelector"
                && parameter.Value == "Button[automationId='saveButton']");
        });
    }

    [Fact]
    public void Only_the_actions_that_put_the_pointer_somewhere_measure_from_a_control()
    {
        var keys = Ui.Run(() => ActionCatalog.Definitions
            .Where(definition => definition.Parameters.Any(parameter => parameter.Name == "anchorSelector"))
            .Select(definition => definition.Key)
            .Order()
            .ToList());

        Assert.Equal(
            ["input.mouseClick", "input.mouseDoubleClick", "input.mouseDown", "input.mouseDrag",
             "input.mouseMove", "input.mouseScroll", "input.mouseUp", "vision.capture"],
            keys);
    }

    /// <summary>Turns the coordinate picker of a dialog onto a named window.</summary>
    private static void Anchor(AddActionViewModel viewModel, string mode)
    {
        var choice = viewModel.Parameters
            .First(parameter => parameter.Definition.Name == "anchorMode");

        choice.Option = choice.Choices.First(option => option.Value == mode);
        viewModel.Parameters
            .First(parameter => parameter.Definition.Name == "anchorWindow").Text = "Notepad";
    }

    /// <summary>
    /// One window open at 1000,500 with its client area 20 pixels further down, which is what the
    /// picture of a step's numbers is taken against.
    /// </summary>
    private sealed class OneOpenWindow : IWindowDevice
    {
        private static readonly WindowInfo Notepad = new(1, "Notepad - notes.txt",
            new ScreenPoint(1000, 500), new ScreenSize(800, 600), false, false);

        public IReadOnlyList<WindowInfo> List() => [Notepad];

        public WindowInfo? Find(string value, WindowMatch match)
            => Notepad.Title.Contains(value, StringComparison.OrdinalIgnoreCase) ? Notepad : null;

        public string ProcessOf(long handle) => "notepad";

        public string ClassOf(long handle) => "Notepad";

        public bool Activate(long handle) => true;

        public bool Minimize(long handle) => true;

        public bool Maximize(long handle) => true;

        public bool Restore(long handle) => true;

        public bool Close(long handle) => true;

        public bool Move(long handle, int x, int y, int width, int height) => true;

        public ScreenPoint ClientOrigin(long handle) => new(1000, 520);
    }

    /// <summary>
    /// One control on screen at 1000,500, which is what a step anchored to a control measures from.
    /// Everything asked of it except the search is answered with "yes", because this stand-in
    /// exists to show where the numbers land rather than to drive anything.
    /// </summary>
    private sealed class OneFoundElement : IUiDevice
    {
        private static readonly UiElementInfo Save = new("Save", "saveButton", "Button",
            "ButtonClass", new ScreenPoint(1000, 500), new ScreenSize(80, 24), "Notepad");

        public bool Exists(UiQuery query, int timeoutMs) => true;

        public bool Click(UiQuery query, string button) => true;

        public bool FocusWindow(string title) => true;

        public string? GetText(UiQuery query) => Save.Name;

        public bool SetText(UiQuery query, string text, bool clearFirst) => true;

        public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit) => [Save];

        public bool Select(UiQuery query, string text, int itemIndex) => true;

        public bool SetChecked(UiQuery query, bool? state) => true;

        public bool SetExpanded(UiQuery query, string action) => true;

        public bool ScrollIntoView(UiQuery query) => true;

        public IReadOnlyList<IReadOnlyList<string>> ReadTable(UiQuery query, int limit) => [];
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

    [Fact]
    public void Error_rules_are_typed_in_the_step_settings_and_saved_on_the_step()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            Assert.True(viewModel.CanSave);

            viewModel.MetaErrorJumps = "*NotFound => 修一下";

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal([new ErrorJump("*NotFound", "修一下", true)], saved!.Meta.Jumps);

            // Reopening the step shows the same words the user typed, not a rewritten form.
            var reopened = new AddActionWindow(saved, ActionCatalog.Definitions, [], []);
            Dispatcher.UIThread.RunJobs();
            var again = (AddActionViewModel)reopened.DataContext!;
            Assert.Equal("*NotFound => 修一下", again.MetaErrorJumps);
        });
    }

    [Fact]
    public void A_rule_line_that_says_nothing_about_where_to_go_stops_the_step_being_saved()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");

            viewModel.MetaErrorJumps = "Run.ImageNotFound";

            Assert.False(viewModel.CanSave);
            Assert.Equal(Strings.Format("Add.BadErrorJump", "Run.ImageNotFound"),
                viewModel.ValidationMessage);

            // Putting it right lets the step be saved again.
            viewModel.MetaErrorJumps = "Run.ImageNotFound -> 修一下";
            Assert.True(viewModel.CanSave);
        });
    }
}
