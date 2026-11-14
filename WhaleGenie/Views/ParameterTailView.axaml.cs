using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// The fields at the end of a parameter: a switch, a list of choices, a variable, a step of the
/// macro, a control of the pad, and the conditions a branch is written under. What a picker does
/// with the value belongs to the editor that opened the form.
/// </summary>
public partial class ParameterTailView : UserControl
{
    public ParameterTailView() => InitializeComponent();

    private ActionEditorView? Host => this.FindAncestorOfType<ActionEditorView>();

    private void OnBrowsePath(object? sender, RoutedEventArgs e) => Host?.OnBrowsePath(sender, e);

    private void OnOpenValuePicker(object? sender, RoutedEventArgs e) => Host?.OnOpenValuePicker(sender, e);

    private void OnPickVariable(object? sender, RoutedEventArgs e) => Host?.OnPickVariable(sender, e);

    private void OnOpenGamepadPad(object? sender, RoutedEventArgs e) => Host?.OnOpenGamepadPad(sender, e);
}
