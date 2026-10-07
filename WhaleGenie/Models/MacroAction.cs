using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WhaleGenie.Models;

/// <summary>A single step inside a macro, or an entry in the action palette.</summary>
public partial class MacroAction : ObservableObject
{
    /// <summary>Vector icon drawn for the action, or <c>null</c> when it has none.</summary>
    public Geometry? Icon { get; set; }

    /// <summary>Key of the tooltip text, resolved on read so the language can change.</summary>
    public string LabelKey { get; set; } = string.Empty;

    public string Label => Localization.Strings.Get(LabelKey);

    /// <summary>Stable id for palette entries that map to an editor command.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>False while this entry has no editor behind it yet.</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>True for the entry that stands out from the rest, such as "run and debug".</summary>
    public bool IsPrimary { get; set; }

    /// <summary>True when the entry only applies with at least one selected step.</summary>
    public bool NeedsSelection { get; set; }

    /// <summary>True when the entry only applies with exactly one selected step.</summary>
    public bool NeedsSingleSelection { get; set; }

    /// <summary>True when the entry only applies while the macro has steps.</summary>
    public bool NeedsSteps { get; set; }

    /// <summary>True when the entry only applies once something can be undone.</summary>
    public bool NeedsUndo { get; set; }

    /// <summary>True when the entry only applies once something can be redone.</summary>
    public bool NeedsRedo { get; set; }

    /// <summary>Whether the palette button can be pressed right now.</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;
}
