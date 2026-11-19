using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WhaleGenie.Views;

/// <summary>
/// The steps of the macro being edited: one tab per document, the list, and the right-click menu
/// on it. The window keeps the step list's drag state and the run buttons; this view is the middle
/// column they act on.
/// </summary>
public partial class MacroDocumentView : UserControl
{
    public MacroDocumentView() => InitializeComponent();

    private MacroEditorWindow? Host => this.FindAncestorOfType<MacroEditorWindow>();

    private void OnDocumentTabChanged(object? sender, SelectionChangedEventArgs e) => Host?.OnDocumentTabChanged(sender, e);

    private void OnDocumentTabClosePressed(object? sender, PointerPressedEventArgs e) => Host?.OnDocumentTabClosePressed(sender, e);

    private void OnCloseDocumentTab(object? sender, RoutedEventArgs e) => Host?.OnCloseDocumentTab(sender, e);

    private void OnExportDocumentClicked(object? sender, RoutedEventArgs e) => Host?.OnExportDocumentClicked(sender, e);

    private void OnStepContextEdit(object? sender, RoutedEventArgs e) => Host?.OnStepContextEdit(sender, e);

    private void OnStepContextCopy(object? sender, RoutedEventArgs e) => Host?.OnStepContextCopy(sender, e);

    private void OnStepContextCut(object? sender, RoutedEventArgs e) => Host?.OnStepContextCut(sender, e);

    private void OnStepContextPaste(object? sender, RoutedEventArgs e) => Host?.OnStepContextPaste(sender, e);

    private void OnStepContextDelete(object? sender, RoutedEventArgs e) => Host?.OnStepContextDelete(sender, e);

    private void OnStepContextMoveUp(object? sender, RoutedEventArgs e) => Host?.OnStepContextMoveUp(sender, e);

    private void OnStepContextMoveDown(object? sender, RoutedEventArgs e) => Host?.OnStepContextMoveDown(sender, e);

    private void OnStepContextGroup(object? sender, RoutedEventArgs e) => Host?.OnStepContextGroup(sender, e);

    private void OnStepContextLift(object? sender, RoutedEventArgs e) => Host?.OnStepContextLift(sender, e);

    private void OnStepContextToggleEnabled(object? sender, RoutedEventArgs e) => Host?.OnStepContextToggleEnabled(sender, e);

    private void OnAddBranch(object? sender, RoutedEventArgs e) => Host?.OnAddBranch(sender, e);

    private void OnEditCaseValues(object? sender, RoutedEventArgs e) => Host?.OnEditCaseValues(sender, e);

    private void OnPickCaseValuesVariable(object? sender, RoutedEventArgs e) => Host?.OnPickCaseValuesVariable(sender, e);

    private void OnCaseValuesKeyDown(object? sender, KeyEventArgs e) => Host?.OnCaseValuesKeyDown(sender, e);

    private void OnCaseValuesLostFocus(object? sender, RoutedEventArgs e) => Host?.OnCaseValuesLostFocus(sender, e);

    private void OnCaseValueBoxAttached(object? sender, VisualTreeAttachmentEventArgs e) => Host?.OnCaseValueBoxAttached(sender, e);
}
