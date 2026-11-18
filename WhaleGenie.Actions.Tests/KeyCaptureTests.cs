using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The editor's own capture loop answers every binding the editor itself asks for: the trigger's,
/// the interface hotkey's, and whatever else hands it somewhere to write. A step form keeps its own,
/// on the window the form stands in. What a captured key writes into is the whole difference, so
/// that is what is checked here.
/// </summary>
public class KeyCaptureTests
{
    [Fact]
    public void A_key_captured_for_a_field_goes_to_the_field_that_asked()
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
    public void A_key_run_row_takes_the_whole_captured_chord()
    {
        var row = new KeyRowViewModel { Keys = "Ctrl" };

        row.WriteKey("Ctrl+Shift+S");

        Assert.Equal("Ctrl+Shift+S", row.Keys);
    }

    [Fact]
    public void A_key_run_row_collects_several_keys_into_one_combination()
    {
        var row = new KeyRowViewModel();

        row.StartCapture();
        row.AddKey("3");
        row.AddKey("4");
        row.AddKey("5");
        row.IsCapturing = false;

        Assert.Equal("3+4+5", row.Keys);
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
