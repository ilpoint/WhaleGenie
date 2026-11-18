using System.Collections.Generic;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>The variable wall shown beside the macro settings for the active document.</summary>
public sealed class VariableCenterViewModel : ViewModelBase
{
    public string Header => Strings.Get("Editor.VariableCenter");

    public string Help => Strings.Get("Editor.VariableCenterHint");

    public VariableWallViewModel Wall { get; } = new() { ShowsDetails = false };

    public VariableCenterViewModel()
    {
        Wall.UseAvailableHeight();
        Wall.EditRequested += choice => EditRequested?.Invoke(choice);
    }

    public event System.Action<VariableChoice>? EditRequested;

    public void Refresh(IReadOnlyList<VariableChoice> variables) => Wall.SetVariables(variables);

    public override void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(Help));
        Wall.RefreshLanguage();
    }
}
