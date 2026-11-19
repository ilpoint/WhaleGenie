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

    /// <summary>A macro names it, but nothing declares it. Usually a typo; the run refuses it.</summary>
    Undeclared,
}

/// <summary>One variable the editor knows about, shown in the Variable Center and the pickers.</summary>
public partial class VariableDefinition : ObservableObject
{
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
    /// The starting value as the line a form shows. This is a rendering, never a second value and
    /// never a way in: a name or a formula is a shape, and a shape arrives through
    /// <see cref="Default"/> from the value window. Rendering it back into an editable box would
    /// turn the name in it into the marks that spell it.
    /// </summary>
    public string DefaultValue => ExpressionPhrase.Display(Default);

    /// <summary>Macro that owns a local variable; empty for system and global variables.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>
    /// True when the program puts a starting value into the table for this name. False for a value
    /// only a run can provide — the round number of a loop — which is undefined outside the run
    /// that writes it rather than starting at the kind's empty value.
    /// </summary>
    public bool Seeded { get; set; } = true;

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
    /// What a starting value is read against: the project's shared values and the built-in ones.
    /// The session this declaration belongs to answers it; a declaration made on its own — a draft,
    /// a check — leaves it empty and its preview then says a name it cannot read is unreadable.
    /// </summary>
    public IVariableResolver? Resolver { get; set; }

    /// <summary>
    /// How the starting value reads to the user. A list shows what is inside it, so its
    /// contents can be checked without opening the macro that fills it in.
    /// </summary>
    public string DefaultPreview => Preview(Type, Default, Resolver);

    /// <summary>True while <see cref="DefaultPreview"/> explains a problem.</summary>
    public bool DefaultHasError => HasError(Type, Default, Resolver);

    /// <summary>True while the preview shows what the list holds rather than a problem.</summary>
    public bool ShowDefaultSummary => IsList && !DefaultHasError;

    /// <summary>Reads a starting value that is already the shape it was written as.</summary>
    public static string Preview(ValueKind type, ExpressionNode shape,
        IVariableResolver? variables = null)
    {
        if (type is not ValueKind.List)
        {
            return VariableValues.Preview(type, shape, variables);
        }

        if (!ListText.TryRead(shape, variables, out var items, out var error))
        {
            return ExpressionText.Describe(error!);
        }

        return items.Count == 0
            ? Strings.Get("Variable.EmptyList")
            : Strings.Format("Variable.ListPreview", items.Count, ListText.Summarize(items));
    }

    /// <summary>True when a shape cannot be read for the given type.</summary>
    public static bool HasError(ValueKind type, ExpressionNode shape,
        IVariableResolver? variables = null)
        => VariableValues.HasError(type, shape, variables);

    /// <summary>Type name in the current interface language.</summary>
    public string LocalType => Strings.Get($"Variable.Type.{VariableTypes.Name(Type)}",
        VariableTypes.Name(Type));

    /// <summary>Scope name in the current interface language.</summary>
    public string LocalScope => Strings.Get($"Variable.Scope.{Scope}", Scope.ToString());

    /// <summary>Macro that owns a local variable, or the word for none for the other scopes.</summary>
    public string LocalOwner => Owner.Length == 0 ? Strings.Get("Common.None") : Owner;

    public override string ToString() => Name;

}
