using Avalonia.Media;

namespace Viktor.Models;

/// <summary>A single step inside a macro, or an entry in the action palette.</summary>
public class MacroAction
{
    /// <summary>Vector icon drawn for the action, or <c>null</c> when it has none.</summary>
    public Geometry? Icon { get; set; }

    public string Label { get; set; } = string.Empty;
}
