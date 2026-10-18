using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// One capture loop serves two things: the trigger's binding and a key field in the step form.
/// What a captured key writes into is the whole difference, so that is what is checked here.
/// </summary>
public class KeyCaptureTests
{
    [Fact]
    public void A_key_captured_for_the_inspector_goes_to_the_field_that_asked()
    {
        var editor = new MacroEditorViewModel();
        string? written = null;

        editor.BeginKeyCapture(key => written = key, combination: false);
        editor.CaptureKey("F5");

        Assert.Equal("F5", written);
        Assert.Equal(string.Empty, editor.BindKey);
        Assert.False(editor.IsCapturingKey);
    }

    [Fact]
    public void The_trigger_capture_still_writes_the_binding()
    {
        var editor = new MacroEditorViewModel();

        editor.SetKeyCommand.Execute(null);
        editor.CaptureKey("F5");

        Assert.Equal("F5", editor.BindKey);
        Assert.False(editor.IsCapturingKey);
    }

    [Fact]
    public void Starting_a_recording_drops_a_pending_capture()
    {
        var editor = new MacroEditorViewModel();
        var written = false;

        editor.BeginKeyCapture(_ => written = true, combination: false);
        editor.ToggleRecordCommand.Execute(null);
        editor.CaptureKey("F5");

        Assert.False(written);
        Assert.False(editor.IsCapturingKey);
    }
}
