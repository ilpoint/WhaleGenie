using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class ActionEditorWindowTests
{
    [Fact]
    public void The_action_form_is_one_resizable_window()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            Assert.IsType<ActionEditorView>(window.Content);
            Assert.True(window.CanResize);
            Assert.False(window.StaysOpen);
            Assert.Equal(WindowDecorations.BorderOnly, window.WindowDecorations);
            window.Close();
        });
    }

    [Fact]
    public void The_form_can_be_preset_to_call_a_named_macro()
    {
        Ui.Run(() =>
        {
            var called = new MacroCallable("整理", [], []);
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [called]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.SetMacroCall("整理");

            var form = Assert.IsType<AddActionViewModel>(window.DataContext);
            Assert.Equal("control.runMacro", form.SelectedDefinition!.Key);
            Assert.Equal("整理",
                form.Parameters.Single(parameter => parameter.Definition.Name == "macro").Text);
            window.Close();
        });
    }

    [Fact]
    public void The_old_action_picker_can_be_hidden_when_the_library_already_chose_the_action()
    {
        Ui.Run(() =>
        {
            var view = new ActionEditorView();
            view.ShowPicker = false;

            Assert.False(view.FindControl<Grid>("PickerColumn")!.IsVisible);
            Assert.Equal(0, view.FindControl<Grid>("FormBody")!
                .ColumnDefinitions[0].Width.Value);
        });
    }

    [Fact]
    public void A_required_call_argument_can_be_saved_as_an_explicit_empty_value()
    {
        Ui.Run(() =>
        {
            var called = new MacroCallable("子",
                [new MacroParameter { Name = "数量", Required = true }], []);
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [called]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var form = (AddActionViewModel)window.DataContext!;
            form.SelectAction("control.runMacro");
            form.Parameters.First(parameter => parameter.Definition.Name == "macro").Text = "子";
            Dispatcher.UIThread.RunJobs();

            var argument = form.Parameters.First(parameter =>
                parameter.Definition.Name == MacroCallFields.Argument("数量"));
            var given = form.Parameters.First(parameter =>
                parameter.Definition.Name == MacroCallFields.Given("数量"));

            Assert.False(form.CanSave);
            given.Flag = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(form.CanSave, form.ValidationMessage);
            Assert.Equal(string.Empty, argument.Text);
            window.Close();
        });
    }

    [Fact]
    public void A_container_does_not_open_a_second_substep_window_from_its_form()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var form = (AddActionViewModel)window.DataContext!;
            form.SelectAction("control.repeat");
            Dispatcher.UIThread.RunJobs();

            var list = form.Parameters.Single(parameter =>
                parameter.Definition.Kind == ActionParameterKind.Steps).List!;
            list.AddCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(window.OwnedWindows);
            window.Close();
        });
    }

    [Fact]
    public void A_new_container_keeps_its_empty_child_list_and_starts_open()
    {
        var form = new AddActionViewModel(ActionCatalog.Definitions, [], []);
        form.SelectAction("control.repeat");

        var step = form.BuildStep();

        Assert.True(step.IsExpanded);
        Assert.Equal("body", Assert.Single(step.StepLists).Name);
        Assert.Empty(step.StepLists.Single().Steps);
    }

    [Fact]
    public void A_condition_group_can_add_more_than_one_condition()
    {
        var form = new AddActionViewModel(ActionCatalog.Conditions, [], []);
        form.SelectAction("condition.group");
        var editor = form.Parameters.Single(parameter => parameter.IsConditionList);
        var asked = 0;
        form.NestedAddRequested += _ => asked++;

        Assert.False(editor.List!.IsCondition);
        Assert.True(editor.List.AcceptsConditions);
        Assert.True(editor.List.HasNoSteps);
        editor.List.AddCommand.Execute(null);

        Assert.Equal(1, asked);
    }

    [Fact]
    public void An_empty_condition_shows_a_large_add_condition_button()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [],
                "control.if");
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible
                    && text.Text == Strings.Get("Add.NoCondition"));
            var add = window.GetVisualDescendants().OfType<Button>().Single(button =>
                button.IsEffectivelyVisible
                && button.Classes.Contains("AddButton")
                && button.DataContext is StepParameterViewModel { IsConditionList: true });
            Assert.Equal(34, add.Height);
            Assert.Contains(add.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == Strings.Get("Add.NestedCondition"));
            window.Close();
        });
    }
}
