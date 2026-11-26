using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Expressions;
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
    public void An_unsaved_action_still_has_a_capture_folder()
    {
        var session = Open(out _);
        session.OpenActionDraft("vision.capture");
        var request = session.ActionForm.PreparePick("path", -1, "capture");

        Assert.NotNull(request);
        Assert.False(string.IsNullOrWhiteSpace(request!.AssetFolder));
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
    public void Hotstring_end_keys_toggle_and_keep_a_captured_key()
    {
        var session = Open(out _);
        session.AddTrigger("Hotstring");
        var trigger = session.Snapshot().Triggers.Single();
        session.SetTriggerHotstring(trigger.Id, "abc");

        var space = session.Snapshot().Triggers.Single().HotstringEndKeys
            .First(key => key.Value == " ");
        session.SetTriggerHotstringEndKey(trigger.Id, " ", false);
        var after = session.Snapshot().Triggers.Single();
        Assert.False(after.HotstringEndKeys.First(key => key.Value == " ").Checked);

        session.CaptureTriggerHotstringEnd(trigger.Id, "~");
        after = session.Snapshot().Triggers.Single();
        Assert.Contains(after.HotstringEndKeys, key => key.Value == "~" && key.IsCustom && key.Checked);

        session.ResetTriggerHotstringEndKeys(trigger.Id);
        after = session.Snapshot().Triggers.Single();
        Assert.DoesNotContain(after.HotstringEndKeys, key => key.IsCustom);
    }

    [Fact]
    public void Interface_hotkey_is_saved_for_the_window_group()
    {
        var session = Open(out _);
        session.AddTrigger("Interface");
        var trigger = session.Snapshot().Triggers.Single();
        session.SetTriggerInterface(trigger.Id, "title", "Notepad");
        session.SetTriggerInterfaceHotkey(trigger.Id, "Ctrl+F8");

        var after = session.Snapshot().Triggers.Single();
        Assert.Equal("Ctrl+F8", after.InterfaceHotkey);
        Assert.Empty(after.InterfaceHotkeyProblem);
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

    [Fact]
    public void A_row_cell_keeps_a_variable_when_the_page_sends_its_parts()
    {
        // The page sends the shape a cell is made of, not the line it is drawn as. A name that went
        // through the line as text is how a region stopped pointing at the variable it named.
        var session = Open(out _);
        session.OpenActionDraft("vision.findImage");
        session.ActionForm.AddRow("region");
        session.ActionForm.PatchRow("region", 0, "variable", string.Empty,
            [new EditorValuePartSnapshot("name", string.Empty, "区域")]);

        var step = session.ActionForm.BuildStepForSave();
        var region = step.Parameters.First(parameter => parameter.Name == "region");
        Assert.IsType<VariableNode>(region.Rows.Single().Cell("text"));
    }
}
