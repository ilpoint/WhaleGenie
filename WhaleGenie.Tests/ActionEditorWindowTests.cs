using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

            Assert.Empty(window.OwnedWindows);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ItemsControl>(),
                control => ReferenceEquals(control.ItemsSource, list.Steps));
            window.Close();
        });
    }

    [Fact]
    public void The_action_window_does_not_repeat_the_technical_action_key()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [],
                "control.repeat");
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == "control.repeat");
            window.Close();
        });
    }

    [Fact]
    public void A_refused_placement_keeps_the_form_open_with_its_reason()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [],
                "control.break")
            {
                PlaceStep = (_, _) => "This list does not take that step.",
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var form = (AddActionViewModel)window.DataContext!;
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.IsVisible);
            Assert.True(form.ShowsWarning);
            Assert.Equal("This list does not take that step.", form.Notice);
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
    public void A_block_condition_is_edited_as_one_expression()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [],
                "control.if");
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var form = (AddActionViewModel)window.DataContext!;
            var condition = form.Parameters.Single(parameter =>
                parameter.Definition.Name == "condition");
            Assert.True(condition.IsExpression);
            Assert.False(condition.IsNested);
            window.Close();
        });
    }

    /// <summary>
    /// A picker puts every window of this program away while it is out, the window a step is being
    /// written in included — and that is exactly how a reference picture is taken. Taking the
    /// window off the screen ends the modal loop it is shown in, so waiting on that loop would be
    /// over before the step is written and the saved step would go nowhere. What the caller waits
    /// on is the window itself.
    /// </summary>
    [Fact]
    public void A_step_is_still_answered_after_a_picker_has_put_the_window_aside()
    {
        Ui.RunAsync(async () =>
        {
            var owner = new Window();
            owner.Show();

            var dialog = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [],
                "vision.findImage");
            var loop = dialog.ShowDialogOver<MacroStep?>(owner);

            using (AppWindows.StepAside([owner, dialog], dialog))
            {
            }

            Dispatcher.UIThread.RunJobs();

            // The loop is over and it brought nothing back, while the window is still standing
            // there with the step half written.
            Assert.True(loop.IsCompleted, "the modal loop outlived the window being put aside");
            Assert.Null(await loop);
            Assert.True(dialog.IsVisible);

            var form = (AddActionViewModel)dialog.DataContext!;
            var pictures = form.Parameters.First(parameter => parameter.Definition.Name == "images");
            pictures.AddPicture();
            pictures.Pictures[0].Text = @"C:\images\ok.png";
            Dispatcher.UIThread.RunJobs();

            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var step = await dialog.Answer;
            Assert.Equal("vision.findImage", step?.Type);
            Assert.False(dialog.IsVisible);
            owner.Close();
            return true;
        });
    }
}
