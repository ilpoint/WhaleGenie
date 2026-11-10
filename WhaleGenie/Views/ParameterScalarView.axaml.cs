using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// The fields that hold one value: text, a path, a key, a window rule, a name with variables in
/// it, a number, a colour. What a button means — which picker opens, which window is asked — is a
/// decision about the whole form, so the editor answers it.
/// </summary>
public partial class ParameterScalarView : UserControl
{
    public ParameterScalarView() => InitializeComponent();

    private ActionEditorView? Host => this.FindAncestorOfType<ActionEditorView>();

    private void OnBrowsePath(object? sender, RoutedEventArgs e) => Host?.OnBrowsePath(sender, e);

    private void OnOpenKeyPad(object? sender, RoutedEventArgs e) => Host?.OnOpenKeyPad(sender, e);

    private void OnCaptureKey(object? sender, RoutedEventArgs e) => Host?.OnCaptureKey(sender, e);

    private void OnChooseWindow(object? sender, RoutedEventArgs e) => Host?.OnChooseWindow(sender, e);

    private void OnClearWindowRule(object? sender, RoutedEventArgs e) => Host?.OnClearWindowRule(sender, e);

    private void OnPickVariable(object? sender, RoutedEventArgs e) => Host?.OnPickVariable(sender, e);

    private void OnPickWindow(object? sender, RoutedEventArgs e) => Host?.OnPickWindow(sender, e);

    private void OnPickElement(object? sender, RoutedEventArgs e) => Host?.OnPickElement(sender, e);

    private void OnTestElement(object? sender, RoutedEventArgs e) => Host?.OnTestElement(sender, e);

    private void OnPickBrowserVariable(object? sender, RoutedEventArgs e) => Host?.OnPickBrowserVariable(sender, e);

    private void OnPickBrowserElement(object? sender, RoutedEventArgs e) => Host?.OnPickBrowserElement(sender, e);

    private void OnTestBrowserElement(object? sender, RoutedEventArgs e) => Host?.OnTestBrowserElement(sender, e);

    private void OnAddBrowserWay(object? sender, RoutedEventArgs e) => Host?.OnAddBrowserWay(sender, e);

    private void OnOpenExpressionBuilder(object? sender, RoutedEventArgs e) => Host?.OnOpenExpressionBuilder(sender, e);

    private void OnPickParameterColor(object? sender, RoutedEventArgs e) => Host?.OnPickParameterColor(sender, e);
}
