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
/// One staged variable definition. The macro editor and the project-level variable manager both
/// bind to this shape, so the same name, type, default, persistence and current value are read one
/// way whichever entry opened them.
/// </summary>
public sealed partial class VariableDefinitionDraft : ObservableObject
{
    private bool _initializing;

    public VariableDefinitionDraft(VariableDefinition? source = null,
        IReadOnlyList<VariableUse>? uses = null)
    {
        OriginalName = source?.Name ?? string.Empty;
        _initializing = true;
        Name = source?.Name ?? string.Empty;
        Type = source?.Type ?? "text";
        OriginalType = Type;
        DefaultValue = source?.DefaultValue ?? string.Empty;
        OriginalDefaultValue = DefaultValue;
        Description = source?.Description ?? string.Empty;
        OriginalDescription = Description;
        PersistValue = source?.PersistValue ?? false;
        OriginalPersistValue = PersistValue;
        CurrentValue = source is { Scope: VariableScope.Global }
            ? CurrentText(source)
            : string.Empty;
        CurrentOriginal = CurrentValue;
        Uses = uses ?? [];
        _initializing = false;
    }

    public string OriginalName { get; }
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
        || PersistValue != OriginalPersistValue
        || CurrentValueDirty;

    public string NameProblem { get; private set; } = string.Empty;
    public string DefaultProblem { get; private set; } = string.Empty;
    public string CurrentProblem { get; private set; } = string.Empty;
    public string RenameProblem { get; private set; } = string.Empty;
    public bool HasNameProblem => NameProblem.Length > 0;
    public bool HasDefaultProblem => DefaultProblem.Length > 0;
    public bool HasCurrentProblem => CurrentProblem.Length > 0;
    public bool HasRenameProblem => RenameProblem.Length > 0;
    public bool HasProblem => HasNameProblem || HasDefaultProblem || HasCurrentProblem
        || HasRenameProblem;

    public IReadOnlyList<ActionParameterOption> TypeOptions { get; private set; } =
        TypeChoices();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedType))]
    [NotifyPropertyChangedFor(nameof(DefaultPreview))]
    [NotifyPropertyChangedFor(nameof(DefaultHasError))]
    [NotifyPropertyChangedFor(nameof(CurrentPreview))]
    [NotifyPropertyChangedFor(nameof(CurrentHasError))]
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPreview))]
    [NotifyPropertyChangedFor(nameof(CurrentHasError))]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial string CurrentValue { get; set; } = string.Empty;

    public string CurrentOriginal { get; private set; }
    public bool CurrentValueDirty { get; private set; }
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

    public string DefaultPreview => VariableValues.Preview(Type, DefaultValue, Resolver);
    public bool DefaultHasError => VariableValues.HasError(Type, DefaultValue, Resolver);
    public string CurrentPreview => VariableValues.Preview(Type, CurrentValue, Resolver);
    public bool CurrentHasError => VariableValues.HasError(Type, CurrentValue, Resolver);

    public VariableDefinition ToDefinition() => new()
    {
        Name = Name.Trim(),
        Scope = VariableScope.Global,
        Type = Type,
        DefaultValue = DefaultValue.Trim(),
        Description = Description.Trim(),
        PersistValue = PersistValue,
    };

    public void AcceptCurrent()
    {
        CurrentOriginal = CurrentValue;
        CurrentValueDirty = false;
        OnPropertyChanged(nameof(IsChanged));
    }

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
        OnPropertyChanged(nameof(CurrentPreview));
        OnPropertyChanged(nameof(CurrentHasError));
    }

    internal void SetProblems(string name, string defaultValue, string currentValue,
        string rename)
    {
        var changed = !string.Equals(NameProblem, name, StringComparison.Ordinal)
            || !string.Equals(DefaultProblem, defaultValue, StringComparison.Ordinal)
            || !string.Equals(CurrentProblem, currentValue, StringComparison.Ordinal)
            || !string.Equals(RenameProblem, rename, StringComparison.Ordinal);
        NameProblem = name;
        DefaultProblem = defaultValue;
        CurrentProblem = currentValue;
        RenameProblem = rename;
        if (!changed)
        {
            return;
        }

        OnPropertyChanged(nameof(NameProblem));
        OnPropertyChanged(nameof(DefaultProblem));
        OnPropertyChanged(nameof(CurrentProblem));
        OnPropertyChanged(nameof(RenameProblem));
        OnPropertyChanged(nameof(HasNameProblem));
        OnPropertyChanged(nameof(HasDefaultProblem));
        OnPropertyChanged(nameof(HasCurrentProblem));
        OnPropertyChanged(nameof(HasRenameProblem));
        OnPropertyChanged(nameof(HasProblem));
    }

    partial void OnCurrentValueChanged(string value)
    {
        if (!_initializing)
        {
            var dirty = !string.Equals(value, CurrentOriginal, StringComparison.Ordinal);
            if (CurrentValueDirty != dirty)
            {
                CurrentValueDirty = dirty;
                OnPropertyChanged(nameof(CurrentValueDirty));
            }

            OnPropertyChanged(nameof(IsChanged));
        }
    }

    partial void OnTypeChanged(string value)
    {
        OnPropertyChanged(nameof(LocalType));
        if (!_initializing)
        {
            CurrentValue = DefaultValue;
            CurrentValueDirty = true;
            OnPropertyChanged(nameof(CurrentValueDirty));
            OnPropertyChanged(nameof(TypeChanged));
        }
    }

    partial void OnDefaultValueChanged(string value)
    {
        if (!IsNew || CurrentValueDirty)
        {
            return;
        }

        _initializing = true;
        CurrentValue = value;
        CurrentOriginal = value;
        _initializing = false;
        OnPropertyChanged(nameof(CurrentPreview));
        OnPropertyChanged(nameof(CurrentHasError));
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
        [.. VariableDefinition.TypeNames.Select(type =>
            new ActionParameterOption(type, Strings.Get($"Variable.Type.{type}", type)))];
}
