using Avalonia.Media;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One entry of the action library's category rail: the category it narrows to (null for "all"),
/// and how the rail draws it.
/// </summary>
public sealed record ActionCategoryChoice(ActionCategory? Value, string Label, string Short,
    Lucide.Avalonia.LucideIconKind Icon, IBrush Accent);
