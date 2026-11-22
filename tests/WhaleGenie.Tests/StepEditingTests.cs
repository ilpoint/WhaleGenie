using System.Linq;
using WhaleGenie.Editing;
using WhaleGenie.Models;
using Xunit;

namespace WhaleGenie.Tests;

public class StepEditingTests
{
    private static HeadlessEditorSession Open(out ProjectWorkspace workspace)
    {
        workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "steps" });
        return HeadlessEditorSession.Open(workspace, "steps");
    }

    [Fact]
    public void Several_steps_can_be_enabled_or_removed_at_once()
    {
        var session = Open(out _);
        var first = session.AddStep("control.delay");
        var second = session.AddStep("control.note");

        session.SetStepsEnabled([first.Id, second.Id], false);
        var disabled = session.Snapshot();
        Assert.All(disabled.Steps, step => Assert.False(step.Enabled));

        session.RemoveSteps([first.Id, second.Id]);
        Assert.Empty(session.Snapshot().Steps);
    }

    [Fact]
    public void Several_steps_can_be_duplicated()
    {
        var session = Open(out _);
        var first = session.AddStep("control.delay");
        var second = session.AddStep("control.note");

        session.DuplicateSteps([first.Id, second.Id]);
        var steps = session.Snapshot().Steps;
        Assert.Equal(4, steps.Count);
        Assert.Equal(steps.Select(step => step.Id).Distinct().Count(), steps.Count);
    }

    [Fact]
    public void A_stretch_can_be_grouped_into_one_sequence()
    {
        var session = Open(out _);
        var first = session.AddStep("control.delay");
        var second = session.AddStep("control.note");

        session.GroupSteps([first.Id, second.Id]);
        var snapshot = session.Snapshot();
        var group = Assert.Single(snapshot.Steps);
        Assert.Equal("control.sequence", group.Type);
        var section = Assert.Single(group.Sections);
        Assert.Equal(2, section.Steps.Count);
    }

    [Fact]
    public void A_stretch_can_be_lifted_into_a_new_project_macro()
    {
        var session = Open(out var workspace);
        var first = session.AddStep("control.delay");
        var second = session.AddStep("control.note");

        session.LiftSteps([first.Id, second.Id]);
        var snapshot = session.Snapshot();
        var call = Assert.Single(snapshot.Steps);
        Assert.Equal("control.runMacro", call.Type);
        Assert.Equal(2, workspace.Macros.Count);
    }

    [Fact]
    public void Copy_and_paste_round_trips_a_stretch()
    {
        var session = Open(out _);
        var step = session.AddStep("control.delay");

        var text = session.CopySteps([step.Id]);
        Assert.False(string.IsNullOrWhiteSpace(text));
        session.PasteSteps(null, null);
        Assert.Equal(2, session.Snapshot().Steps.Count);
    }

    [Fact]
    public void Drag_to_a_neighbour_moves_the_selected_rows()
    {
        var session = Open(out _);
        var first = session.AddStep("control.delay");
        var second = session.AddStep("control.note");

        session.MoveStepsTo([first.Id], second.Id, false);
        var steps = session.Snapshot().Steps;
        Assert.Equal(first.Id, steps[1].Id);
        Assert.Equal(second.Id, steps[0].Id);
    }
}
