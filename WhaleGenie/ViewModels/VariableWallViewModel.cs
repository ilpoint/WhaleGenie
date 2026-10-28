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
    private IReadOnlyList<VariableChoice> _all;
    private bool _receivedVariables;

    public VariableWallViewModel(IReadOnlyList<VariableChoice>? variables = null)
    {
        _all = variables ?? [];
        // The two kinds of variable a macro's author owns are named the way the run window's
        // variable page names them: one word for one idea, so a field's picker and the wall that
        // lists them cannot drift into calling the same thing two things.
        Filters =
        [
            new VariableFilter(VariableGroup.All, "VarWall.All", "VarWall.AllHint"),
            new VariableFilter(VariableGroup.Local, "Variable.Tab.Local", "VarWall.LocalHint"),
            new VariableFilter(VariableGroup.Global, "Variable.Tab.Global", "VarWall.GlobalHint"),
            new VariableFilter(VariableGroup.System, "VarWall.System", "VarWall.SystemHint"),
            new VariableFilter(VariableGroup.StepResult, "VarWall.StepResult", "VarWall.StepResultHint"),
        ];

        // The macro's own variables are what most fields are filled in with, so that is where the
        // wall starts; tapping another button is one click away, and the hint on this one says so.
        // A value that only has global and system names — a macro interface default, for instance —
        // would otherwise open on an empty page, with the values it does have hidden behind a
        // filter the user has no reason to suspect.
        var start = Filters[1];
        if (_all.Count > 0 && !_all.Any(choice => choice.Group == VariableGroup.Local))
        {
            start = Filters[0];
        }

        SelectedFilter = start;
        Rebuild();
    }

    /// <summary>Raised with the text of the variable that was picked.</summary>
    public event Action<string>? Chosen;

    /// <summary>Raised with a step somebody asked to be shown, so the place they came from can look.</summary>
    public event Action<MacroStep>? Jumped;

    /// <summary>Raised when somebody asks to inspect or edit one variable definition.</summary>
    public event Action<VariableChoice>? EditRequested;

    public IReadOnlyList<VariableFilter> Filters { get; private set; }

    /// <summary>The variables on show, which is what the filter leaves of everything known.</summary>
    public ObservableCollection<VariableChoice> Shown { get; } = [];

    /// <summary>The steps that read the variable under the pointer, in the order they run.</summary>
    public ObservableCollection<VariableUse> Uses { get; } = [];

    /// <summary>The variable the pointer is over, whose details are shown underneath.</summary>
    [ObservableProperty]
    public partial VariableChoice? Looking { get; set; }

    /// <summary>Which button is pressed: one of them always is, so the wall is never without a meaning.</summary>
    [ObservableProperty]
    public partial VariableFilter? SelectedFilter { get; set; }

    /// <summary>Narrows the current group by name, type, description or source.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>The list height in a picker, or room to grow when the wall fills the inspector.</summary>
    [ObservableProperty]
    public partial double ListMaxHeight { get; set; } = 196;

    /// <summary>
    /// Whether the panel under the buttons is there at all. A picker keeps it: the step names in it
    /// are buttons that jump to the step. Beside the macro it is turned off — the same words are one
    /// hover away, and the band it takes comes straight out of the list.
    /// </summary>
    public bool ShowsDetails { get; init; } = true;

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

    /// <summary>
    /// True while the variable under the pointer is one a step reads. "Where is this used" is the
    /// question that follows "what is this": it is what makes a name safe to change, and what turns
    /// "unused, so it can go" into something a person can check rather than take on trust.
    /// </summary>
    public bool HasUses => Uses.Count > 0;

    partial void OnSelectedFilterChanged(VariableFilter? value) => Rebuild();

    partial void OnSearchTextChanged(string value) => Rebuild();

    /// <summary>Replaces the variables on the wall, keeping the chosen group when it still applies.</summary>
    public void SetVariables(IReadOnlyList<VariableChoice> variables)
    {
        var looking = Looking is { } current ? Key(current) : string.Empty;
        _all = variables;
        var firstLoad = !_receivedVariables;
        _receivedVariables = true;
        if (firstLoad
            && _all.Count > 0
            && Filter is VariableGroup.Local or VariableGroup.StepResult
            && !_all.Any(choice => choice.Group == Filter))
        {
            SelectedFilter = Filters[0];
            return;
        }

        Rebuild();
        Looking = looking.Length == 0
            ? null
            : Shown.FirstOrDefault(choice => string.Equals(Key(choice), looking,
                StringComparison.OrdinalIgnoreCase));
    }

    public void UseAvailableHeight() => ListMaxHeight = 1000;

    public override void RefreshLanguage()
    {
        var group = SelectedFilter?.Group ?? VariableGroup.All;
        Filters =
        [
            new VariableFilter(VariableGroup.All, "VarWall.All", "VarWall.AllHint"),
            new VariableFilter(VariableGroup.Local, "Variable.Tab.Local", "VarWall.LocalHint"),
            new VariableFilter(VariableGroup.Global, "Variable.Tab.Global", "VarWall.GlobalHint"),
            new VariableFilter(VariableGroup.System, "VarWall.System", "VarWall.SystemHint"),
            new VariableFilter(VariableGroup.StepResult, "VarWall.StepResult", "VarWall.StepResultHint"),
        ];
        OnPropertyChanged(nameof(Filters));
        SelectedFilter = Filters.First(filter => filter.Group == group);
        foreach (var row in _all)
        {
            row.RefreshLanguage();
        }

        base.RefreshLanguage();
    }

    partial void OnLookingChanged(VariableChoice? value)
    {
        Uses.Clear();
        if (value is not null)
        {
            foreach (var use in value.Uses)
            {
                Uses.Add(use);
            }
        }

        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(HasUses));
    }

    /// <summary>Somebody pressed one of the steps that reads the variable, to be shown where it is.</summary>
    [RelayCommand]
    private void Jump(VariableUse? use)
    {
        if (use is not null)
        {
            Jumped?.Invoke(use.Step);
        }
    }

    /// <summary>The variable a button stands for was picked.</summary>
    [RelayCommand]
    private void Choose(VariableChoice? choice)
    {
        if (choice is not null)
        {
            Chosen?.Invoke(choice.Token);
        }
    }

    [RelayCommand]
    private void Edit(VariableChoice? choice)
    {
        if (choice is not null)
        {
            EditRequested?.Invoke(choice);
        }
    }

    private void Rebuild()
    {
        var wanted = _all.Where(choice =>
                (Filter == VariableGroup.All || choice.Group == Filter)
                && MatchesSearch(choice))
            .ToList();
        var wantedKeys = wanted.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var at = Shown.Count - 1; at >= 0; at--)
        {
            if (!wantedKeys.Contains(Key(Shown[at])))
            {
                Shown.RemoveAt(at);
            }
        }

        for (var at = 0; at < wanted.Count; at++)
        {
            var choice = wanted[at];
            var found = -1;
            for (var scan = at; scan < Shown.Count; scan++)
            {
                if (string.Equals(Key(Shown[scan]), Key(choice), StringComparison.OrdinalIgnoreCase))
                {
                    found = scan;
                    break;
                }
            }

            if (found < 0)
            {
                Shown.Insert(at, choice);
            }
            else
            {
                if (found != at)
                {
                    Shown.Move(found, at);
                }

                if (!ReferenceEquals(Shown[at], choice))
                {
                    Shown[at] = choice;
                }
            }
        }

        while (Shown.Count > wanted.Count)
        {
            Shown.RemoveAt(Shown.Count - 1);
        }

        if (Looking is { } looking && !wantedKeys.Contains(Key(looking)))
        {
            Looking = null;
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasUses));
        OnPropertyChanged(nameof(Details));
    }

    private bool MatchesSearch(VariableChoice choice)
    {
        var search = SearchText.Trim();
        return search.Length == 0
            || choice.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || choice.Display.Contains(search, StringComparison.OrdinalIgnoreCase)
            || choice.Type.Contains(search, StringComparison.OrdinalIgnoreCase)
            || choice.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
            || choice.Source.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string Key(VariableChoice choice) => $"{choice.Group}\u0000{choice.Name}";
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
