using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

            // A step that puts the pointer somewhere can be measured from a control as well,
            // which is the fourth basis and only makes sense for the ones that move the pointer.
            Assert.Equal(
                viewModel.Parameters.Any(parameter => parameter.Definition.Name == "anchorSelector")
                    ? ["screen", "window", "client", "element"]
                    : ["screen", "window", "client"],
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

            // The branches of a switch are steps of a block, so they are edited in the editor's
            // own list, where the switch can be seen whole, and the dialog only says so. What a
            // list may take is asked of the place the step is going (the editor's `InsertChoices`),
            // not of this dialog.
            var otherwise = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "otherwise");

            Assert.True(cases.IsStepList);
            Assert.False(cases.IsConditionList);
            Assert.True(otherwise.IsStepList);
        });
    }

    [Fact]
    public void The_steps_of_a_block_are_edited_in_the_editor_and_not_in_the_dialog()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.repeat");
            var body = viewModel.Parameters.First(parameter => parameter.Definition.Name == "body");

            // The dialog says where the steps of a block went instead of showing a second list
            // of the same steps, which is what used to make the same structure editable twice.
            Assert.True(body.IsStepList);
            Assert.False(body.IsConditionList);
            Assert.NotEmpty(body.StepsNote);

            // A condition is the one thing a step needs that is not a step, so it stays here.
            var test = Open("control.if");
            var condition = test.Parameters.First(parameter => parameter.Definition.Name == "condition");

            Assert.True(condition.IsConditionList);
            Assert.False(condition.IsStepList);
            Assert.Same(ActionCatalog.Conditions, condition.List!.Catalog);
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
    public void A_pause_typed_into_a_step_settings_box_can_run_on_for_days()
    {
        Ui.Run(() =>
        {
            // A wait of a few hours is normal for "release this key for a while" macro work,
            // so the ceiling has to sit well past the old one hour.
            var delay = Open("control.delay");
            Assert.Equal(StepMeta.LongestPauseMs, StoredMaximum(delay, "ms"));

            // Timeouts on the waiting actions share the same ceiling.
            var wait = Open("control.waitUntil");
            Assert.Equal(StepMeta.LongestPauseMs, StoredMaximum(wait, "timeoutMs"));
        });
    }

    private static decimal StoredMaximum(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == name)
            .Definition.Maximum;

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
            var viewModel = Open("control.waitUntil");
            var timeout = viewModel.Parameters.First(parameter => parameter.Definition.Name == "timeoutMs");

            // 10000 is 10 whole seconds, and it still opens as 10000: every time field in a step
            // reads in milliseconds, and converting is the user's own job.
            Assert.Equal("ms", timeout.Unit.Key);
            Assert.Equal(10_000m, timeout.NumberValue);
            Assert.Equal("10000", Value(viewModel, "timeoutMs"));
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
                        Value = "300000",
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
            ms.ToggleFormula();

            // The unit dropdown steps aside because the engine reads the expression in milliseconds.
            Assert.False(ms.IsDurationEditor);
            Assert.True(ms.IsDurationFormula);
            Assert.Equal("120000", Value(viewModel, "ms"));

            ms.ToggleFormula();
            // Coming back to a plain number reads in milliseconds, the same as every other time
            // field: what is shown is the number the step holds, so nothing is quietly converted.
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
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
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
            var settingBox = window.GetVisualDescendants().OfType<ComboBox>()
                .First(box => ReferenceEquals(box.DataContext, viewModel.MetaTimeout));
            Assert.Same(viewModel.MetaTimeout.Units, settingBox.ItemsSource);
            Assert.Equal(0, settingBox.SelectedIndex);

            // A plain number carries no unit, so its box steps out of the way. It is the one that
            // is leaving the screen that matters: a hidden box still sits in the tree.
            viewModel.SelectAction("uia.getText");
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

    [Fact]
    public void A_step_with_nothing_unusual_keeps_its_folded_settings_shut()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.mouseMove");
            Dispatcher.UIThread.RunJobs();

            // x and y share a line, so four fields read as three lines while the fold holds the
            // rest: what the coordinates are measured from, and where the input is sent.
            Assert.Equal(3, viewModel.Rows.Count);
            Assert.Equal(5, viewModel.AdvancedRows.Count);
            Assert.False(viewModel.ShowAdvanced);

            var fold = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("Disclosure"));
            Assert.True(fold.IsVisible);

            fold.Command?.Execute(fold.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.ShowAdvanced);
        });
    }

    [Fact]
    public void The_fold_sits_on_the_heading_so_a_full_box_of_fields_cannot_push_it_away()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Six fields, which is what used to push the fold below the bottom edge of the
            // dialog and leave a step that has folded settings with no way to reach them.
            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.mouseClick");
            Dispatcher.UIThread.RunJobs();

            var fold = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("Disclosure"));
            Assert.True(fold.IsVisible);

            var corner = fold.TranslatePoint(default, window);
            Assert.NotNull(corner);

            // The fold belongs to the heading of the box, so it reads above every field rather
            // than under the last of them, where a full box of fields used to leave it off the
            // bottom edge with no way to reach the settings behind it.
            var fields = window.GetVisualDescendants().OfType<Control>()
                .Where(control => control.DataContext is StepParameterViewModel
                    && control.IsEffectivelyVisible)
                .Select(control => control.TranslatePoint(default, window))
                .OfType<Point>()
                .ToList();

            Assert.NotEmpty(fields);
            Assert.All(fields, field => Assert.True(field.Y > corner!.Value.Y,
                $"the fold at {corner} is not above the field at {field}"));
            Assert.InRange(corner!.Value.Y + fold.Bounds.Height, 0, window.Bounds.Height);
        });
    }

    [Fact]
    public void A_step_that_uses_a_folded_setting_opens_with_it_showing()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            viewModel.LoadFrom(new MacroStep
            {
                Type = "input.keyPress",
                Parameters =
                [
                    new StepParameter { Name = "key", Kind = ActionParameterKind.Key, Value = "F5" },
                    new StepParameter
                    {
                        Name = "inputMode",
                        Kind = ActionParameterKind.Choice,
                        Value = "background",
                    },
                ],
            });

            // A step that posts its input somewhere is doing something unusual, so the reason is
            // put in front of the user rather than left under the fold.
            Assert.True(viewModel.ShowAdvanced);
        });
    }

    [Fact]
    public void A_step_that_says_nothing_about_the_folded_settings_opens_with_them_shut()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            viewModel.LoadFrom(new MacroStep
            {
                Type = "input.keyPress",
                Parameters =
                [
                    new StepParameter { Name = "key", Kind = ActionParameterKind.Key, Value = "F5" },
                ],
            });

            Assert.False(viewModel.ShowAdvanced);
        });
    }

    [Fact]
    public void A_folded_setting_is_still_saved_onto_the_step()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            var mode = viewModel.Parameters.First(parameter => parameter.Definition.Name == "inputMode");
            mode.Option = mode.Choices.First(choice => choice.Value == "background");

            MacroStep? saved = null;
            viewModel.CloseRequested += result => saved = result;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Contains(saved!.Parameters,
               parameter => parameter.Name == "inputMode" && parameter.Value == "background");
        });
    }

    [Fact]
    public void The_knobs_of_a_search_sit_behind_the_fold_too()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var visible = viewModel.Rows.SelectMany(Names).ToList();
            var folded = viewModel.AdvancedRows.SelectMany(Names).ToList();

            // How sure a match has to be, which hit to take and what the search is measured from
            // are worth having and not worth showing on every step.
            Assert.DoesNotContain("confidence", visible);
            Assert.Contains("confidence", folded);
            Assert.Contains("matchIndex", folded);
            Assert.Contains("anchorMode", folded);

            // What the step is about stays in front.
            Assert.Contains("image", visible);
            Assert.Contains("region", visible);
        });
    }

    [Fact]
    public void The_action_picker_opens_as_searchable_groups_rather_than_one_long_list()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            Assert.True(viewModel.IsPickerOpen);
            Assert.Empty(viewModel.ActionSearch);

            // The blocks are lifted out of the categories and listed together, in the order a
            // task is built in: the four things that all "repeat" only read as different from
            // each other when they stand side by side.
            var blocks = viewModel.ActionGroups.First(group => group.Key == "blocks");
            Assert.Equal(
                ["control.sequence", "control.repeat", "control.while", "control.for",
                    "control.forEach", "control.if", "control.switch", "control.try",
                    // The switch case is a block too, and only ever offered inside a switch, so
                    // it lands after the ones a task is built from.
                    "control.case"],
                blocks.Actions.Select(action => action.Key));
            Assert.True(blocks.HasNote);

            // Everything else stays a group a category, and no block is left behind in one.
            var groups = viewModel.ActionGroups
                .Where(group => group.Key is not "recent" and not "blocks").ToList();
            Assert.Equal(
                viewModel.AvailableActions.Select(action => action.Category.ToString())
                    .Distinct().Order().ToList(),
                groups.Select(group => group.Key).Order().ToList());
            Assert.All(groups, group => Assert.All(group.Actions,
                action => Assert.Equal(action.Category.ToString(), group.Key)));
            Assert.All(groups.SelectMany(group => group.Actions), action => Assert.DoesNotContain(
                action.Parameters,
                parameter => parameter.Kind is ActionParameterKind.Steps && !parameter.ConditionsOnly));

            // Over a hundred actions read as a dozen shut headings until one of them is opened or
            // a search says which one matters. ("Recently used" is the exception: it is open,
            // because a handful of actions the user just reached for is already short enough.)
            Assert.False(blocks.IsOpen);
            Assert.All(groups, group => Assert.False(group.IsOpen));
            Assert.All(groups, group => Assert.NotEmpty(group.Actions));
            Assert.NotEmpty(blocks.Actions);

            var search = window.GetVisualDescendants().OfType<TextBox>()
                .First(box => Equals(box.PlaceholderText, Strings.Get("Add.SearchAction")));
            Assert.True(search.IsEffectivelyVisible);

            // The dropdown the picker used to be, holding every action at once, is gone.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ComboBox>(),
                box => ReferenceEquals(box.ItemsSource, viewModel.AvailableActions));
        });
    }

    [Fact]
    public void Typing_in_the_search_narrows_the_catalogue_to_what_matches()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseMove");
            viewModel.OpenPickerCommand.Execute(null);

            // Something written in any of the three things a search reads — the key, the name the
            // card shows, what the action does — finds the action. The name is asked for in
            // whatever language the interface is in rather than a word typed into the case.
            var target = viewModel.AvailableActions.First(action => action.Key == "input.mouseScroll");
            foreach (var term in new[] { "mouseScroll", target.LocalName })
            {
                viewModel.ActionSearch = term;
                Dispatcher.UIThread.RunJobs();

                var found = viewModel.ActionGroups.SelectMany(group => group.Actions).ToList();
                Assert.NotEmpty(found);
                Assert.Contains(target, found);
                Assert.All(found, action => Assert.True(
                    action.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || action.LocalName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || action.LocalDescription.Contains(term, StringComparison.OrdinalIgnoreCase)));

                // A search has already done the narrowing, so everything left is open to read.
                Assert.All(viewModel.ActionGroups, group => Assert.True(group.IsOpen, group.Key));
            }

            // Something no action answers to empties the picker and says so, rather than
            // leaving the last result of the previous search behind under the box.
            viewModel.ActionSearch = "zzzz";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(viewModel.ActionGroups);
            Assert.True(viewModel.HasNoActionMatch);
        });
    }

    [Fact]
    public void Choosing_an_action_folds_the_picker_down_to_the_line_that_names_it()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;

            // Clicking a card in the picker is the same thing as choosing that action.
            var card = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("ActionCard")
                    && button.DataContext is ActionDefinition { Key: "input.mouseMove" });
            card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("input.mouseMove", viewModel.SelectedDefinition!.Key);
            Assert.False(viewModel.IsPickerOpen);
            Assert.Contains(viewModel.SelectedDefinition.LocalName, viewModel.SelectedActionTitle);

            // The search box and the groups step out of the way and leave the fields the room.
            var search = window.GetVisualDescendants().OfType<TextBox>()
                .First(box => Equals(box.PlaceholderText, Strings.Get("Add.SearchAction")));
            Assert.False(search.IsEffectivelyVisible);

            viewModel.OpenPickerCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Asking for another action brings the picker back with the search box emptied, so
            // the previous search is not still in the way.
            Assert.True(viewModel.IsPickerOpen);
            Assert.Empty(viewModel.ActionSearch);
            Assert.True(search.IsEffectivelyVisible);
        });
    }

    [Fact]
    public void A_group_opened_by_hand_stays_open()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            var heading = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("GroupToggle")
                    && button.DataContext is ActionGroupViewModel { Key: "Input" });
            Assert.False(((ActionGroupViewModel)heading.DataContext!).IsOpen);

            heading.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // A search rebuilds the groups, and the one the user opened is still open after it.
            Assert.True(viewModel.ActionGroups.First(group => group.Key == "Input").IsOpen);
            viewModel.ActionSearch = "mouse";
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.ActionGroups.First(group => group.Key == "Input").IsOpen);
        });
    }

    [Fact]
    public void An_action_that_has_just_been_used_waits_at_the_top_of_the_picker()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.getPixel");
            viewModel.CloseRequested += _ => { };
            viewModel.SaveCommand.Execute(null);

            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var picker = (AddActionViewModel)window.DataContext!;
            var recent = picker.ActionGroups.First(group => group.Key == "recent");

            // The group is there to save the trip back through the categories, so it is open
            // and the action is the first thing in it.
            Assert.True(recent.IsOpen);
            Assert.Equal("vision.getPixel", recent.Actions[0].Key);
        });
    }

    /// <summary>The parameter names on one line of the dialog.</summary>
    private static IEnumerable<string> Names(ParameterRowViewModel row)
    {
        yield return row.First.Definition.Name;
        if (row.Second is { } second)
        {
            yield return second.Definition.Name;
        }
    }
}
