using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
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
    /// <summary>Names offered by the type dropdown; the display text is translated.</summary>
    public static IReadOnlyList<string> TypeNames { get; } =
        ["text", "number", "bool", "color", "date", "list"];

    public required string Name { get; set; }

    public VariableScope Scope { get; set; }

    /// <summary>One of <see cref="TypeNames"/>: text, number, bool, colour, date or list.</summary>
    public string Type { get; set; } = "text";

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Value the variable starts with, kept as text. A list is written as <c>[1, 2, 3]</c>,
    /// as an expression that builds one, or as plain items separated by commas.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultPreview))]
    [NotifyPropertyChangedFor(nameof(DefaultHasError))]
    [NotifyPropertyChangedFor(nameof(ShowDefaultSummary))]
    public partial string DefaultValue { get; set; } = string.Empty;

    /// <summary>Macro that owns a local variable; empty for system and global variables.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>
    /// Value shown in the Variable Center. System variables are read live; local ones are
    /// only known while their macro runs.
    /// </summary>
    [ObservableProperty]
    public partial string CurrentValue { get; set; } = string.Empty;

    /// <summary>Description in the current interface language.</summary>
    public string LocalDescription => Strings.Get($"Variable.{Name}.desc", Description);

    /// <summary>True when this variable holds a list of values rather than a single value.</summary>
    public bool IsList => Type is "list";

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
    public static string Preview(string type, string value)
    {
        if (type is not "list")
        {
            return value;
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
    public static bool HasError(string type, string value)
        => type is "list" && value.Trim().Length > 0 && !ListText.TryParse(value, out _, out _);

    /// <summary>Type name in the current interface language.</summary>
    public string LocalType => Strings.Get($"Variable.Type.{Type}", Type);

    /// <summary>Scope name in the current interface language.</summary>
    public string LocalScope => Strings.Get($"Variable.Scope.{Scope}", Scope.ToString());

    /// <summary>Macro that owns a local variable, or a dash for the other scopes.</summary>
    public string LocalOwner => Owner.Length == 0 ? "—" : Owner;

    /// <summary>Type choices for the editor dropdown, with translated text.</summary>
    public IReadOnlyList<ActionParameterOption> TypeOptions { get; } =
        [.. TypeNames.Select(name => new ActionParameterOption(name, Strings.Get($"Variable.Type.{name}", name)))];

    /// <summary>The chosen type, bound to the dropdown in the Variable Center.</summary>
    public ActionParameterOption SelectedType
    {
        get => TypeOptions.FirstOrDefault(option => option.Value == Type) ?? TypeOptions[0];
        set
        {
            if (Type == value.Value)
            {
                return;
            }

            Type = value.Value;
            OnPropertyChanged(nameof(SelectedType));
            OnPropertyChanged(nameof(IsList));
            OnPropertyChanged(nameof(DefaultPreview));
            OnPropertyChanged(nameof(DefaultHasError));
            OnPropertyChanged(nameof(ShowDefaultSummary));
        }
    }

    public override string ToString() => Name;
}
