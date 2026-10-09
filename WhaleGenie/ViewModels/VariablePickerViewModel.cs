using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Backs the small picker that stands beside a field: the same wall the expression builder shows,
/// handing back the text of the variable that was picked. A field that can hold a variable should
/// never need its name typed out of memory.
/// </summary>
public partial class VariablePickerViewModel : ViewModelBase
{
    public VariablePickerViewModel(IReadOnlyList<VariableChoice>? variables = null)
    {
        Wall = new VariableWallViewModel(variables);
        Wall.Chosen += token => CloseRequested?.Invoke(token);
    }

    /// <summary>Raised with the text to put in the field, or null when nothing was picked.</summary>
    public event Action<string?>? CloseRequested;

    public string Header => Strings.Get("VarPicker.Title");

    public string Help => Strings.Get("VarPicker.Help");

    public VariableWallViewModel Wall { get; }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);
}
