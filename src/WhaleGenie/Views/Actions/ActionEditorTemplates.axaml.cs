using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// The templates a parameter row is built from. They live beside the view rather than inside it
/// because the row list is drawn from them; the one button among them asks the form the row is on,
/// which is reached through the control that was clicked.
/// </summary>
public partial class ActionEditorTemplates : ResourceDictionary
{
    private void OnOpenCellExpression(object? sender, RoutedEventArgs e) =>
        (sender as Control)?.FindAncestorOfType<ActionEditorView>()?.OnOpenCellExpression(sender, e);
}
