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
/// The window that manages the variables a macro makes for itself. A local variable is not declared
/// anywhere: it appears because a step handed a result over under a name, or because a "set variable"
/// step wrote one. That is why this window cannot create one — a name nobody writes to is not a
/// variable — and why it can rename one: the name is what the rest of the macro reads it by.
/// </summary>
public partial class LocalVariablesViewModel : ViewModelBase
{
    private IReadOnlyList<VariableChoice> _all = [];

    public LocalVariablesViewModel(IReadOnlyList<VariableChoice>? variables = null)
    {
        SetVariables(variables ?? []);
    }

    /// <summary>Asks the editor to rename one variable, which also rewrites what reads it.</summary>
    public event Action<string, string>? RenameRequested;

    public string Title => Strings.Get("LocalVariables.Title");

    public string Help => Strings.Get("LocalVariables.Hint");

    /// <summary>The macro's own variables, narrowed by the search box.</summary>
    public ObservableCollection<VariableChoice> Shown { get; } = [];

    [ObservableProperty]
    public partial VariableChoice? Selected { get; set; }

    /// <summary>A word or two from the name, the source or the description.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>The name being typed, which starts as the one this variable has now.</summary>
    [ObservableProperty]
    public partial string DraftName { get; set; } = string.Empty;

    /// <summary>What went wrong with the last rename, or nothing.</summary>
    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    public bool HasProblem => Problem.Length > 0;

    public bool HasSelection => Selected is not null;

    public bool IsEmpty => Shown.Count == 0;

    public string SelectedType => Selected?.Type.Length > 0
        ? Strings.Get($"Variable.Type.{Selected.Type}", Selected.Type)
        : string.Empty;

    public string SelectedSource => Selected?.Source ?? string.Empty;

    public string SelectedDescription => Selected?.Description ?? string.Empty;

    public bool HasDescription => SelectedDescription.Length > 0;

    public IReadOnlyList<VariableUse> SelectedUses => Selected?.Uses ?? [];

    public bool HasUses => SelectedUses.Count > 0;

    /// <summary>True while the name typed is worth sending: it is different and not empty.</summary>
    public bool CanRename => Selected is not null
        && DraftName.Trim().Length > 0
        && !string.Equals(DraftName.Trim(), Selected.Name, StringComparison.Ordinal);

    /// <summary>
    /// Takes the macro's current variables and keeps one selected: the one named, or the one that
    /// was being looked at before. Renaming goes through here too, so the list shows the new name.
    /// </summary>
    public void SetVariables(IReadOnlyList<VariableChoice> variables, string? keep = null)
    {
        _all = [.. variables.Where(choice => choice.Origin is VariableOrigin.Local)];
        Rebuild(keep ?? Selected?.Name);
    }

    partial void OnSearchTextChanged(string value) => Rebuild(Selected?.Name);

    partial void OnSelectedChanged(VariableChoice? value)
    {
        DraftName = value?.Name ?? string.Empty;
        Problem = string.Empty;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanRename));
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(SelectedDescription));
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(SelectedUses));
        OnPropertyChanged(nameof(HasUses));
    }

    partial void OnDraftNameChanged(string value)
    {
        Problem = string.Empty;
        OnPropertyChanged(nameof(CanRename));
    }

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    /// <summary>Asks for the rename; the editor answers by calling back with what went wrong.</summary>
    [RelayCommand]
    private void Rename()
    {
        if (!CanRename)
        {
            return;
        }

        RenameRequested?.Invoke(Selected!.Name, DraftName.Trim());
    }

    /// <summary>Says what the editor refused, on the line under the box.</summary>
    public void SayProblem(string problem)
    {
        Problem = problem;
    }

    private void Rebuild(string? keep)
    {
        var search = SearchText.Trim();
        var rows = search.Length == 0
            ? _all
            : [.. _all.Where(choice =>
                choice.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || choice.Source.Contains(search, StringComparison.OrdinalIgnoreCase)
                || choice.Description.Contains(search, StringComparison.OrdinalIgnoreCase))];

        Shown.Clear();
        foreach (var row in rows)
        {
            Shown.Add(row);
        }

        Selected = Shown.FirstOrDefault(choice =>
                string.Equals(choice.Name, keep, StringComparison.OrdinalIgnoreCase))
            ?? Shown.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }

    public override void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Help));
        OnPropertyChanged(nameof(SelectedType));
    }
}
