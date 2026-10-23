using System.Collections.Generic;
using System.Collections.ObjectModel;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Carries a block's child steps through the action form. The editor's main list is the only place
/// they are shown or reordered; this object exists so opening and saving the form keeps them.
/// </summary>
public sealed class StepListValue : ViewModelBase
{
    public ObservableCollection<MacroStep> Steps { get; } = [];

    /// <summary>Replaces the whole list, used when loading an existing step.</summary>
    public void Load(IEnumerable<MacroStep> steps)
    {
        Steps.Clear();
        foreach (var step in steps)
        {
            Steps.Add(step);
        }

        OnPropertyChanged(nameof(Steps));
    }
}
