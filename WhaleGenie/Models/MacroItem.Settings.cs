using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// The switches around it: input rate, where it came from, and the trigger it is showing.
public partial class MacroItem : ObservableObject, ILanguageAware
{

    /// <summary>
    /// Whether a binding names a controller button. Every one of those names starts with the same
    /// word, which is what the card reads rather than a list of buttons: a later button joins the
    /// vocabulary by having a name like the others.
    /// </summary>
    private static bool LooksLikeGamepadButton(string binding)
        => (binding ?? string.Empty).Trim()
            .StartsWith("Pad ", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Keeps the card's icon and preview in step with the settings behind them.</summary>
    partial void OnTriggerModeChanged(MacroTrigger value) => RefreshTriggerDisplay();

    partial void OnBindKeyChanged(string value) => RefreshTriggerDisplay();

    partial void OnHexColorChanged(string value) => RefreshTriggerDisplay();

    private void RefreshTriggerDisplay()
    {
        OnPropertyChanged(nameof(IsColorTrigger));
        OnPropertyChanged(nameof(IsTimerTrigger));
        OnPropertyChanged(nameof(IsFileTrigger));
        OnPropertyChanged(nameof(IsProcessTrigger));
        OnPropertyChanged(nameof(IsWindowTrigger));
        OnPropertyChanged(nameof(IsIdleTrigger));
        OnPropertyChanged(nameof(IsMouseTrigger));
        OnPropertyChanged(nameof(IsGamepadTrigger));
        OnPropertyChanged(nameof(IsKeyboardTrigger));
        OnPropertyChanged(nameof(TriggerPreview));
    }
}
