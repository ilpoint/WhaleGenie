using System.Linq;
using WhaleGenie.Editing;
using WhaleGenie.Models;
using Xunit;

namespace WhaleGenie.Tests;

public class HeadlessActionSessionTests
{
    private static HeadlessEditorSession Open(out ProjectWorkspace workspace)
    {
        workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "actions" });
        return HeadlessEditorSession.Open(workspace, "actions");
    }

    private static EditorActionFieldSnapshot Field(EditorActionFormSnapshot form, string name)
        => form.Rows.SelectMany(row => row.Cells).First(field => field.Name == name);

    [Fact]
    public void Execution_settings_are_written_into_the_step_meta()
    {
        var session = Open(out _);
        var form = session.OpenActionDraft("control.delay");
        session.ActionForm.PatchMeta("timeoutMs", "500", "ms");
        session.ActionForm.PatchMeta("retryCount", "2", "");
        session.ActionForm.PatchMeta("retryDelayMs", "250", "ms");
        session.ActionForm.PatchMeta("delayBeforeMs", "100", "ms");
        session.ActionForm.PatchMeta("delayAfterMs", "50", "ms");
        session.ActionForm.PatchMeta("retryBackoff", "doubling", "");
        session.ActionForm.PatchMeta("onError", "continue", "");

        var after = session.ActionFormState().Meta;
        Assert.Equal(500, after.Timeout.Milliseconds);
        Assert.Equal(2, after.RetryCount);
        Assert.Equal(250, after.RetryDelay.Milliseconds);
        Assert.Equal(100, after.DelayBefore.Milliseconds);
        Assert.Equal(50, after.DelayAfter.Milliseconds);
        Assert.Equal("doubling", after.RetryBackoff);
        Assert.Equal("continue", after.OnError);

        session.ConfirmActionDraft();
        var step = session.Snapshot().Steps.Single();
        Assert.Equal(500, step.TimeoutMs);
        Assert.Equal(2, step.RetryCount);
        Assert.Equal("Continue", step.OnError);
    }

    [Fact]
    public void A_result_is_written_only_after_it_is_ticked_and_named()
    {
        var session = Open(out _);
        var form = session.OpenActionDraft("clipboard.readText");
        var result = form.Returns.First();
        session.ActionForm.PatchReturn(result.Key, ticked: false, name: null);
        Assert.False(session.ActionFormState().Returns.First(row => row.Key == result.Key).Ticked);

        session.ActionForm.PatchReturn(result.Key, ticked: true, name: "found");
        var after = session.ActionFormState().Returns.First(row => row.Key == result.Key);
        Assert.True(after.Ticked);
        Assert.Equal("found", after.Name);

        session.ConfirmActionDraft();
        var step = session.Snapshot().Steps.Single();
        var returns = step.Parameters.First(parameter => parameter.Name == "returns");
        var row = returns.Rows.Single();
        Assert.Equal(result.Key, row.Cells[0]);
        Assert.Equal("found", row.Cells[1]);
    }

    [Fact]
    public void Hotstring_trigger_options_survive_the_round_trip()
    {
        var session = Open(out _);
        session.AddTrigger("Hotstring");
        var trigger = session.Snapshot().Triggers.Single();

        session.SetTriggerHotstring(trigger.Id, "abc");
        session.SetTriggerHotstringOption(trigger.Id, "endChars", ";,.");
        session.SetTriggerHotstringOption(trigger.Id, "insideWord", "true");
        session.SetTriggerHotstringOption(trigger.Id, "keepEnd", "false");

        var after = session.Snapshot().Triggers.Single();
        Assert.Equal(";,.", after.HotstringEndChars);
        Assert.True(after.HotstringInsideWord);
        Assert.False(after.HotstringKeepEnd);
    }

    [Fact]
    public void Recording_options_are_part_of_the_document_snapshot()
    {
        var session = Open(out _);
        session.SetRecordOption("mouseMovement", "false");
        session.SetRecordOption("ignoreInjected", "false");
        session.SetRecordOption("positionCapture", "1");
        session.SetRecordOption("ignoreDelaysBelowMs", "120");

        var recording = session.Snapshot().Recording;
        Assert.False(recording.MouseMovement);
        Assert.False(recording.IgnoreInjected);
        Assert.Equal(1, recording.PositionIndex);
        Assert.Equal(120, recording.IgnoreDelaysBelowMs);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void A_window_rule_can_be_applied_to_a_draft_field()
    {
        var session = Open(out _);
        var form = session.OpenActionDraft("window.activate");
        var field = form.Rows.SelectMany(row => row.Cells).First(cell => cell.IsWindowRule);
        const string rule = "{\"title\":\"Notepad\",\"process\":\"notepad\",\"class\":\"Notepad\",\"handle\":0,\"how\":\"TitleContains\",\"caseSensitive\":false,\"trim\":true,\"multiline\":false}";

        session.ActionForm.PatchWindow(field.Name, "rule", rule);
        var after = Field(session.ActionFormState(), field.Name);
        Assert.False(string.IsNullOrEmpty(after.WindowRuleSummary));
        Assert.False(after.IsFollowingWindow);
    }
}
