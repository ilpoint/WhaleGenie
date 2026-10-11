using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Recording;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The settings a step starts with when the editor made it rather than the user writing it. A step
/// written in the add-action dialog opens with "ask me"; the block Ctrl + G throws around a
/// selection and a step that was just recorded are made by the editor itself, and they have to come
/// out the same way, or one macro behaves differently depending on how each of its steps got there.
/// </summary>
public class StepDefaultsTests
{
    [Fact]
    public void A_group_thrown_around_a_selection_asks_when_it_fails()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var first = new MacroStep { Type = "control.log" };
            var second = new MacroStep { Type = "control.delay" };
            editor.AddStep(first);
            editor.AddStep(second);
            editor.SetSelection([first, second]);

            editor.GroupSelectedCommand.Execute(null);

            var group = Assert.Single(editor.Steps);
            Assert.Equal("control.sequence", group.Type);
            Assert.Equal(StepErrorAction.AskUser, group.Meta.OnError);

            // The step list has to say so as well: a step that would bring the whole macro down on
            // its own is what the user has to be able to see before running it.
            Assert.Contains(Strings.Get("Editor.Meta.OnErrorAsk"), group.MetaSummary);
        });
    }

    [Fact]
    public void A_recorded_step_asks_when_it_fails()
        => Assert.Equal(StepErrorAction.AskUser,
            new RecordedAction(RecordedActionKind.KeyPress, Key: "Ctrl+C").ToStep().Meta.OnError);
}
