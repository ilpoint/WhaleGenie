using System.Linq;
using WhaleGenie.Editing;
using WhaleGenie.Models;
using Xunit;

namespace WhaleGenie.Tests;

public class ActionEditorTests
{
    private static HeadlessEditorSession Open(out ProjectWorkspace workspace)
    {
        workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "actions" });
        return HeadlessEditorSession.Open(workspace, "actions");
    }

    [Fact]
    public void An_action_form_is_a_draft_until_it_is_confirmed()
    {
        var session = Open(out _);
        var form = session.OpenActionDraft("control.delay");

        Assert.True(form.Open);
        Assert.False(form.IsEditing);
        Assert.True(form.CanConfirm);
        Assert.Empty(session.Snapshot().Steps);
        Assert.False(session.IsDirty);

        session.PatchParameter(form.StepId, "ms", "750");
        Assert.Equal("750", session.ActionFormState().Step!.Parameters
            .Single(parameter => parameter.Name == "ms").Value);
        Assert.Empty(session.Snapshot().Steps);

        var after = session.ConfirmActionDraft();
        Assert.Single(after.Steps);
        Assert.True(session.IsDirty);
        Assert.False(session.ActionFormState().Open);
    }

    [Fact]
    public void Cancelling_an_action_form_leaves_the_tree_untouched()
    {
        var session = Open(out _);
        session.OpenActionDraft("control.delay");
        session.CancelActionDraft();

        Assert.Empty(session.Snapshot().Steps);
        Assert.False(session.IsDirty);
        Assert.False(session.ActionFormState().Open);
    }

    [Fact]
    public void Editing_is_committed_only_by_confirming()
    {
        var session = Open(out _);
        var step = session.AddStep("control.delay");
        session.PatchParameter(step.Id, "ms", "500");

        var form = session.OpenStepDraft(step.Id);
        Assert.True(form.IsEditing);
        session.PatchParameter(form.StepId, "ms", "900");
        session.CancelActionDraft();
        Assert.Equal("500", session.Snapshot().Steps.Single().Parameters
            .Single(parameter => parameter.Name == "ms").Value);

        session.OpenStepDraft(step.Id);
        session.PatchParameter(step.Id, "ms", "900");
        session.ConfirmActionDraft();
        Assert.Equal("900", session.Snapshot().Steps.Single().Parameters
            .Single(parameter => parameter.Name == "ms").Value);
    }

    [Fact]
    public void A_draft_can_be_inserted_before_a_drop_target()
    {
        var session = Open(out _);
        var first = session.AddStep("control.delay");
        var second = session.AddStep("control.note");
        var form = session.OpenActionDraft("control.delay", targetStepId: second.Id, before: true);
        session.ConfirmActionDraft();

        var steps = session.Snapshot().Steps;
        Assert.Equal(3, steps.Count);
        Assert.Equal(second.Id, steps[2].Id);
        Assert.NotEqual(first.Id, steps[1].Id);
    }

    [Fact]
    public void Required_fields_keep_the_confirm_button_disabled()
    {
        var session = Open(out _);
        var form = session.OpenActionDraft("vision.findImage");

        Assert.False(form.CanConfirm);
        Assert.Contains("图片", form.ValidationMessage);
    }

    [Fact]
    public void The_value_window_can_edit_a_draft_field()
    {
        var session = Open(out _);
        var form = session.OpenActionDraft("control.delay");
        session.OpenValue(form.StepId, "ms");
        session.ValueCommand(new ValueEditorCommand("clear"));
        session.ValueCommand(new ValueEditorCommand("place", Token: "750"));
        session.ConfirmValue();

        Assert.Equal("750", session.ActionFormState().Step!.Parameters
            .Single(parameter => parameter.Name == "ms").Value);
        Assert.Empty(session.Snapshot().Steps);
    }
}
