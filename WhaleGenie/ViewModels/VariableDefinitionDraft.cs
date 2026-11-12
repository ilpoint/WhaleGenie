using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One staged global variable definition. The project-level manager and the "set variable" dialog
/// both bind to this shape, so the same name, type, default and persistence are read one way
/// whichever entry opened them. The current value is what a run has already put there: it is shown,
/// never typed, because nothing at design time can know what a run will hold.
/// </summary>
public sealed partial class VariableDefinitionDraft : ObservableObject
{
    public VariableDefinitionDraft(VariableDefinition? source = null,
        IReadOnlyList<VariableUse>? uses = null)
    {
        OriginalName = source?.Name ?? string.Empty;
        Id = source?.Id ?? string.Empty;
        Name = source?.Name ?? string.Empty;
        Type = source is { } given ? VariableTypes.Name(given.Type) : "text";
        OriginalType = Type;
        DefaultValue = source?.DefaultValue ?? string.Empty;
        OriginalDefaultValue = DefaultValue;
        Description = source?.Description ?? string.Empty;
        OriginalDescription = Description;
        PersistValue = source?.PersistValue ?? false;
        OriginalPersistValue = PersistValue;
        CurrentValue = source is { Scope: VariableScope.Global } ? CurrentText(source) : string.Empty;
        Uses = uses ?? [];
    }

    public string OriginalName { get; }

    /// <summary>
    /// The identity this declaration already had, kept so that editing its name, type or default
    /// does not make every reference to it stale. A draft of a variable that does not exist yet
    /// carries none, and one is given when the project next settles its declarations.
    /// </summary>
    public string Id { get; }
    public string OriginalType { get; }
    public string OriginalDefaultValue { get; }
    public string OriginalDescription { get; }
    public bool OriginalPersistValue { get; }
    public bool IsNew => OriginalName.Length == 0;
    public bool TypeChanged => !string.Equals(Type, OriginalType, StringComparison.Ordinal);
    public IReadOnlyList<VariableUse> Uses { get; }
    public IVariableResolver? Resolver { get; set; } = SharedVariables.Current;
    public bool IsChanged => IsNew
        || !string.Equals(Name.Trim(), OriginalName.Trim(), StringComparison.Ordinal)
        || !string.Equals(Type, OriginalType, StringComparison.Ordinal)
        || !string.Equals(DefaultValue.Trim(), OriginalDefaultValue.Trim(), StringComparison.Ordinal)
        || !string.Equals(Description.Trim(), OriginalDescription.Trim(), StringComparison.Ordinal)
        || PersistValue != OriginalPersistValue;

    public string NameProblem { get; private set; } = string.Empty;
    public string DefaultProblem { get; private set; } = string.Empty;
    public string RenameProblem { get; private set; } = string.Empty;
    public bool HasNameProblem => NameProblem.Length > 0;
    public bool HasDefaultProblem => DefaultProblem.Length > 0;
    public bool HasRenameProblem => RenameProblem.Length > 0;
    public bool HasProblem => HasNameProblem || HasDefaultProblem || HasRenameProblem;

    public IReadOnlyList<ActionParameterOption> TypeOptions { get; private set; } = TypeChoices();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedType))]
    [NotifyPropertyChangedFor(nameof(DefaultPreview))]
    [NotifyPropertyChangedFor(nameof(DefaultHasError))]
    [NotifyPropertyChangedFor(nameof(LocalType))]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string Type { get; set; } = "text";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultPreview))]
    [NotifyPropertyChangedFor(nameof(DefaultHasError))]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string DefaultValue { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial bool PersistValue { get; set; }

    /// <summary>
    /// What the running program holds under this name, or the persisted value, or the default when
    /// neither exists. Read once when the manager opens: a run that writes the variable afterwards
    /// is not something a staged edit should follow.
    /// </summary>
    public string CurrentValue { get; }

    public bool HasUses => Uses.Count > 0;
    public string DisplayName => Name.Trim().Length == 0
        ? Strings.Get("Variable.New")
        : Name.Trim();
    public string LocalType => Strings.Get($"Variable.Type.{Type}", Type);

    public ActionParameterOption SelectedType
    {
        get => TypeOptions.FirstOrDefault(option => option.Value == Type) ?? TypeOptions[0];
        set
        {
            if (Type != value.Value)
            {
                Type = value.Value;
            }
        }
    }

    public string DefaultPreview =>
        VariableValues.Preview(VariableTypes.KindOf(Type), DefaultValue, Resolver);
    public bool DefaultHasError =>
        VariableValues.HasError(VariableTypes.KindOf(Type), DefaultValue, Resolver);

    public VariableDefinition ToDefinition() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        Scope = VariableScope.Global,
        Type = VariableTypes.KindOf(Type),
        DefaultValue = DefaultValue.Trim(),
        Description = Description.Trim(),
        PersistValue = PersistValue,
    };

    public void RefreshLanguage()
    {
        TypeOptions = TypeChoices();
        OnPropertyChanged(nameof(TypeOptions));
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(string.Empty);
    }

    public void RefreshValues()
    {
        OnPropertyChanged(nameof(DefaultPreview));
        OnPropertyChanged(nameof(DefaultHasError));
    }

    internal void SetProblems(string name, string defaultValue, string rename)
    {
        var changed = !string.Equals(NameProblem, name, StringComparison.Ordinal)
            || !string.Equals(DefaultProblem, defaultValue, StringComparison.Ordinal)
            || !string.Equals(RenameProblem, rename, StringComparison.Ordinal);
        NameProblem = name;
        DefaultProblem = defaultValue;
        RenameProblem = rename;
        if (!changed)
        {
            return;
        }

        OnPropertyChanged(nameof(NameProblem));
        OnPropertyChanged(nameof(DefaultProblem));
        OnPropertyChanged(nameof(RenameProblem));
        OnPropertyChanged(nameof(HasNameProblem));
        OnPropertyChanged(nameof(HasDefaultProblem));
        OnPropertyChanged(nameof(HasRenameProblem));
        OnPropertyChanged(nameof(HasProblem));
    }

    private static string CurrentText(VariableDefinition definition)
    {
        if (SharedVariables.Current.TryGet(definition.Name, out var live))
        {
            return live.AsText();
        }

        return PersistentVariables.TryGet(definition.Name, out var saved)
            ? saved
            : definition.DefaultValue;
    }

    private static IReadOnlyList<ActionParameterOption> TypeChoices() =>
        [.. VariableTypes.Declarable.Select(kind => new ActionParameterOption(
            VariableTypes.Name(kind),
            Strings.Get($"Variable.Type.{VariableTypes.Name(kind)}", VariableTypes.Name(kind))))];
}
