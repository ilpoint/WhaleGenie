using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The window steps are written in, as the editor opens it: it stays up from one step to the next
/// so the list behind it can still be read and picked from, and the step it hands over goes to
/// wherever the editor is pointing at that moment.
/// </summary>
public class AddStepWindowTests
{
    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    private static StepParameter Body(string name, params MacroStep[] steps)
        => new() { Name = name, Kind = ActionParameterKind.Steps, Steps = [.. steps] };

    private static IList<MacroStep> Inside(MacroStep block)
        => block.StepLists.Single().Steps;

    private static (MacroEditorWindow Window, MacroEditorViewModel Editor, AddActionWindow Picker)
        OpenEditor()
    {
        var macro = new MacroItem { Name = "probe" };
        var window = new MacroEditorWindow(macro, [macro]);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var editor = (MacroEditorViewModel)window.DataContext!;
        editor.NewStepCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var picker = window.OwnedWindows.OfType<AddActionWindow>().Single();
        return (window, editor, picker);
    }

    private static AddActionViewModel Form(AddActionWindow window)
        => (AddActionViewModel)window.DataContext!;

    [Fact]
    public void The_window_stays_up_from_one_step_to_the_next()
    {
        Ui.Run(() =>
        {
            var (window, editor, picker) = OpenEditor();
            var form = Form(picker);

            form.SelectAction("input.keyPress");
            form.Parameters.First(parameter => parameter.Definition.Name == "key").Text = "F5";
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // The step is in the list, the window is still up, and the form is back to the action
            // it was on with nothing in it.
            Assert.Equal(["input.keyPress"], editor.Steps.Select(step => step.Type));
            Assert.Contains(picker, window.OwnedWindows.OfType<AddActionWindow>());
            Assert.False(form.IsEditing);
            Assert.Equal("input.keyPress", form.SelectedDefinition!.Key);
            Assert.Equal(string.Empty,
                form.Parameters.First(parameter => parameter.Definition.Name == "key").Text);

            // The next step is written in that same window and gets a name of its own.
            form.SelectAction("control.log");
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["input.keyPress", "control.log"], editor.Steps.Select(step => step.Type));
            Assert.Equal(2, editor.Steps.Select(step => step.Id).Distinct().Count());
        });
    }

    [Fact]
    public void Each_step_written_here_is_given_a_name_of_its_own()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions, [], []) { StaysOpen = true };
            var minted = new Queue<string>(["k3f9", "t7wq"]);
            window.MintStepId = () => minted.Dequeue();

            window.BeginStep(null);
            Assert.Equal("k3f9", Form(window).StepId);

            window.BeginStep(null);
            Assert.Equal("t7wq", Form(window).StepId);
        });
    }

    [Fact]
    public void Clearing_the_form_leaves_the_window_up()
    {
        Ui.Run(() =>
        {
            var (window, editor, picker) = OpenEditor();
            var form = Form(picker);

            form.SelectAction("control.log");
            form.MetaComment = "half written";
            form.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(picker, window.OwnedWindows.OfType<AddActionWindow>());
            Assert.Empty(editor.Steps);
            Assert.Equal(string.Empty, form.MetaComment);
        });
    }

    [Fact]
    public void The_step_goes_where_the_selection_is_when_it_is_handed_over()
    {
        Ui.Run(() =>
        {
            var (_, editor, picker) = OpenEditor();
            var form = Form(picker);

            var first = Step("control.delay");
            var inner = Step("control.delay");
            var loop = Step("control.repeat", Body("body", inner));
            editor.AddStep(first);
            editor.AddStep(loop);
            editor.SetSelection([first]);
            Dispatcher.UIThread.RunJobs();

            form.SelectAction("control.log");

            // The list is still being worked on while the window is up: picking the step inside the
            // block is the user saying where the step being written is to go.
            editor.SetSelection([inner]);
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["control.delay", "control.log"], Inside(loop).Select(step => step.Type));
            Assert.Equal(["control.delay", "control.repeat"], editor.Steps.Select(step => step.Type));
        });
    }

    [Fact]
    public void A_step_that_the_place_will_not_take_is_refused_with_a_reason()
    {
        Ui.Run(() =>
        {
            var (window, editor, picker) = OpenEditor();
            var form = Form(picker);

            var branch = Step("control.case", Body("body", Step("control.delay")));
            var choice = Step("control.switch",
                new StepParameter { Name = "cases", Kind = ActionParameterKind.Steps, Steps = [branch] });
            editor.AddStep(choice);
            editor.SetSelection([choice]);
            Dispatcher.UIThread.RunJobs();

            form.SelectAction("control.log");

            // A branch only goes inside a switch, and the place picked now is a branch's own list:
            // nothing is added, and the line under the form says why.
            editor.SetSelection([branch]);
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(form.ShowsWarning);
            Assert.Single(editor.Steps);
            Assert.Equal(["control.delay"], Inside(branch).Select(step => step.Type));
            Assert.Contains(picker, window.OwnedWindows.OfType<AddActionWindow>());

            // What the form offers follows the place, so the next attempt can be filled in.
            Assert.DoesNotContain(form.AvailableActions, action => action.Key == "control.log");
            Assert.Contains(form.AvailableActions, action => action.Key == "control.case");
        });
    }

    [Fact]
    public void Editing_a_step_reuses_the_window_that_is_already_up()
    {
        Ui.Run(() =>
        {
            var (window, editor, picker) = OpenEditor();

            var step = Step("input.keyPress",
                new StepParameter { Name = "key", Kind = ActionParameterKind.Text, Value = "F5" });
            editor.AddStep(step);
            editor.SetSelection([step]);
            Dispatcher.UIThread.RunJobs();

            editor.EditSelectedCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // The same window, now opened on that step rather than on a new one.
            Assert.Same(picker, window.OwnedWindows.OfType<AddActionWindow>().Single());
            var form = Form(picker);
            Assert.True(form.IsEditing);
            Assert.Equal("input.keyPress", form.SelectedDefinition!.Key);
            Assert.Equal("F5", form.Parameters.First(parameter => parameter.Definition.Name == "key").Text);

            form.Parameters.First(parameter => parameter.Definition.Name == "key").Text = "F6";
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // The edited step took the place of the old one, and the window went back to writing
            // the next new step.
            Assert.Equal("F6", editor.Steps.Single().Parameters.First(parameter => parameter.Name == "key").Value);
            Assert.False(form.IsEditing);
        });
    }
}
