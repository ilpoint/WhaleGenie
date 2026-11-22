using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One entry of the action library's category rail: the category it narrows to (null for "all"),
/// its label, and the short form.
/// </summary>
/// <remarks>
/// It is observable rather than a record because the rail has to say which one is in force: five
/// buttons that look the same leave the reader unable to say which category the wall below belongs
/// to. The palette sets exactly one flag, the same way the run window's variable page does.
/// </remarks>
public sealed partial class ActionCategoryChoice : ObservableObject
{
    public ActionCategoryChoice(ActionCategory? value, string label, string @short)
    {
        Value = value;
        Label = label;
        Short = @short;
    }

    public ActionCategory? Value { get; }

    public string Label { get; }

    public string Short { get; }

    /// <summary>True on the one category the wall below is showing right now.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
