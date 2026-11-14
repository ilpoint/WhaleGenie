using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One row of the local variable manager. Two kinds share the list: a value this macro declares,
/// whose name, kind, starting value and description are all editable here, and a name a step wrote,
/// whose kind and value belong to that step — only its name can be changed from this window.
/// </summary>
/// <remarks>
/// A row of its own rather than the global manager's draft: a local has no current value and no
/// persistence, and a global has no "written by step" kind. Sharing one shape would mean a row that
/// has to be told which of its own fields to hide.
/// </remarks>
public sealed partial class LocalVariableDraft : ObservableObject
{
    private bool _initializing;

    private LocalVariableDraft(
        string originalName, string name, ValueKind type, ExpressionNode initial, string description,
        bool isDeclared, string source, IReadOnlyList<VariableUse>? uses)
    {
        _initializing = true;
        OriginalName = originalName;
        OriginalType = type;
        OriginalInitial = initial;
        OriginalDescription = description;
        IsDeclared = isDeclared;
        Source = source;
        Uses = uses ?? [];
        Name = name;
        Type = type;
        Initial = initial;
        Description = description;
        _initializing = false;
    }

    /// <summary>A value the macro declares, or an empty row for one it is about to declare.</summary>
    public static LocalVariableDraft Declared(MacroLocalVariable? source = null,
        IReadOnlyList<VariableUse>? uses = null) => new(
            originalName: source?.Name.Trim() ?? string.Empty,
            name: source?.Name.Trim() ?? string.Empty,
            type: source?.Type ?? ValueKind.Text,
            initial: source?.Initial ?? Formula.Nothing,
            description: source?.Description ?? string.Empty,
            isDeclared: true,
            source: Strings.Get("LocalVariables.Declared"),
            uses);

    /// <summary>A name a step wrote. What it holds was decided by that step, so only the name is ours.</summary>
    public static LocalVariableDraft FromStep(VariableChoice choice) => new(
        originalName: choice.Name,
        name: choice.Name,
        type: choice.Type ?? ValueKind.Text,
        initial: Formula.Nothing,
        description: choice.Description,
        isDeclared: false,
        source: choice.Source,
        choice.Uses);

    public string OriginalName { get; }
    public ValueKind OriginalType { get; }
    public ExpressionNode OriginalInitial { get; }
    public string OriginalDescription { get; }

    /// <summary>True for a value this macro declares; false for one a step wrote.</summary>
    public bool IsDeclared { get; }

    /// <summary>Where it comes from: the declaration itself, or the step that writes it.</summary>
    public string Source { get; }

    public IReadOnlyList<VariableUse> Uses { get; }

    /// <summary>A new row that has not been named yet.</summary>
    public bool IsNew => OriginalName.Length == 0;

    public bool HasUses => Uses.Count > 0;

    /// <summary>What reads the declared value, when it is checked against the initial value's kind.</summary>
    public IVariableResolver? Resolver { get; set; } = SharedVariables.Current;

    public bool IsChanged => IsNew
        || !string.Equals(Name.Trim(), OriginalName.Trim(), StringComparison.Ordinal)
        || IsDeclared
        && (Type != OriginalType
            || !Equals(Initial, OriginalInitial)
            || !string.Equals(Description.Trim(), OriginalDescription.Trim(), StringComparison.Ordinal));

    public string NameProblem { get; private set; } = string.Empty;
    public string InitialProblem { get; private set; } = string.Empty;
    public bool HasNameProblem => NameProblem.Length > 0;
    public bool HasInitialProblem => InitialProblem.Length > 0;
    public bool HasProblem => HasNameProblem || HasInitialProblem;

    public IReadOnlyList<ActionParameterOption> TypeOptions { get; private set; } = TypeChoices();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedType))]
    [NotifyPropertyChangedFor(nameof(LocalType))]
    [NotifyPropertyChangedFor(nameof(InitialPreview))]
    [NotifyPropertyChangedFor(nameof(HasInitialError))]
    public partial ValueKind Type { get; set; } = ValueKind.Text;

    /// <summary>The starting value as the shape the declaration will keep. The only value here.</summary>
    public ExpressionNode Initial
    {
        get => _initial;
        set
        {
            if (Equals(_initial, value))
            {
                return;
            }

            _initial = value;
            OnPropertyChanged(nameof(Initial));
            OnPropertyChanged(nameof(InitialValue));
            OnPropertyChanged(nameof(InitialPreview));
            OnPropertyChanged(nameof(HasInitialError));
            OnPropertyChanged(nameof(IsChanged));
            if (!_initializing)
            {
                RefreshValues();
            }
        }
    }

    private ExpressionNode _initial = Formula.Nothing;

    /// <summary>
    /// What the box shows. Only out: a value is written through the value window, never typed here.
    /// </summary>
    public string InitialValue => ExpressionPhrase.Display(_initial);

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    /// <summary>The name to read on screen, which a row nobody has named yet still has to have.</summary>
    public string DisplayName => Name.Trim().Length == 0
        ? Strings.Get("Variable.New")
        : Name.Trim();

    public string LocalType => Strings.Get($"Variable.Type.{VariableTypes.Name(Type)}",
        VariableTypes.Name(Type));

    public ActionParameterOption SelectedType
    {
        get => TypeOptions.FirstOrDefault(option => option.Value == VariableTypes.Name(Type))
            ?? TypeOptions[0];
        set
        {
            var kind = VariableTypes.KindOf(value.Value);
            if (Type != kind)
            {
                Type = kind;
            }
        }
    }

    /// <summary>What the initial value comes to, read the way a run reads it.</summary>
    public string InitialPreview =>
        VariableValues.Preview(Type, Initial, Resolver);

    public bool HasInitialError =>
        VariableValues.HasError(Type, Initial, Resolver);

    public MacroLocalVariable ToLocal() => new()
    {
        Name = Name.Trim(),
        Type = Type,
        Initial = Initial,
        Description = Description.Trim(),
    };

    internal void SetProblems(string name, string initial)
    {
        var changed = !string.Equals(NameProblem, name, StringComparison.Ordinal)
            || !string.Equals(InitialProblem, initial, StringComparison.Ordinal);
        NameProblem = name;
        InitialProblem = initial;
        if (!changed)
        {
            return;
        }

        OnPropertyChanged(nameof(NameProblem));
        OnPropertyChanged(nameof(InitialProblem));
        OnPropertyChanged(nameof(HasNameProblem));
        OnPropertyChanged(nameof(HasInitialProblem));
        OnPropertyChanged(nameof(HasProblem));
    }

    internal void RefreshValues()
    {
        OnPropertyChanged(nameof(InitialPreview));
        OnPropertyChanged(nameof(HasInitialError));
    }

    public void RefreshLanguage()
    {
        TypeOptions = TypeChoices();
        OnPropertyChanged(nameof(TypeOptions));
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(LocalType));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Source));
    }

    partial void OnTypeChanged(ValueKind value)
    {
        if (!_initializing)
        {
            RefreshValues();
        }
    }

    private static IReadOnlyList<ActionParameterOption> TypeChoices() =>
        [.. VariableTypes.Declarable.Select(kind => new ActionParameterOption(
            VariableTypes.Name(kind),
            Strings.Get($"Variable.Type.{VariableTypes.Name(kind)}", VariableTypes.Name(kind))))];
}
