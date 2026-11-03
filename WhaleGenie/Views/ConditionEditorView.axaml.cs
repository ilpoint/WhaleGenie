using Avalonia.Controls;

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
}
