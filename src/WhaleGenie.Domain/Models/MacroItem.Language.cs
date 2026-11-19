using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

// Re-reading the labels this macro shows when the language changes.
public partial class MacroItem : ObservableObject, ILanguageAware
{
    /// <summary>Re-reads the words this macro is shown with, without rebuilding the card.</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(CardTriggerSummary));
        OnPropertyChanged(nameof(CardTriggerActions));
        OnPropertyChanged(nameof(CardTriggerPreview));
        OnPropertyChanged(string.Empty);
    }
}
