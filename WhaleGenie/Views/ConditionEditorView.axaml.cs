using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// A block's condition, as a table of comparisons or as a tree of them. Both views are bound to one
/// view model, so the switch at the top changes the template rather than the condition.
/// </summary>
public partial class ConditionEditorView : UserControl
{
    public ConditionEditorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Notes which side of the row the caret went into. The two buttons at the end of a row fill one
    /// cell, and this is how they know which: the cell a person was just typing in is the one they
    /// mean, and the left one is what a comparison starts from when nobody has typed yet.
    /// </summary>
    private void OnCellFocused(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ConditionNodeViewModel node } cell)
        {
            node.ActiveRight = (cell.Tag as string) == "right";
        }
    }

    /// <summary>
    /// The one window a cell opens, from the button inside it. The button belongs to the cell, so
    /// which side is filled is read from the button rather than guessed from the last one focused.
    /// </summary>
    private async void OnPickVariable(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ConditionNodeViewModel node } button
            || DataContext is not ConditionEditorViewModel editor
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        await OpenValue(node, editor, owner, (button.Tag as string) == "right", startOnFormula: true);
    }

    /// <summary>
    /// The one window every cell of a condition opens: it is handed the cell as it stands, the
    /// caret where it was, and gives back the cell as it should be. A name and a formula are the
    /// same thing here, so both buttons come through here and neither is a different window.
    /// </summary>
    private async Task OpenValue(ConditionNodeViewModel node, ConditionEditorViewModel editor,
        Window owner, bool right, bool startOnFormula)
    {
        var seed = right ? node.Right : node.Left;
        if (await ValuePickerWindow.ShowFor(owner, seed, editor.Variables,
                startOnFormula: startOnFormula, startCaret: CaretIn(node) ?? -1) is { } written)
        {
            editor.WriteExpression(node, right, written);
        }
    }

    /// <summary>
    /// Where the caret is, when it is in the row the button belongs to. A row whose box was never
    /// focused has no caret to speak of, and the name goes on the end.
    /// </summary>
    private int? CaretIn(ConditionNodeViewModel node)
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is AutoCompleteBox box
            && ReferenceEquals(box.DataContext, node))
        {
            return box.CaretIndex;
        }

        return null;
    }
}
