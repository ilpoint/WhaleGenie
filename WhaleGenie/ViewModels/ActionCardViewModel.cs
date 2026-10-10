using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One action as the picker shows it: the catalogue entry, and whether it is the one the dialog is
/// about. The mark lives here rather than on the entry because the catalogue is shared — every
/// dialog reads the same list — while what is being edited belongs to one dialog.
/// </summary>
public partial class ActionCardViewModel : ViewModelBase
{
    public required ActionDefinition Definition { get; init; }

    /// <summary>True when this is the action the fields beside the list are fields of.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Key of the action, which is what choosing a card hands over.</summary>
    public string Key => Definition.Key;
}
