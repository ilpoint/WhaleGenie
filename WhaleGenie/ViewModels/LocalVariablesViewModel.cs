using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The window that manages the variables a macro keeps for itself. Two kinds stand side by side: the
/// values the macro declares — created, edited and deleted here, with the value every run starts
/// from — and the names its steps write, which exist because a step wrote them and can only be
/// renamed. Every edit is staged in rows, so leaving without saving changes nothing.
/// </summary>
public sealed partial class LocalVariablesViewModel : ViewModelBase
{
    private readonly Func<string, string?> _taken;
    private readonly IReadOnlyList<string> _initialNames;
    private List<LocalVariableProblem> _problems = [];
    private bool _refreshing;

    public LocalVariablesViewModel(IReadOnlyList<MacroLocalVariable>? declared = null,
        IReadOnlyList<VariableChoice>? variables = null, Func<string, string?>? taken = null)
    {
        _taken = taken ?? (_ => null);
        var context = VariableValues.DesignContext();
        var known = (variables ?? []).ToLookup(choice => choice.Name, StringComparer.OrdinalIgnoreCase);

        // Declared values first, in the order the macro declares them — that order is what a run
        // reads them in, so the list has to show it rather than an alphabet nobody can act on.
        // Then the names a step wrote, read by name.
        foreach (var local in declared ?? [])
        {
            Add(LocalVariableDraft.Declared(local, UsesOf(known, local.Name)), context);
        }

        foreach (var choice in (variables ?? [])
                     .Where(choice => choice.Origin is VariableOrigin.Local)
                     .OrderBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase))
        {
            Add(LocalVariableDraft.FromStep(choice), context);
        }

        _initialNames = [.. Rows.Select(row => row.OriginalName)];
        RebuildShown();
    }

    /// <summary>Raised with the rows to commit; the window's caller answers by applying them.</summary>
    public event Action<IReadOnlyList<LocalVariableDraft>>? Saved;
    public event Action? Cancelled;
    public event Action<LocalVariableProblemField>? FocusRequested;

    public string Title => Strings.Get("LocalVariables.Title");
    public string Help => Strings.Get("LocalVariables.Hint");
    public string NewLabel => Strings.Get("LocalVariables.New");
    public string RemoveLabel => Strings.Get("LocalVariables.Delete");

    public ObservableCollection<LocalVariableDraft> Rows { get; } = [];
    public ObservableCollection<LocalVariableDraft> Shown { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedUses))]
    [NotifyPropertyChangedFor(nameof(HasUses))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowsDeclaration))]
    [NotifyPropertyChangedFor(nameof(ShowsStep))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial LocalVariableDraft? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    /// <summary>True while the row on show is a value this macro declares, so its fields are editable.</summary>
    public bool ShowsDeclaration => Selected is { IsDeclared: true };

    /// <summary>True while the row on show is a name a step wrote, which only its name can change.</summary>
    public bool ShowsStep => Selected is { IsDeclared: false };

    public IReadOnlyList<VariableUse> SelectedUses => Selected?.Uses ?? [];

    public bool HasUses => SelectedUses.Count > 0;

    public bool HasValidationProblem => _problems.Count > 0;

    public bool CanSave => _problems.Count == 0;

    public string ValidationMessage => _problems.Count switch
    {
        0 => string.Empty,
        1 => _problems[0].Message,
        _ => Strings.Format("LocalVariables.ProblemSummary", _problems.Count, _problems[0].Message),
    };

    public bool IsEmpty => Shown.Count == 0;

    public string EmptyMessage => Rows.Count == 0
        ? Strings.Get("LocalVariables.Empty")
        : Strings.Get("LocalVariables.NoMatches");

    /// <summary>True while leaving would throw away a name, a kind, a value or a deletion.</summary>
    public bool HasChanges => Rows.Any(row => row.IsChanged)
        || !Rows.Where(row => !row.IsNew)
            .Select(row => row.OriginalName)
            .SequenceEqual(_initialNames, StringComparer.OrdinalIgnoreCase);

    partial void OnSearchTextChanged(string value) => RebuildShown();

    partial void OnSelectedChanged(LocalVariableDraft? value)
    {
        OnPropertyChanged(nameof(SelectedUses));
        OnPropertyChanged(nameof(HasUses));
    }

    [RelayCommand]
    private void New()
    {
        // The empty row that is already there is reused rather than adding a second one: pressing
        // the button twice should not leave two nameless declarations to clean up.
        var row = Rows.FirstOrDefault(candidate => candidate.IsDeclared && candidate.IsNew
            && candidate.Name.Trim().Length == 0);
        if (row is null)
        {
            row = LocalVariableDraft.Declared();
            Add(row, VariableValues.DesignContext());
        }

        SearchText = string.Empty;
        RebuildShown();
        Selected = row;
        RefreshState();
        FocusRequested?.Invoke(LocalVariableProblemField.Name);
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var index = Shown.IndexOf(row);
        row.PropertyChanged -= OnRowChanged;
        Rows.Remove(row);
        RebuildShown();
        if (Shown.Count > 0)
        {
            Selected = Shown[Math.Clamp(index, 0, Shown.Count - 1)];
        }

        RefreshState();
    }

    /// <summary>
    /// Only a declared value can be taken away: a name a step writes is that step's, and the way to
    /// stop it is to change the step.
    /// </summary>
    private bool CanRemove() => Selected is { IsDeclared: true };

    [RelayCommand]
    private void Save()
    {
        if (!CanSave)
        {
            FocusFirstProblem();
            return;
        }

        Saved?.Invoke([.. Rows]);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    /// <summary>Brings the first problem into view and asks the window to put the caret in its field.</summary>
    public bool FocusFirstProblem()
    {
        if (_problems.Count == 0)
        {
            return false;
        }

        SearchText = string.Empty;
        RebuildShown();
        Selected = _problems[0].Row;
        FocusRequested?.Invoke(_problems[0].Field);
        return true;
    }

    /// <summary>Shows what the macro said when it refused to apply the rows.</summary>
    public void SayProblem(string problem)
    {
        _problems = Selected is { } row
            ? [new LocalVariableProblem(row, LocalVariableProblemField.Name, problem)]
            : [];
        RefreshState();
    }

    public override void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Help));
        OnPropertyChanged(nameof(NewLabel));
        OnPropertyChanged(nameof(RemoveLabel));
        OnPropertyChanged(nameof(EmptyMessage));
        foreach (var row in Rows)
        {
            row.RefreshLanguage();
        }

        base.RefreshLanguage();
    }

    private void Add(LocalVariableDraft row, IVariableResolver context)
    {
        row.Resolver = context;
        row.PropertyChanged += OnRowChanged;
        Rows.Add(row);
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_refreshing)
        {
            return;
        }

        if (e.PropertyName is nameof(LocalVariableDraft.Name)
            or nameof(LocalVariableDraft.Type)
            or nameof(LocalVariableDraft.Initial))
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
        var wanted = Rows.Where(row =>
                search.Length == 0
                || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Source.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Shown.Clear();
        foreach (var row in wanted)
        {
            Shown.Add(row);
        }

        Selected = keep is not null && wanted.Contains(keep) ? keep : wanted.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
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
                row.RefreshValues();
            }

            var duplicates = Rows
                .Where(row => row.Name.Trim().Length > 0)
                .GroupBy(row => row.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var problems = new List<LocalVariableProblem>();
            foreach (var row in Rows)
            {
                var name = row.Name.Trim();
                var display = name.Length > 0 ? name : row.DisplayName;
                var nameProblem = name.Length == 0
                    ? Strings.Get("LocalVariables.NameRequired")
                    : !VariableText.IsName(name)
                        ? Strings.Format("Editor.Problem.InvalidVariableName", name)
                        : Reserved(name)
                            ? Strings.Format("LocalVariables.ReservedName", name)
                            : duplicates.Contains(name)
                                ? Strings.Format("Editor.Problem.VariableNameTaken", name)
                                : _taken(name) ?? string.Empty;
                var initialProblem = row.IsDeclared && row.HasInitialError
                    ? Strings.Format("LocalVariables.BadInitial", display, row.InitialPreview)
                    : string.Empty;

                row.SetProblems(nameProblem, initialProblem);
                if (nameProblem.Length > 0)
                {
                    problems.Add(new LocalVariableProblem(row, LocalVariableProblemField.Name,
                        nameProblem));
                }

                if (initialProblem.Length > 0)
                {
                    problems.Add(new LocalVariableProblem(row, LocalVariableProblemField.Initial,
                        initialProblem));
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
        OnPropertyChanged(nameof(HasValidationProblem));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasSelection));
        RemoveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>What reads a declared value, taken from the rows built for the wall.</summary>
    private static IReadOnlyList<VariableUse> UsesOf(
        ILookup<string, VariableChoice> known, string name)
        => name.Trim() is { Length: > 0 } written
            ? known[written].FirstOrDefault()?.Uses ?? []
            : [];

    internal static bool Reserved(string name) =>
        name.StartsWith("sys.", StringComparison.OrdinalIgnoreCase)
        || MacroRunner.IsRunStepName(name);
}

/// <summary>Which field of a row a problem belongs to, so the window can put the caret there.</summary>
public enum LocalVariableProblemField
{
    Name,
    Initial,
}

internal sealed record LocalVariableProblem(
    LocalVariableDraft Row, LocalVariableProblemField Field, string Message);
