using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The project-level global variable manager. Every edit is staged in drafts, so Cancel leaves the
/// catalogue, current values and the persistence file untouched.
/// </summary>
public sealed partial class GlobalVariablesViewModel : ViewModelBase
{
    private readonly Func<string, IReadOnlyList<VariableUse>> _usesOf;
    private readonly Func<string, string?> _renameConflict;
    private readonly Func<string, IReadOnlyList<string>> _scriptUsers;
    private readonly IReadOnlyList<string> _initialNames;
    private List<GlobalVariableProblem> _problems = [];
    private bool _refreshing;
    private bool _suppressRebuild;

    public GlobalVariablesViewModel(IEnumerable<VariableDefinition> globals,
        Func<string, IReadOnlyList<VariableUse>>? usesOf = null,
        Func<string, string?>? renameConflict = null,
        Func<string, IReadOnlyList<string>>? scriptUsers = null)
    {
        _usesOf = usesOf ?? (_ => []);
        _renameConflict = renameConflict ?? (_ => null);
        _scriptUsers = scriptUsers ?? (_ => []);
        var definitions = globals.ToList();
        _initialNames = [.. definitions.Select(variable => variable.Name)];
        foreach (var variable in definitions)
        {
            AddDraft(new VariableDefinitionDraft(variable, _usesOf(variable.Name)));
        }

        Filters =
        [
            new GlobalVariableFilter(GlobalVariableFilterKind.All, "VarWall.All"),
            new GlobalVariableFilter(GlobalVariableFilterKind.Persistent, "VarWall.Persistent"),
            new GlobalVariableFilter(GlobalVariableFilterKind.Temporary, "GlobalVariables.Temporary"),
        ];
        SelectedFilter = Filters[0];
        RebuildShown();
    }

    public event Action<IReadOnlyList<VariableDefinitionDraft>>? Saved;
    public event Action? Cancelled;
    public event Action? SaveBlocked;
    public event Action<GlobalVariableProblemField>? FocusRequested;
    public event Action<VariableDefinitionDraft>? DefaultExpressionRequested;
    public event Action<VariableDefinitionDraft>? CurrentExpressionRequested;

    public string Header => Strings.Get("GlobalVariables.Title");
    public string Help => Strings.Get("GlobalVariables.Help");
    public string NewLabel => Strings.Get("GlobalVariables.New");
    public string RemoveLabel => Strings.Get("GlobalVariables.Remove");
    public string SaveLabel => Strings.Get("Common.Save");
    public string CancelLabel => Strings.Get("Common.Cancel");

    public ObservableCollection<VariableDefinitionDraft> Rows { get; } = [];
    public ObservableCollection<VariableDefinitionDraft> Shown { get; } = [];
    public IReadOnlyList<GlobalVariableFilter> Filters { get; private set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial GlobalVariableFilter? SelectedFilter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Editor))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial VariableDefinitionDraft? Selected { get; set; }

    public VariableDefinitionDraft? Editor => Selected;
    public bool HasSelection => Selected is not null;
    internal IReadOnlyList<GlobalVariableProblem> Problems => _problems;
    public int ProblemCount => _problems.Count;
    public string ValidationMessage => _problems.Count switch
    {
        0 => string.Empty,
        1 => _problems[0].Message,
        _ => Strings.Format("GlobalVariables.ProblemSummary", _problems.Count, _problems[0].Message),
    };

    public bool HasValidationProblem => _problems.Count > 0;
    public bool CanSave => _problems.Count == 0;
    public bool NeedsName => Rows.Any(row => string.IsNullOrWhiteSpace(row.Name));
    public string NamePrompt => NeedsName
        ? Strings.Get("GlobalVariables.NameRequired")
        : string.Empty;
    public bool HasRows => Shown.Count > 0;
    public bool HasVariables => Rows.Count > 0;
    public bool ShowsNoVariables => Rows.Count == 0;
    public bool ShowsNoMatches => Rows.Count > 0 && Shown.Count == 0;
    public string EmptyMessage => ShowsNoVariables
        ? Strings.Get("GlobalVariables.NoVariables")
        : Strings.Get("GlobalVariables.NoMatches");
    public bool CanClearFilters => SearchText.Trim().Length > 0
        || SelectedFilter?.Kind is not null and not GlobalVariableFilterKind.All;
    public bool HasChanges => Rows.Any(row => row.IsChanged)
        || !Rows.Where(row => !row.IsNew)
            .Select(row => row.OriginalName)
            .SequenceEqual(_initialNames, StringComparer.Ordinal);
    public string RenameWarning => Notes(Selected);
    public bool HasRenameWarning => RenameWarning.Length > 0;

    public void SelectNamed(string name)
    {
        _suppressRebuild = true;
        SearchText = string.Empty;
        SelectedFilter = Filters[0];
        _suppressRebuild = false;
        RebuildShown();
        Selected = Shown.FirstOrDefault(row =>
            string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? Shown.FirstOrDefault();
        RefreshState();
    }

    /// <summary>
    /// Brings the first problem into view. The window listens to
    /// <see cref="FocusRequested"/> to put the caret in the matching field.
    /// </summary>
    public bool FocusFirstProblem()
    {
        if (_problems.Count == 0)
        {
            return false;
        }

        var problem = _problems[0];
        _suppressRebuild = true;
        SearchText = string.Empty;
        SelectedFilter = Filters[0];
        _suppressRebuild = false;
        RebuildShown();
        Selected = problem.Row;
        FocusRequested?.Invoke(problem.Field);
        return true;
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_suppressRebuild)
        {
            RebuildShown();
        }
    }

    partial void OnSelectedFilterChanged(GlobalVariableFilter? value)
    {
        if (!_suppressRebuild)
        {
            RebuildShown();
        }
    }

    partial void OnSelectedChanged(VariableDefinitionDraft? value)
    {
        OnPropertyChanged(nameof(RenameWarning));
        OnPropertyChanged(nameof(HasRenameWarning));
        RemoveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void New()
    {
        var draft = Rows.FirstOrDefault(row => row.IsNew && string.IsNullOrWhiteSpace(row.Name));
        if (draft is null)
        {
            draft = new VariableDefinitionDraft
            {
                Name = string.Empty,
                Type = "text",
            };
            AddDraft(draft);
        }

        _suppressRebuild = true;
        SearchText = string.Empty;
        SelectedFilter = Filters[0];
        _suppressRebuild = false;
        RebuildShown();
        Selected = draft;
        RefreshState();
        FocusRequested?.Invoke(GlobalVariableProblemField.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var index = Shown.IndexOf(row);
        row.PropertyChanged -= OnDraftChanged;
        Rows.Remove(row);
        RebuildShown();
        if (Shown.Count > 0)
        {
            Selected = Shown[Math.Clamp(index, 0, Shown.Count - 1)];
        }

        RefreshState();
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanSave)
        {
            SaveBlocked?.Invoke();
            FocusFirstProblem();
            return;
        }

        Saved?.Invoke([.. Rows]);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressRebuild = true;
        SearchText = string.Empty;
        SelectedFilter = Filters[0];
        _suppressRebuild = false;
        RebuildShown();
    }

    [RelayCommand]
    private void BuildDefaultExpression()
    {
        if (Selected is { } row)
        {
            DefaultExpressionRequested?.Invoke(row);
        }
    }

    [RelayCommand]
    private void BuildCurrentExpression()
    {
        if (Selected is { } row)
        {
            CurrentExpressionRequested?.Invoke(row);
        }
    }

    public override void RefreshLanguage()
    {
        var kind = SelectedFilter?.Kind ?? GlobalVariableFilterKind.All;
        _suppressRebuild = true;
        Filters =
        [
            new GlobalVariableFilter(GlobalVariableFilterKind.All, "VarWall.All"),
            new GlobalVariableFilter(GlobalVariableFilterKind.Persistent, "VarWall.Persistent"),
            new GlobalVariableFilter(GlobalVariableFilterKind.Temporary, "GlobalVariables.Temporary"),
        ];
        OnPropertyChanged(nameof(Filters));
        SelectedFilter = Filters.First(filter => filter.Kind == kind);
        _suppressRebuild = false;
        foreach (var row in Rows)
        {
            row.RefreshLanguage();
        }

        RebuildShown();
        base.RefreshLanguage();
    }

    private void AddDraft(VariableDefinitionDraft draft)
    {
        draft.Resolver = VariableValues.DesignContext();
        draft.PropertyChanged += OnDraftChanged;
        Rows.Add(draft);
    }

    private void OnDraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_refreshing)
        {
            return;
        }

        if (e.PropertyName is nameof(VariableDefinitionDraft.Name)
            or nameof(VariableDefinitionDraft.Type)
            or nameof(VariableDefinitionDraft.DefaultValue)
            or nameof(VariableDefinitionDraft.CurrentValue))
        {
            RefreshValidation();
        }

        RefreshState();
    }

    private void RebuildShown()
    {
        var keep = Selected;
        RefreshValidation();
        var search = SearchText.Trim();
        var kind = SelectedFilter?.Kind ?? GlobalVariableFilterKind.All;
        var wanted = Rows.Where(row =>
                (kind == GlobalVariableFilterKind.All
                 || kind == GlobalVariableFilterKind.Persistent && row.PersistValue
                 || kind == GlobalVariableFilterKind.Temporary && !row.PersistValue)
                && (search.Length == 0
                    || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || row.Type.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || row.LocalType.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || row.Description.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Shown.Clear();
        foreach (var row in wanted)
        {
            Shown.Add(row);
        }

        Selected = keep is not null && wanted.Contains(keep)
            ? keep
            : wanted.FirstOrDefault();
        RefreshState();
    }

    private void RefreshValidation()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var context = VariableValues.DesignContext();
            foreach (var row in Rows)
            {
                row.Resolver = context;
                var source = row.CurrentValueDirty ? row.CurrentValue : row.DefaultValue;
                if (VariableValues.TryRead(row.Type, source, context, out var value, out _)
                    && VariableText.IsName(row.Name.Trim()))
                {
                    context.Global.Set(row.Name.Trim(), value);
                }

                row.RefreshValues();
            }

            var duplicates = Rows
                .Where(row => row.Name.Trim().Length > 0)
                .GroupBy(row => row.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var problems = new List<GlobalVariableProblem>();
            foreach (var row in Rows)
            {
                var name = row.Name.Trim();
                var display = name.Length > 0 ? name : row.DisplayName;
                var nameProblem = name.Length == 0
                    ? Strings.Get("GlobalVariables.NameRequired")
                    : !VariableText.IsName(name)
                        ? Strings.Format("GlobalVariables.BadName", name)
                        : Reserved(name)
                            ? Strings.Format("GlobalVariables.ReservedName", name)
                            : duplicates.Contains(name)
                                ? Strings.Format("GlobalVariables.Repeated", name)
                                : string.Empty;
                var renameProblem = string.Empty;
                if (nameProblem.Length == 0 && !row.IsNew
                    && !string.Equals(row.OriginalName, name, StringComparison.Ordinal)
                    && _renameConflict(row.OriginalName) is { } conflict)
                {
                    renameProblem = Strings.Format("GlobalVariables.RenameConflict",
                        row.OriginalName, conflict);
                }

                var defaultProblem = row.DefaultHasError
                    ? Strings.Format("GlobalVariables.BadDefault", display, row.DefaultPreview)
                    : string.Empty;
                var currentProblem = row.CurrentValueDirty && row.CurrentHasError
                    ? Strings.Format("GlobalVariables.BadCurrent", display, row.CurrentPreview)
                    : string.Empty;

                row.SetProblems(nameProblem, defaultProblem, currentProblem, renameProblem);
                if (nameProblem.Length > 0)
                {
                    problems.Add(new GlobalVariableProblem(row,
                        GlobalVariableProblemField.Name, nameProblem));
                }

                if (defaultProblem.Length > 0)
                {
                    problems.Add(new GlobalVariableProblem(row,
                        GlobalVariableProblemField.DefaultValue, defaultProblem));
                }

                if (currentProblem.Length > 0)
                {
                    problems.Add(new GlobalVariableProblem(row,
                        GlobalVariableProblemField.CurrentValue, currentProblem));
                }

                if (renameProblem.Length > 0)
                {
                    problems.Add(new GlobalVariableProblem(row,
                        GlobalVariableProblemField.RenameConflict, renameProblem));
                }
            }

            _problems = problems;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationProblem));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasVariables));
        OnPropertyChanged(nameof(ShowsNoVariables));
        OnPropertyChanged(nameof(ShowsNoMatches));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(CanClearFilters));
        OnPropertyChanged(nameof(NeedsName));
        OnPropertyChanged(nameof(NamePrompt));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(RenameWarning));
        OnPropertyChanged(nameof(HasRenameWarning));
        RemoveCommand.NotifyCanExecuteChanged();
    }

    private string Notes(VariableDefinitionDraft? row)
    {
        if (row is null || row.IsNew
            || string.Equals(row.OriginalName, row.Name.Trim(), StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var scripts = _scriptUsers(row.OriginalName);
        return scripts.Count == 0
            ? string.Empty
            : Strings.Format("GlobalVariables.RenameScripts", row.OriginalName,
                string.Join(", ", scripts));
    }

    internal static bool Reserved(string name) =>
        name.StartsWith("sys.", StringComparison.OrdinalIgnoreCase)
        || MacroRunner.IsRunStepName(name);
}

public enum GlobalVariableProblemField
{
    Name,
    DefaultValue,
    CurrentValue,
    RenameConflict,
}

internal sealed record GlobalVariableProblem(VariableDefinitionDraft Row,
    GlobalVariableProblemField Field, string Message);

public enum GlobalVariableFilterKind
{
    All,
    Persistent,
    Temporary,
}

public sealed class GlobalVariableFilter(GlobalVariableFilterKind kind, string labelKey)
{
    public GlobalVariableFilterKind Kind { get; } = kind;
    public string Label { get; } = Strings.Get(labelKey);
}
