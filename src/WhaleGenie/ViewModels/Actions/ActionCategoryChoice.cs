using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One entry of the action library's category rail: the category it narrows to (null for "all"),
/// and how the rail draws it.
/// </summary>
/// <remarks>
/// It is observable rather than a record because the rail has to say which one is in force: five
/// buttons that look the same leave the reader unable to say which category the wall below belongs
/// to. The palette sets exactly one flag, the same way the run window's variable page does.
/// </remarks>
public sealed partial class ActionCategoryChoice : ObservableObject
{
    public ActionCategoryChoice(ActionCategory? value, string label, string @short,
        Lucide.Avalonia.LucideIconKind icon, IBrush accent)
    {
        Value = value;
        Label = label;
        Short = @short;
        Icon = icon;
        Accent = accent;
    }

    public ActionCategory? Value { get; }

    public string Label { get; }

    public string Short { get; }

    public Lucide.Avalonia.LucideIconKind Icon { get; }

    public IBrush Accent { get; }

    /// <summary>True on the one category the wall below is showing right now.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
