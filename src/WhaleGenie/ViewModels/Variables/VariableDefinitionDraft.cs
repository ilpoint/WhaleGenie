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
        IReadOnlyList<VariableUse>? uses = null, PersistedVariables? persisted = null,
        IVariableResolver? live = null)
    {
        OriginalName = source?.Name ?? string.Empty;
        Name = source?.Name ?? string.Empty;
        Type = source?.Type ?? ValueKind.Text;
        OriginalType = Type;
        Default = source?.Default ?? Formula.Nothing;
        OriginalDefaultValue = DefaultValue;
        Description = source?.Description ?? string.Empty;
        OriginalDescription = Description;
        PersistValue = source?.PersistValue ?? false;
        OriginalPersistValue = PersistValue;
        Persisted = persisted ?? new PersistedVariables();
        CurrentValue = source is { Scope: VariableScope.Global }
            ? CurrentText(source, Persisted, live)
            : string.Empty;
        Uses = uses ?? [];
    }

    /// <summary>Where a value kept on this machine is read from and written to.</summary>
    public PersistedVariables Persisted { get; }

    public string OriginalName { get; }

    public ValueKind OriginalType { get; }
    public string OriginalDefaultValue { get; }
    public string OriginalDescription { get; }
    public bool OriginalPersistValue { get; }
    public bool IsNew => OriginalName.Length == 0;
    public bool TypeChanged => Type != OriginalType;
    public IReadOnlyList<VariableUse> Uses { get; }
    public IVariableResolver? Resolver { get; set; }
    public bool IsChanged => IsNew
        || !string.Equals(Name.Trim(), OriginalName.Trim(), StringComparison.Ordinal)
        || Type != OriginalType
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
    public partial ValueKind Type { get; set; } = ValueKind.Text;

    /// <summary>The starting value as the shape the declaration will keep. The only value here.</summary>
    public ExpressionNode Default
    {
        get => _default;
        set
        {
            if (Equals(_default, value))
            {
                return;
            }

            _default = value;
            OnPropertyChanged(nameof(Default));
            OnPropertyChanged(nameof(DefaultValue));
            OnPropertyChanged(nameof(DefaultPreview));
            OnPropertyChanged(nameof(DefaultHasError));
            OnPropertyChanged(nameof(IsChanged));
        }
    }

    private ExpressionNode _default = Formula.Nothing;

    /// <summary>
    /// What the box shows. It is a rendering, never a way in: the value arrives through
    /// <see cref="Default"/> from the value window, so a name in it stays a name.
    /// </summary>
    public string DefaultValue => ExpressionPhrase.Display(_default);

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

    public string DefaultPreview =>
        VariableValues.Preview(Type, Default, Resolver);
    public bool DefaultHasError =>
        VariableValues.HasError(Type, Default, Resolver);

    public VariableDefinition ToDefinition() => new()
    {
        Name = Name.Trim(),
        Scope = VariableScope.Global,
        Type = Type,
        Default = Default,
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

    private static string CurrentText(VariableDefinition definition, PersistedVariables persisted,
        IVariableResolver? live)
    {
        if (live?.TryGet(definition.Name, out var current) == true)
        {
            return current.AsText();
        }

        return persisted.TryGet(definition.Name, out var saved)
            ? saved
            : definition.DefaultValue;
    }

    private static IReadOnlyList<ActionParameterOption> TypeChoices() =>
        [.. VariableTypes.Declarable.Select(kind => new ActionParameterOption(
            VariableTypes.Name(kind),
            Strings.Get($"Variable.Type.{VariableTypes.Name(kind)}", VariableTypes.Name(kind))))];
}
