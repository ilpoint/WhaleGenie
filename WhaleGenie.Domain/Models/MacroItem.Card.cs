using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

// The card's own words: what the macro is called, where it sits, and what starts it.
public partial class MacroItem : ObservableObject, ILanguageAware
{
    /// <summary>Whether the macro is armed; the macro list shows this as a coloured dot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    public partial bool IsEnabled { get; set; } = true;

    /// <summary>
    /// What the card's menu calls the switch for this macro: arming one that is off, or disarming
    /// one that is on. It is read when the menu opens, so the word always matches the dot.
    /// </summary>
    public string ToggleLabel => Strings.Get(IsEnabled ? "Main.DisableMacro" : "Main.EnableMacro");

    /// <summary>
    /// Whether a trigger is running this macro right now. It is shown on the macro list and is
    /// never saved: a macro that was running when the project was written is not running when it
    /// is read back.
    /// </summary>
    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The folder this macro was put in, as the author spelled it. Empty means ungrouped. It is a
    /// second layer beside the fixed views: the views say how a macro starts, a group says which
    /// part of someone's work it belongs to, and the two filter together.
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// A sentence for the macro library or for a call made to this macro. It does not describe a
    /// particular trigger, so it belongs to the callable part shared by projects and submacros.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// What starts this macro, one line per entry. It is a projection the trigger set writes when
    /// it changes — never saved and never edited — so the card does not have to hold a copy of the
    /// triggers to say what they are.
    /// </summary>
    public string CardTriggerSummary { get; private set; } = Strings.Get("Trigger.Unset");

    /// <summary>The ways the key and colour entries repeat, for the card's second line.</summary>
    public string CardTriggerActions { get; private set; } = string.Empty;

    /// <summary>What the macro picker shows beside the name.</summary>
    public string CardTriggerPreview { get; private set; } = Strings.Get("Common.None");

    internal void ShowTriggers(string summary, string actions, string preview)
    {
        CardTriggerSummary = summary;
        CardTriggerActions = actions;
        CardTriggerPreview = preview;
        RefreshTriggerDisplay();
    }

    /// <summary>Re-reads the words the card shows, keeping the values the trigger set wrote.</summary>
    private void RefreshTriggerDisplay()
    {
        OnPropertyChanged(nameof(CardTriggerSummary));
        OnPropertyChanged(nameof(CardTriggerActions));
        OnPropertyChanged(nameof(CardTriggerPreview));
    }
}
