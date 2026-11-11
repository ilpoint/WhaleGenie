using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.ViewModels;

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

    /// <summary>
    /// Remembers where the person was, so a name picked from the wall lands where they were
    /// writing rather than at the end of the value.
    /// </summary>
    private void OnPartLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TextPart part } box
            && DataContext is StepParameterViewModel field)
        {
            field.RememberPlace(part, box.CaretIndex);
        }
    }

    /// <summary>
    /// Backspace at the very start of a run and Delete at its very end reach across pieces: the
    /// name beside it goes, and the two runs of text join where it stood.
    /// </summary>
    private void OnPartKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TextPart part } box
            || DataContext is not StepParameterViewModel field)
        {
            return;
        }

        var end = box.Text?.Length ?? 0;
        var place = e.Key switch
        {
            Key.Back when box.CaretIndex == 0 => field.JoinBack(part),
            Key.Delete when box.CaretIndex >= end => field.JoinForward(part),
            _ => null,
        };

        if (place is not { } moved)
        {
            return;
        }

        e.Handled = true;
        FocusPart(moved.Part, moved.Caret);
    }

    /// <summary>Puts the caret back in the run the edit landed in, once the rows have been rebuilt.</summary>
    private void FocusPart(ValuePart part, int caret)
        => Dispatcher.UIThread.Post(() =>
        {
            var box = this.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(candidate => ReferenceEquals(candidate.DataContext, part));
            if (box is null)
            {
                return;
            }

            box.Focus();
            box.CaretIndex = Math.Clamp(caret, 0, box.Text?.Length ?? 0);
        });

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
