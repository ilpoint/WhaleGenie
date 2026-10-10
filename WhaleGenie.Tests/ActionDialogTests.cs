using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// What the "Add action" dialog does when it is driven the way a user drives it: the window is
/// built for real, the controls are found, and the values are read back through the view model.
/// </summary>
public class ActionDialogTests
{
    private static AddActionViewModel Open(string key)
    {
        var window = new AddActionWindow(null, ActionCatalog.Definitions,
            VariableChoicesForChecks.Named("match.x", "match.y", "spot"), []);

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
                VariableChoicesForChecks.Named("match.x"), []);
            reopened.Show();
            Dispatcher.UIThread.RunJobs();

            var again = ((AddActionViewModel)reopened.DataContext!).Parameters
                .First(parameter => parameter.Definition.Name == "x");
            Assert.True(again.IsNumberFormula);
            Assert.Equal("$match.x", again.CurrentText);
        });
    }

    [Fact]
    public void A_new_step_starts_by_pausing_and_asking_if_it_fails()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal(StepErrorAction.AskUser, saved!.Meta.OnError);
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

    /// <summary>
    /// Looking for a picture is complete once the picture is named. "Ignore this colour" answers a
    /// question most steps never ask, so leaving it empty is not a mistake — it is what a step that
    /// does not care about it looks like, and the label says so. Treating it as still to be filled
    /// in left all three picture finders unsavable, which is what this holds shut.
    /// </summary>
    [Fact]
    public void Naming_the_picture_is_enough_to_save_a_look()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "vision.findImage", "vision.waitImage", "vision.clickImage" })
            {
                var viewModel = Open(key);
                var ignore = viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == "ignoreColor");

                Assert.Equal(
                    Strings.Format("Common.OptionalSuffix", ignore.Definition.LocalLabel),
                    ignore.Label);

                viewModel.Parameters.First(parameter => parameter.Definition.Name == "image")
                    .AddPicture(new ImageRowViewModel { Text = @"C:\images\ok.png" });

                Assert.True(viewModel.CanSave, $"{key}: {viewModel.ValidationMessage}");
            }
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
            // A list that names itself says so on the button that adds to it, the way the editor's
            // own list does: the branches of a switch are branches, not steps.
            Assert.Equal(Strings.Get("Add.Case"), cases.List!.AddLabel);
            Assert.True(otherwise.IsStepList);
        });
    }

    [Fact]
    public void The_add_button_in_a_block_offers_only_what_that_list_takes()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.switch");
            var cases = viewModel.Parameters.First(parameter => parameter.Definition.Name == "cases");
            var otherwise = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "otherwise");

            // A switch's branches are branches wherever they are added from, so the button in the
            // dialog is narrowed the same way the editor's own list is. An ordinary block takes
            // anything and says so by offering nothing in particular.
            Assert.Equal(["control.case"], cases.List!.Catalog!.Select(action => action.Key));
            Assert.Null(otherwise.List!.Catalog);
        });
    }

    [Fact]
    public void A_switch_compares_two_operands_with_an_operator_from_a_list()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.switch");
            var value = viewModel.Parameters.First(parameter => parameter.Definition.Name == "value");
            var mode = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "matchMode");

            // Both sides of a switch's comparison are operands: a variable list to pick from with
            // room to type, so a value never has to be spelled from memory. The operator is only
            // ever picked, never written out.
            Assert.True(value.IsVariable);
            Assert.False(value.IsExpression);
            Assert.True(mode.IsChoice);
            Assert.NotEmpty(mode.Choices);

            var branch = Open("control.case");
            var values = branch.Parameters.First(parameter => parameter.Definition.Name == "values");

            // The other side is an operand too, so the branch values read the same way the value
            // they are compared with does.
            Assert.True(values.IsVariable);
        });
    }

    [Fact]
    public void A_variable_calculation_is_one_expression()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.calculate");
            var value = viewModel.Parameters.First(parameter => parameter.Definition.Name == "value");

            // The operand-and-operator pieces are gone: the whole value is one expression, offered
            // the live checking and suggestions that storing a value gets, so nothing has to be
            // assembled by hand.
            Assert.True(value.IsExpression);
            Assert.NotEmpty(value.ExpressionSuggestions);
            Assert.DoesNotContain(viewModel.Parameters,
                parameter => parameter.Definition.Name is "left" or "right" or "operator");
        });
    }

    [Fact]
    public void Nothing_can_be_stored_into_a_variable_WhaleGenie_owns()
    {
        Ui.Run(() =>
        {
            // The variables WhaleGenie provides are read-only, and the rule belongs to storing a value
            // under a name rather than to one action, so a calculation is refused the same way
            // "Set Variable" is. The clipboard is written by the clipboard action, never by
            // storing into sys.clipboard.
            foreach (var key in new[] { "control.setVariable", "control.calculate", "control.listCreate" })
            {
                var viewModel = Open(key);
                viewModel.Parameters.First(parameter => parameter.Definition.Name == "name").Text =
                    "sys.clipboard";

                Assert.False(viewModel.CanSave);
                Assert.Equal(Strings.Format("Add.SystemReadOnly", "sys.clipboard"),
                    viewModel.ValidationMessage);
            }
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

            // Adding one is still here: it is the one thing about the inside of a block you may
            // well want while its settings are open.
            Assert.Equal(Strings.Get("Add.AddStepToBlock"), body.List!.AddLabel);

            // A condition is the one thing a step needs that is not a step, so it stays here.
            var test = Open("control.if");
            var condition = test.Parameters.First(parameter => parameter.Definition.Name == "condition");

            Assert.True(condition.IsConditionList);
            Assert.False(condition.IsStepList);
            Assert.Same(ActionCatalog.Conditions, condition.List!.Catalog);
        });
    }

    [Fact]
    public void The_settings_of_a_block_say_they_are_about_the_block()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(
                new MacroStep { Type = "control.repeat" }, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;

            // Retries, timeouts and "when it fails" mean something bigger on a block: a failure
            // inside it that no step answered is the block failing, so the dialog says so.
            Assert.True(viewModel.IsBlock);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == Strings.Get("Add.BlockSettings"));

            // A step that does one thing is not a block, and the line is not about it.
            Assert.False(Open("control.delay").IsBlock);
        });
    }

    [Fact]
    public void A_block_can_be_given_a_step_from_its_own_dialog()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(
                new MacroStep { Type = "control.repeat" }, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            var body = viewModel.Parameters.First(parameter => parameter.Definition.Name == "body");
            var before = body.StepsNote;

            // The block's settings carry the line about where its steps are ordered, and beside it
            // the one thing this dialog still does to them: add another.
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == body.StepsNote);
            var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                candidate => candidate.IsEffectivelyVisible
                    && Equals(candidate.Content, body.List!.AddLabel));

            button.Command!.Execute(button.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            // The action is picked in the same dialog the editor opens for a step of its own, and
            // what it returns is appended to this block's list rather than to the macro.
            var nested = window.OwnedWindows.OfType<AddActionWindow>().Single();
            var picker = (AddActionViewModel)nested.DataContext!;
            picker.SelectAction("control.delay");
            Dispatcher.UIThread.RunJobs();
            Assert.True(picker.CanSave, picker.ValidationMessage);
            picker.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("control.delay", Assert.Single(body.List!.Steps).Type);

            // The line under the settings is the count, so it has to follow what was added.
            Assert.NotEqual(before, body.StepsNote);

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.Equal("control.delay",
                Assert.Single(saved!.StepLists.Single().Steps).Type);
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
    private static void Choose(AddActionViewModel viewModel, string name, string value)
    {
        var choice = viewModel.Parameters.First(parameter => parameter.Definition.Name == name);
        choice.Option = choice.Choices.First(option => option.Value == value);
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

        public UiTable ReadTable(UiQuery query, int limit) => UiTable.Empty;
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
            Assert.Contains("match.x", result.Variables.Select(choice => choice.Name));
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
            // They live on a page of their own, so that page is the one opened to read them.
            viewModel.Select(viewModel.Pages.First(page => page.Key == AddActionViewModel.OtherPage));
            Dispatcher.UIThread.RunJobs();

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
    public void The_pages_hold_the_fields_a_step_is_read_a_part_at_a_time()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.mouseMove");
            Dispatcher.UIThread.RunJobs();

            // x and y share a line, so four fields read as three lines while the advanced page
            // holds the rest: what the coordinates are measured from, and where the input is sent.
            Assert.Equal(3, viewModel.Rows.Count);
            Assert.Equal(5, viewModel.AdvancedRows.Count);
            Assert.True(viewModel.OnBase);

            // The pages are opened by their own tabs rather than by a fold, so a step's settings
            // are read in the parts they come in.
            var advanced = viewModel.Pages.First(page => page.Key == AddActionViewModel.AdvancedPage);
            Assert.True(advanced.IsPresent);
            viewModel.Select(advanced);
            Assert.True(viewModel.OnAdvanced);
        });
    }

    [Fact]
    public void The_pages_sit_above_the_fields_so_a_full_box_of_them_cannot_push_them_away()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Six fields, which is what used to push the fold below the bottom edge of the dialog
            // and leave a step with settings no one could reach.
            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.mouseClick");
            Dispatcher.UIThread.RunJobs();

            var tab = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("PageTab") && button.IsEffectivelyVisible);

            var corner = tab.TranslatePoint(default, window);
            Assert.NotNull(corner);

            // The pages are above the fields rather than under the last of them, which is where a
            // long form used to leave the settings behind them off the bottom edge.
            var fields = window.GetVisualDescendants().OfType<Control>()
                .Where(control => control.DataContext is StepParameterViewModel
                    && control.IsEffectivelyVisible)
                .Select(control => control.TranslatePoint(default, window))
                .OfType<Point>()
                .ToList();

            Assert.NotEmpty(fields);
            Assert.All(fields, field => Assert.True(field.Y > corner!.Value.Y,
                $"the pages at {corner} are not above the field at {field}"));
            Assert.InRange(corner!.Value.Y + tab.Bounds.Height, 0, window.Bounds.Height);
        });
    }

    [Fact]
    public void A_step_that_changed_an_advanced_setting_opens_on_that_page()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.keyPress");
            viewModel.LoadFrom(new MacroStep
            {
                Type = "input.keyPress",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "inputMode",
                        Kind = ActionParameterKind.Choice,
                        Value = "background",
                    },
                ],
            });

            // A step that posts its input somewhere is doing something unusual, so that is the page
            // it opens on rather than something to go looking for.
            Assert.True(viewModel.OnAdvanced);
            Assert.True(viewModel.Pages.First(page =>
                page.Key == AddActionViewModel.AdvancedPage).HasDot);
        });
    }

    [Fact]
    public void A_step_that_says_nothing_unusual_opens_on_what_the_step_is()
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

            Assert.True(viewModel.OnBase);
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

            // Which hit to take and what the search is measured from are worth having and not worth
            // showing on every step; how alike the picture has to be is part of what the step is.
            Assert.Contains("confidence", visible);
            Assert.Contains("matchIndex", folded);
            Assert.Contains("anchorMode", folded);

            // What the step is about stays in front.
            Assert.Contains("image", visible);
            Assert.Contains("region", visible);
        });
    }

    /// <summary>
    /// Pairing up features comes back with one hit, so counting the hits, choosing which one and
    /// recording the whole set have nothing behind them there. Those lines leave the form while
    /// that way is chosen and come back with the values that were typed into them: a field that
    /// stays and does nothing is how a form comes to look broken.
    /// </summary>
    [Fact]
    public void The_questions_only_a_pixel_search_can_answer_come_and_go_with_the_way()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "vision.findImage", "vision.waitImage", "vision.clickImage" })
            {
                var viewModel = Open(key);
                var index = viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == "matchIndex");

                index.NumberValue = 3;

                // Pairing up features: only "least feature pairs" has anything to answer to.
                Choose(viewModel, "algorithm", "feature");
                Dispatcher.UIThread.RunJobs();

                Assert.True(Shown(viewModel, "minFeatures"), key);
                Assert.False(Shown(viewModel, "matchIndex"), key);
                Assert.False(Shown(viewModel, "orderBy"), key);
                Assert.False(Shown(viewModel, "allMatches"), key);

                // Back to a pixel comparison, and what was typed is still there.
                Choose(viewModel, "algorithm", "correlated");
                Dispatcher.UIThread.RunJobs();

                Assert.True(Shown(viewModel, "matchIndex"), key);
                Assert.True(Shown(viewModel, "orderBy"), key);
                Assert.False(Shown(viewModel, "minFeatures"), key);
                Assert.Equal("3", index.CurrentText);
            }
        });
    }

    /// <summary>Whether a line of the form is drawn at all.</summary>
    private static bool Shown(AddActionViewModel viewModel, string name)
        => viewModel.Rows.Concat(viewModel.AdvancedRows)
            .Any(row => Names(row).Contains(name) && row.IsApplicable);

    /// <summary>
    /// What the step is saved as carries only the questions the way it was set up asks: a step that
    /// pairs up features has no room for "the third hit", not even the default one.
    /// </summary>
    [Fact]
    public void A_step_saves_only_the_fields_its_way_asks_about()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "image").Text =
                @"C:\images\ok.png";
            viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "matchIndex").NumberValue = 3;
            viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "allMatches").Flag = true;
            Choose(viewModel, "algorithm", "feature");
            Dispatcher.UIThread.RunJobs();

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            var names = saved!.Parameters.Select(parameter => parameter.Name).ToList();

            Assert.DoesNotContain("matchIndex", names);
            Assert.DoesNotContain("allMatches", names);
            Assert.DoesNotContain("orderBy", names);
            Assert.Contains("algorithm", names);
        });
    }

    /// <summary>
    /// One picker answers both "which way" and "comparing what". Two lists let a step say "pair up
    /// the features, comparing pixel for pixel", which is a sentence with nothing behind it — and
    /// whichever half lost was the half doing nothing.
    /// </summary>
    [Fact]
    public void Recognising_a_picture_is_one_choice_of_four()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "vision.findImage", "vision.waitImage", "vision.clickImage" })
            {
                var viewModel = Open(key);
                var folded = viewModel.AdvancedRows.SelectMany(Names).ToList();

                Assert.Contains("algorithm", folded);
                Assert.DoesNotContain("method", folded);

                var algorithm = viewModel.Parameters
                    .First(parameter => parameter.Definition.Name == "algorithm");

                Assert.Equal(["normed", "correlated", "difference", "feature"],
                    algorithm.Definition.Options);
                Assert.Equal(4, algorithm.Definition.OptionLabels.Count);
            }
        });
    }

    [Fact]
    public void A_file_step_reads_in_the_order_the_job_is_done_in()
    {
        Ui.Run(() =>
        {
            // Which file, which part of it, where the answer or the data goes — and then the
            // knobs. Filling a step in is answering those questions in that order, so the dialog
            // asks them in that order and keeps the rest behind the fold.
            var reading = Open("file.readCsv");
            Assert.Equal(
                ["path", "hasHeader", "columns", "matchColumn", "matchValue"],
                reading.Rows.SelectMany(Names).ToList());

            var folded = reading.AdvancedRows.SelectMany(Names).ToList();
            Assert.Contains("encoding", folded);
            Assert.Contains("separator", folded);
            Assert.Contains("trim", folded);

            // The names the step leaves its answer under are asked for on a page of their own: they
            // are not part of what the step is, they are how the rest of the macro reaches it.
            Assert.Equal(["resultVariable", "headerVariable"],
                reading.OutputRows.SelectMany(Names).ToList());

            var writing = Open("file.writeCsv");
            Assert.Equal(["path", "rows", "header", "mode", "align"],
                writing.Rows.SelectMany(Names).ToList());
            Assert.Empty(writing.OutputRows);

            var listing = Open("file.listFiles");
            Assert.Equal(["folder", "pattern", "recurse"],
                listing.Rows.SelectMany(Names).ToList());
            Assert.Equal(["resultVariable"], listing.OutputRows.SelectMany(Names).ToList());

            var text = Open("file.readText");
            Assert.Equal(["path"], text.Rows.SelectMany(Names).ToList());
            Assert.Equal(["resultVariable"], text.OutputRows.SelectMany(Names).ToList());
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
            Assert.Empty(viewModel.ActionSearch);

            // The list of actions is on screen the whole time the dialog is, rather than folded
            // away once one is chosen: changing one's mind is a click and nothing moves. The
            // groups start shut, so what is on screen is the box and the headings.
            var headings = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("GroupToggle") && button.IsEffectivelyVisible)
                .ToList();
            Assert.NotEmpty(headings);

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

            // The categories are read in two parts: what a game macro reaches for, then the rest.
            // The headings that divide them hold no actions of their own, so they are told apart
            // from the groups by that.
            var sections = viewModel.ActionGroups.Where(group => group.IsSection).ToList();
            Assert.Equal(["section.game", "section.further"],
                sections.Select(section => section.Key));
            Assert.All(sections, section => Assert.Empty(section.Actions));
            Assert.All(sections, section => Assert.True(section.HasNote));

            // Everything else stays a group a category, and no block is left behind in one.
            var groups = viewModel.ActionGroups
                .Where(group => group.HasActions && group.Key is not "recent" and not "blocks")
                .ToList();
            Assert.Equal(
                viewModel.AvailableActions.Select(action => action.Category.ToString())
                    .Distinct().Order().ToList(),
                groups.Select(group => group.Key).Order().ToList());
            Assert.All(groups, group => Assert.All(group.Actions,
                card => Assert.Equal(card.Definition.Category.ToString(), group.Key)));
            Assert.All(groups.SelectMany(group => group.Actions), action => Assert.DoesNotContain(
                action.Definition.Parameters,
                parameter => parameter.Kind is ActionParameterKind.Steps && !parameter.ConditionsOnly));

            // Over a hundred actions read as a dozen shut headings until one of them is opened or
            // a search says which one matters. ("Recently used" is the exception: it is open,
            // because a handful of actions the user just reached for is already short enough.)
            Assert.False(blocks.IsOpen);
            Assert.All(groups, group => Assert.False(group.IsOpen));
            Assert.All(groups, group => Assert.NotEmpty(group.Actions));
            Assert.NotEmpty(blocks.Actions);

            // The first part lists the categories a game macro is written out of, in the order
            // they are reached for: what the screen shows and says, then the hands, then the
            // things around the game.
            var forGames = viewModel.ActionGroups
                .SkipWhile(group => group.Key != "section.game")
                .Skip(1)
                .TakeWhile(group => !group.IsSection)
                .Select(group => group.Key)
                .ToList();
            Assert.Equal(["Vision", "Ocr", "Input", "Control", "Window", "Process", "System",
                "Data", "Script"], forGames);

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
                Assert.Contains(found, card => ReferenceEquals(card.Definition, target));
                Assert.All(found, action => Assert.True(
                    action.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || action.Definition.LocalName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || action.Definition.LocalDescription.Contains(
                        term, StringComparison.OrdinalIgnoreCase)));

                // A search has already done the narrowing, so everything left is open to read.
                Assert.All(viewModel.ActionGroups.Where(group => group.HasActions),
                    group => Assert.True(group.IsOpen, group.Key));
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
    public void Choosing_an_action_marks_its_card_and_fills_the_fields_beside_it()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;

            // The list opens with its groups folded, so the card for one action is only on
            // screen once something has narrowed the list to it — searching for its key does
            // that, and searching is also what opens the groups that hold the matches. Looking
            // for the card without searching would only work while a case that runs first has
            // happened to use this action, because "recently used" is kept in a static list.
            viewModel.ActionSearch = "mouseMove";
            Dispatcher.UIThread.RunJobs();

            // Clicking a card in the list is the same thing as choosing that action.
            var card = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("ActionCard")
                    && button.DataContext is ActionCardViewModel { Key: "input.mouseMove" });
            card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("input.mouseMove", viewModel.SelectedDefinition!.Key);
            Assert.Contains(viewModel.SelectedDefinition.LocalName, viewModel.SelectedActionName);

            // The card that is being built reads as chosen, and the search box and the groups stay
            // where they are: the list is one of the two halves of the dialog, not a question that
            // has been answered and folded away.
            Assert.True(((ActionCardViewModel)card.DataContext!).IsSelected);
            var search = window.GetVisualDescendants().OfType<TextBox>()
                .First(box => Equals(box.PlaceholderText, Strings.Get("Add.SearchAction")));
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

    // -------------------------------------------------------- keys and controller controls

    [Fact]
    public void A_hotkey_is_built_a_key_at_a_time_from_the_keyboard_on_screen()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.hotkey");
            Dispatcher.UIThread.RunJobs();

            // The combination is written in one box with the keyboard beside it, and what the
            // keyboard hands back joins what is written: Ctrl+Shift+S is put together one key at a
            // time, so a key that replaced the rest would throw away the two already picked.
            var keys = viewModel.Parameters.First(parameter => parameter.Definition.Name == "keys");
            Assert.True(keys.IsKeys);
            Assert.False(keys.IsKey);

            var keyboard = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.IsEffectivelyVisible
                    && Equals(button.Content, Strings.Get("Add.OpenKeyPad")));
            Assert.NotNull(keyboard);
            Assert.Same(keys, keyboard.DataContext);

            // The sides are named apart on the drawn keyboard, so a combination can say which
            // Ctrl or Shift it means — the same names the trigger bindings use.
            keys.AddKey("左Ctrl");
            keys.AddKey("左Shift");
            keys.AddKey("S");
            Assert.Equal("左Ctrl+左Shift+S", keys.CurrentText);

            // A single key field is the one that is replaced, and it stays that way.
            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();
            var single = viewModel.Parameters.First(parameter => parameter.Definition.Name == "key");
            Assert.True(single.IsKey);
            Assert.False(single.IsKeys);
        });
    }

    [Fact]
    public void A_key_field_offers_the_virtual_keyboard_beside_it()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();

            // The key can be typed, offered as a name, or pointed at on the keyboard drawn on
            // screen; the three live on the same line, so the field never looks like it must be
            // spelled from memory.
            var field = viewModel.Parameters.First(parameter => parameter.Definition.Name == "key");
            Assert.True(field.IsKey);
            Assert.Contains("F5", field.KeyChoices);

            var keyboard = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.IsEffectivelyVisible
                    && Equals(button.Content, Strings.Get("Add.OpenKeyPad")));
            Assert.NotNull(keyboard);
            Assert.Same(field, keyboard.DataContext);

            // Nothing else gets it: a plain text field has no keys to point at.
            viewModel.SelectAction("input.typeText");
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible),
                button => button.DataContext is StepParameterViewModel { IsKey: true });
        });
    }

    [Fact]
    public void The_controls_of_a_controller_are_named_on_the_pad_drawn_on_screen()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("gamepad.button");
            Dispatcher.UIThread.RunJobs();

            var button = viewModel.Parameters.First(parameter => parameter.Definition.Name == "button");
            Assert.True(button.IsGamepadPad);
            Assert.False(viewModel.Parameters.First(parameter => parameter.Definition.Name == "mode")
                .IsGamepadPad);

            var pad = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(item => item.IsEffectivelyVisible
                    && Equals(item.Content, Strings.Get("Add.OpenGamepad")));
            Assert.NotNull(pad);
            Assert.Same(button, pad.DataContext);

            // What the pad hands back is one of the choices this parameter has, and a control it
            // does not know is left alone rather than written into the step.
            button.Choose("y");
            Assert.Equal("y", Value(viewModel, "button"));
            button.Choose("both");
            Assert.Equal("y", Value(viewModel, "button"));

            // A choice that is not a control of a controller — the button of a mouse click — is
            // picked out of its list and carries no pad.
            viewModel.SelectAction("input.mouseClick");
            Dispatcher.UIThread.RunJobs();

            var mouse = viewModel.Parameters.First(parameter => parameter.Definition.Name == "button");
            Assert.False(mouse.IsGamepadPad);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>()
                    .Where(item => item.IsEffectivelyVisible),
                item => item.DataContext is StepParameterViewModel { IsGamepadPad: true });
        });
    }

    // ---------------------------------------------------------------- trying one field

    [Fact]
    public void A_key_field_can_be_tried_on_the_spot()
    {
        Ui.RunAsync(async () =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();

            var key = viewModel.Parameters.First(parameter => parameter.Definition.Name == "key");
            key.Text = "F5";

            var devices = new RecordingDevices();
            window.Devices = devices;

            TestButton(window, key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // The step is tried off the thread that draws the window, so what it sent arrives a
            // moment after the click.
            Assert.True(await devices.WaitUntil(() => devices.Calls.Count >= 1),
                "the trial never reached the devices");
            Assert.Equal("keyPress F5 50", devices.Calls[0]);

            window.Close();
            return true;
        });
    }

    [Fact]
    public void A_controller_control_can_be_tried_on_the_spot()
    {
        Ui.RunAsync(async () =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("gamepad.button");
            Dispatcher.UIThread.RunJobs();

            var button = viewModel.Parameters.First(parameter => parameter.Definition.Name == "button");
            button.Choose("y");

            var devices = new RecordingDevices();
            window.Devices = devices;

            TestButton(window, button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // A tap, then the letting go that keeps a tried button from staying down in the game.
            Assert.True(await devices.WaitUntil(() => devices.Calls.Count >= 3),
                $"the trial sent {string.Join(", ", devices.Calls)}");
            Assert.Equal("gamepadButton y True", devices.Calls[0]);
            Assert.Equal("gamepadButton y False", devices.Calls[1]);
            Assert.Equal("gamepadRelease", devices.Calls[^1]);

            window.Close();
            return true;
        });
    }

    /// <summary>The "test" button that belongs to one parameter, the way the user finds it.</summary>
    private static Button TestButton(Window window, StepParameterViewModel parameter)
        => window.GetVisualDescendants().OfType<Button>()
            .First(item => item.IsEffectivelyVisible
                && Equals(item.Content, Strings.Get("Add.TestField"))
                && ReferenceEquals(item.DataContext, parameter));

    /// <summary>
    /// A device layer with a notebook behind it: what a trial sent is written down, and everything
    /// else is refused the way a machine with no devices would refuse it. It is how the test button
    /// can be checked without a real keyboard, controller or screen being touched.
    /// </summary>
    private sealed class RecordingDevices : IDeviceLayer, IInputDevice, IGamepadDevice
    {
        public RecordingDevices() => Inputs = new SingleInputRouter(this);

        public List<string> Calls { get; } = [];

        public IInputRouter Inputs { get; }

        public IInputDevice Input => this;

        public IGamepadDevice Gamepad => this;

        public IScreenDevice Screen => NullDeviceLayer.Instance.Screen;

        public IVisionDevice Vision => NullDeviceLayer.Instance.Vision;

        public IOcrDevice Ocr => NullDeviceLayer.Instance.Ocr;

        public IUiDevice Ui => NullDeviceLayer.Instance.Ui;

        public IFileDevice Files => NullDeviceLayer.Instance.Files;

        public IClipboardDevice Clipboard => NullDeviceLayer.Instance.Clipboard;

        public IProcessDevice Processes => NullDeviceLayer.Instance.Processes;

        public ISystemDevice System => NullDeviceLayer.Instance.System;

        public IWindowDevice Windows => NullDeviceLayer.Instance.Windows;

        public IBrowserDevice Browser => NullDeviceLayer.Instance.Browser;

        public ScreenPoint Cursor => new(0, 0);

        public void KeyPress(string key, int holdMs) => Calls.Add($"keyPress {key} {holdMs}");

        public void KeyDown(string key) => Calls.Add($"keyDown {key}");

        public void KeyUp(string key) => Calls.Add($"keyUp {key}");

        public void Hotkey(IReadOnlyList<string> keys, int holdMs) => Calls.Add("hotkey");

        public void TypeText(string text, int intervalMs) => Calls.Add("typeText");

        public void MoveMouse(int x, int y, int durationMs) => Calls.Add("moveMouse");

        public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
            => Calls.Add("moveMouseAlong");

        public void MoveMouseRelative(int dx, int dy, int durationMs)
            => Calls.Add("moveMouseRelative");

        public void MouseDown(string button, int x, int y) => Calls.Add("mouseDown");

        public void MouseUp(string button, int x, int y) => Calls.Add("mouseUp");

        public void Click(string button, int x, int y, int clicks, int intervalMs) => Calls.Add("click");

        public void Scroll(string direction, int delta, int x, int y) => Calls.Add("scroll");

        public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs,
            int steps) => Calls.Add("drag");

        public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
            => Calls.Add("dragAlong");

        public void Connect() => Calls.Add("gamepadConnect");

        public void Button(string button, bool down) => Calls.Add($"gamepadButton {button} {down}");

        public void Stick(string stick, int x, int y) => Calls.Add($"gamepadStick {stick} {x} {y}");

        public void Trigger(string trigger, int amount)
            => Calls.Add($"gamepadTrigger {trigger} {amount}");

        public void ReleaseAll() => Calls.Add("gamepadRelease");

        /// <summary>Waits for something to have been sent, so a check can see a click's work.</summary>
        public async Task<bool> WaitUntil(Func<bool> done)
        {
            var step = TimeSpan.FromMilliseconds(25);
            for (var waited = TimeSpan.Zero; waited < TimeSpan.FromSeconds(10); waited += step)
            {
                if (done())
                {
                    return true;
                }

                await Task.Delay(step);
            }

            return done();
        }
    }
}
