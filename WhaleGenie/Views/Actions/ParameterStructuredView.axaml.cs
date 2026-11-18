using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// The fields that hold a list of things: pictures, regions, the colours around one, a run of key
/// combinations, and the narrowing conditions of a page element. Each row is edited in place; the
/// pickers and captures behind the buttons belong to the editor that opened the form.
/// </summary>
public partial class ParameterStructuredView : UserControl
{
    public ParameterStructuredView() => InitializeComponent();

    private ActionEditorView? Host => this.FindAncestorOfType<ActionEditorView>();

    private void OnOpenCellExpression(object? sender, RoutedEventArgs e) => Host?.OnOpenCellExpression(sender, e);

    private void OnBrowseImage(object? sender, RoutedEventArgs e) => Host?.OnBrowseImage(sender, e);

    private void OnCaptureImage(object? sender, RoutedEventArgs e) => Host?.OnCaptureImage(sender, e);

    private void OnClearImage(object? sender, RoutedEventArgs e) => Host?.OnClearImage(sender, e);

    private void OnAddImageRow(object? sender, RoutedEventArgs e) => Host?.OnAddImageRow(sender, e);

    private void OnBrowseImageRow(object? sender, RoutedEventArgs e) => Host?.OnBrowseImageRow(sender, e);

    private void OnCaptureImageRow(object? sender, RoutedEventArgs e) => Host?.OnCaptureImageRow(sender, e);

    private void OnAddRegionRow(object? sender, RoutedEventArgs e) => Host?.OnAddRegionRow(sender, e);

    private void OnPickRegionRow(object? sender, RoutedEventArgs e) => Host?.OnPickRegionRow(sender, e);

    private void OnPickRegionVariable(object? sender, RoutedEventArgs e) => Host?.OnPickRegionVariable(sender, e);

    private void OnAddColourPoint(object? sender, RoutedEventArgs e) => Host?.OnAddColourPoint(sender, e);

    private void OnPickColourPoints(object? sender, RoutedEventArgs e) => Host?.OnPickColourPoints(sender, e);

    private void OnAddKeyRow(object? sender, RoutedEventArgs e) => Host?.OnAddKeyRow(sender, e);

    private void OnPickKeyRow(object? sender, RoutedEventArgs e) => Host?.OnPickKeyRow(sender, e);

    private void OnCaptureKeyRow(object? sender, RoutedEventArgs e) => Host?.OnCaptureKeyRow(sender, e);

    private void OnAddNarrowRow(object? sender, RoutedEventArgs e) => Host?.OnAddNarrowRow(sender, e);

    private void OnPickNarrowValue(object? sender, RoutedEventArgs e) => Host?.OnPickNarrowValue(sender, e);

    private void OnLookNarrowRow(object? sender, RoutedEventArgs e) => Host?.OnLookNarrowRow(sender, e);
}
