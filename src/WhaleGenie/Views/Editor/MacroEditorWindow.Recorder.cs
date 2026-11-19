using WhaleGenie.Localization;

namespace WhaleGenie.Views;

public partial class MacroEditorWindow : AppWindow
{
    // Starting, stopping and timing the recorder. The session owns the recorder and the steps;
    // EditorRecording owns what a recording does to this window and to the page it started from.

    /// <summary>Puts the input hook up when recording starts and takes it down when it stops.</summary>
    private void OnRecordingChanged(bool recording)
    {
        if (recording)
        {
            StartRecorder();
        }
        else
        {
            StopRecorder();
        }
    }

    private void StartRecorder()
    {
        // The recorder comes from the device layer, so a layer handed in by a check records what
        // the check plays into it rather than what a real keyboard is doing.
        if (_recordingRun.Start())
        {
            return;
        }

        _viewModel.StopRecording();
        _ = ConfirmDialog.ShowAsync(this,
            Strings.Get("Editor.RecordActions"),
            Strings.Get("Editor.RecordFailed"),
            Strings.Get("Common.Ok"),
            showCancel: false);
    }

    private void StopRecorder(bool announceEmpty = true)
    {
        if (_recordingRun.Stop() is not { } captured)
        {
            return;
        }

        // Nothing at all is worth explaining: input another program sends is left out on purpose,
        // so a recording over a remote session or through another tool looks empty.
        if (announceEmpty && !_closing && captured == 0)
        {
            _ = ConfirmDialog.ShowAsync(this,
                Strings.Get("Editor.RecordActions"),
                Strings.Get("Editor.RecordEmpty"),
                Strings.Get("Common.Ok"),
                showCancel: false);
        }
    }
}
