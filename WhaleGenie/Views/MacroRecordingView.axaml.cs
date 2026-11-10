using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// The recorder shown in place of the step list while a macro is being recorded. The window
/// decides when it takes over and what the button closes.
/// </summary>
public partial class MacroRecordingView : UserControl
{
    public MacroRecordingView() => InitializeComponent();

    private MacroEditorWindow? Host => this.FindAncestorOfType<MacroEditorWindow>();

    private void OnCloseRecording(object? sender, RoutedEventArgs e) => Host?.OnCloseRecording(sender, e);
}
