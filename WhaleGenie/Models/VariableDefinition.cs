using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>Where a variable lives, and who is allowed to change it.</summary>
public enum VariableScope
{
    /// <summary>Provided by WhaleGenie itself and read-only.</summary>
    System,

    /// <summary>Shared by every macro. Only the Variable Center creates these.</summary>
    Global,

    /// <summary>Owned by one macro and visible only inside it.</summary>
    Local,

    /// <summary>A macro names it, but no step defines it. Usually a typo.</summary>
    Unknown,
}

/// <summary>One variable the editor knows about, shown in the Variable Center and the pickers.</summary>
public partial class VariableDefinition : ObservableObject
{
    /// <summary>The stable identity a reference binds to; empty on a system value, which has none.</summary>
    public string Id { get; set; } = string.Empty;

    public required string Name { get; set; }

    public VariableScope Scope { get; set; }

    /// <summary>What this variable holds. Text is the one kind every value can be read as.</summary>
    public ValueKind Type { get; set; } = ValueKind.Text;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Value the variable starts with, as the shape it was written in. A name is a name and a list
    /// is a list; nothing has to read this back out of a line before it can be used.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultValue))]
    [NotifyPropertyChangedFor(nameof(DefaultPreview))]
    [NotifyPropertyChangedFor(nameof(DefaultHasError))]
    [NotifyPropertyChangedFor(nameof(ShowDefaultSummary))]
    public partial ExpressionNode Default { get; set; } = Formula.Nothing;

    /// <summary>
    /// The starting value as the line a form shows. Setting it reads the line into a shape, which is
    /// what a person typing into the field does; a value that arrives as a shape is set through
    /// <see cref="Default"/> instead, so nothing it carries is lost on the way in.
    /// </summary>
    public string DefaultValue
    {
        get => FormulaText.Write(Default);
        set => Default = Formula.FromText(value);
    }

    /// <summary>Macro that owns a local variable; empty for system and global variables.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Whether this global's current value is kept for this user between program runs.</summary>
    [ObservableProperty]
    public partial bool PersistValue { get; set; }

    /// <summary>
    /// Value shown in the Variable Center. System variables are read live; local ones are
    /// only known while their macro runs.
    /// </summary>
    [ObservableProperty]
    public partial string CurrentValue { get; set; } = string.Empty;

    /// <summary>Description in the current interface language.</summary>
    public string LocalDescription => Strings.Get($"Variable.{Name}.desc", Description);

    /// <summary>
    /// How this variable is named on screen: the Chinese name with the code name beside it where it
    /// has one, and the name the macro writes otherwise. What is written into a field is still the
    /// code name — this is what the tables that list variables show.
    /// </summary>
    public string Display => VariableCatalog.Display(Name);

    /// <summary>True when this variable holds a list of values rather than a single value.</summary>
    public bool IsList => Type is ValueKind.List;

    /// <summary>
    /// How the starting value reads to the user. A list shows what is inside it, so its
    /// contents can be checked without opening the macro that fills it in.
    /// </summary>
    public string DefaultPreview => Preview(Type, DefaultValue);

    /// <summary>True while <see cref="DefaultPreview"/> explains a problem.</summary>
    public bool DefaultHasError => HasError(Type, DefaultValue);

    /// <summary>True while the preview shows what the list holds rather than a problem.</summary>
    public bool ShowDefaultSummary => IsList && !DefaultHasError;

    /// <summary>Reads a starting value the way the Variable Center shows it.</summary>
    public static string Preview(ValueKind type, string value)
    {
        if (type is not ValueKind.List)
        {
            return VariableValues.Preview(type, value, VariableValues.DesignContext());
        }

        if (value.Trim().Length == 0)
        {
            return Strings.Get("Variable.EmptyList");
        }

        return ListText.TryParse(value, out var items, out var error)
            ? Strings.Format("Variable.ListPreview", items.Count, ListText.Summarize(items))
            : ExpressionText.Describe(error!);
    }

    /// <summary>True when a starting value cannot be read for the given type.</summary>
    public static bool HasError(ValueKind type, string value)
        => VariableValues.HasError(type, value, VariableValues.DesignContext());

    /// <summary>Type name in the current interface language.</summary>
    public string LocalType => Strings.Get($"Variable.Type.{VariableTypes.Name(Type)}",
        VariableTypes.Name(Type));

    /// <summary>Scope name in the current interface language.</summary>
    public string LocalScope => Strings.Get($"Variable.Scope.{Scope}", Scope.ToString());

    /// <summary>Macro that owns a local variable, or the word for none for the other scopes.</summary>
    public string LocalOwner => Owner.Length == 0 ? Strings.Get("Common.None") : Owner;

    /// <summary>Type choices for the editor dropdown, with translated text.</summary>
    public IReadOnlyList<ActionParameterOption> TypeOptions { get; } =
        [.. VariableTypes.Declarable.Select(kind => new ActionParameterOption(
            VariableTypes.Name(kind),
            Strings.Get($"Variable.Type.{VariableTypes.Name(kind)}", VariableTypes.Name(kind))))];

    /// <summary>The chosen type, bound to the dropdown in the Variable Center.</summary>
    public ActionParameterOption SelectedType
    {
        get => TypeOptions.FirstOrDefault(option => option.Value == VariableTypes.Name(Type))
            ?? TypeOptions[0];
        set
        {
            var kind = VariableTypes.KindOf(value.Value);
            if (Type == kind)
            {
                return;
            }

            Type = kind;
            OnPropertyChanged(nameof(SelectedType));
            OnPropertyChanged(nameof(IsList));
            OnPropertyChanged(nameof(DefaultPreview));
            OnPropertyChanged(nameof(DefaultHasError));
            OnPropertyChanged(nameof(ShowDefaultSummary));
        }
    }

    public override string ToString() => Name;

}
