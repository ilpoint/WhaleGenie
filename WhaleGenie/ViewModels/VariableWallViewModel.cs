using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The wall of variable buttons a picker shows: every variable the macro can read, laid out as
/// something to point at rather than as a name to remember, with the ones that are not wanted at
/// the moment filtered out. What a person needs to choose one is on the button's tooltip and on the
/// line underneath, so a wall of names is not a wall of guesses.
/// </summary>
public partial class VariableWallViewModel : ViewModelBase
{
    private readonly IReadOnlyList<VariableChoice> _all;

    public VariableWallViewModel(IReadOnlyList<VariableChoice>? variables = null)
    {
        _all = variables ?? [];
        Filters =
        [
            new VariableFilter(VariableGroup.All, "VarWall.All", "VarWall.AllHint"),
            new VariableFilter(VariableGroup.Local, "VarWall.Local", "VarWall.LocalHint"),
            new VariableFilter(VariableGroup.Global, "VarWall.Global", "VarWall.GlobalHint"),
            new VariableFilter(VariableGroup.System, "VarWall.System", "VarWall.SystemHint"),
            new VariableFilter(VariableGroup.StepResult, "VarWall.StepResult", "VarWall.StepResultHint"),
        ];

        // The macro's own variables are what most fields are filled in with, so that is where the
        // wall starts; tapping another button is one click away, and the hint on this one says so.
        SelectedFilter = Filters[1];
        Rebuild();
    }

    /// <summary>Raised with the text of the variable that was picked.</summary>
    public event Action<string>? Chosen;

    public IReadOnlyList<VariableFilter> Filters { get; }

    /// <summary>The variables on show, which is what the filter leaves of everything known.</summary>
    public ObservableCollection<VariableChoice> Shown { get; } = [];

    /// <summary>The variable the pointer is over, whose details are shown underneath.</summary>
    [ObservableProperty]
    public partial VariableChoice? Looking { get; set; }

    /// <summary>Which button is pressed: one of them always is, so the wall is never without a meaning.</summary>
    [ObservableProperty]
    public partial VariableFilter? SelectedFilter { get; set; }

    /// <summary>The group whose variables are on show.</summary>
    public VariableGroup Filter => SelectedFilter?.Group ?? VariableGroup.All;

    /// <summary>
    /// The line under the buttons: what the one under the pointer is, or — when there is nothing to
    /// point at — why there is nothing there, which is the moment a hint is worth its line.
    /// </summary>
    public string Details => Looking is { } choice
        ? choice.Details
        : Strings.Get(IsEmpty ? "VarWall.Empty" : "VarWall.Pick");

    /// <summary>True while there is nothing to show, so the line says why instead of standing empty.</summary>
    public bool IsEmpty => Shown.Count == 0;

    partial void OnSelectedFilterChanged(VariableFilter? value) => Rebuild();

    partial void OnLookingChanged(VariableChoice? value) => OnPropertyChanged(nameof(Details));

    /// <summary>The variable a button stands for was picked.</summary>
    [RelayCommand]
    private void Choose(VariableChoice? choice)
    {
        if (choice is not null)
        {
            Chosen?.Invoke(choice.Token);
        }
    }

    private void Rebuild()
    {
        Shown.Clear();
        foreach (var choice in _all.Where(choice =>
            Filter == VariableGroup.All || choice.Group == Filter))
        {
            Shown.Add(choice);
        }

        Looking = null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Details));
    }
}

/// <summary>One of the wall's filter buttons: the group it shows and how it is written.</summary>
public sealed class VariableFilter
{
    public VariableFilter(VariableGroup group, string labelKey, string hintKey)
    {
        Group = group;
        Label = Strings.Get(labelKey);
        Hint = Strings.Get(hintKey);
    }

    public VariableGroup Group { get; }

    public string Label { get; }

    public string Hint { get; }
}
