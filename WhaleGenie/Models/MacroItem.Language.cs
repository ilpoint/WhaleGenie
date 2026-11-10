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

// Re-reading the labels this macro shows when the language changes.
public partial class MacroItem : ObservableObject, ILanguageAware
{
    /// <summary>Re-reads the words this macro is shown with, without rebuilding the card.</summary>
    public void RefreshLanguage()
    {
        RefreshTriggerDisplay();
        OnPropertyChanged(string.Empty);
    }
}
