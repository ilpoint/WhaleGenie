using System.Collections.Generic;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>Top-level grouping used by the action catalogue and its icons.</summary>
public enum ActionCategory
{
    Control,
    File,
    Clipboard,
    Process,
    System,
    Window,
    Input,
    Vision,
    Ocr,
    Uia,
    Script,
    Condition,
}

/// <summary>The kind of editor rendered for a single action parameter.</summary>
public enum ActionParameterKind
{
    Text,
    MultilineText,
    Number,
    Bool,
    Choice,
    Key,
    Color,

    /// <summary>
    /// A picture the action looks for on screen, edited with the picture picker and a preview.
    /// </summary>
    Image,

    /// <summary>
    /// The title of a window, edited with free text and the window picker.
    /// </summary>
    Window,

    /// <summary>A list of child steps, rendered as a nested step editor.</summary>
    Steps,

    /// <summary>A single <c>condition.*</c> action, rendered as a condition picker.</summary>
    Condition,

    /// <summary>
    /// A value written as an expression, edited with suggestions for the variables in
    /// scope and for the built-in functions.
    /// </summary>
    Expression,

    /// <summary>Name of a variable, offered as suggestions while staying editable.</summary>
    Variable,

    /// <summary>Name of another macro in the project, offered from a list while staying editable.</summary>
    Macro,
}

/// <summary>One choice of a <see cref="ActionParameterKind.Choice"/> parameter:
/// the value written to JSON together with the text shown for it.</summary>
public sealed record ActionParameterOption(string Value, string Display);

/// <summary>Describes one parameter of a catalogued action.</summary>
public class ActionParameter
{
    /// <summary>Key used inside the step's <c>params</c> object.</summary>
    public required string Name { get; init; }

    /// <summary>Label shown next to the editor.</summary>
    public required string Label { get; init; }

    /// <summary>Fully qualified key of the action this parameter belongs to.</summary>
    internal string OwnerKey { get; set; } = string.Empty;

    /// <summary>Label in the current interface language.</summary>
    public string LocalLabel => Strings.Get($"{OwnerKey}.{Name}.label", Label);

    /// <summary>Hint in the current interface language.</summary>
    public string LocalHint => Strings.Get($"{OwnerKey}.{Name}.hint", Hint);

    public ActionParameterKind Kind { get; init; } = ActionParameterKind.Text;

    /// <summary>Guidance shown under the editor.</summary>
    public string Hint { get; init; } = string.Empty;

    /// <summary>Placeholder text for text-like editors.</summary>
    public string Placeholder { get; init; } = string.Empty;

    /// <summary>Initial value, kept as text and converted when the JSON is written.</summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>When false the parameter may be left empty.</summary>
    public bool Required { get; init; } = true;

    /// <summary>Allowed values when <see cref="Kind"/> is <see cref="ActionParameterKind.Choice"/>.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>
    /// Display text for every entry of <see cref="Options"/>, in the same order.
    /// Empty entries fall back to the raw value.
    /// </summary>
    public IReadOnlyList<string> OptionLabels { get; init; } = [];

    /// <summary>
    /// When set on a <see cref="ActionParameterKind.Steps"/> parameter, that list only takes
    /// conditions, so a logic group can hold other conditions and nest.
    /// </summary>
    public bool ConditionsOnly { get; init; }

    /// <summary>
    /// When set on a <see cref="ActionParameterKind.Steps"/> parameter, that list only takes
    /// these action keys: it is how a switch keeps a case list holding cases and nothing else,
    /// the same way <see cref="ConditionsOnly"/> keeps a group holding conditions.
    /// </summary>
    public IReadOnlyList<string> ChildKeys { get; init; } = [];

    /// <summary>
    /// Resource key of the label for adding to this list, when the list has a name of its own.
    /// Empty means the usual "add step" wording; a switch sets it so the button reads "add case".
    /// </summary>
    public string AddLabelKey { get; init; } = string.Empty;

    /// <summary>
    /// Name of a sibling list parameter this parameter depends on. It only takes part
    /// in the step once that list holds two or more items, which is how the AND / OR / NOT
    /// logic of a condition group stays out of the way while the group has a single condition.
    /// </summary>
    public string EnabledBySibling { get; init; } = string.Empty;

    /// <summary>
    /// True when the value always names a variable rather than holding a literal. The
    /// Variable Center uses it to spot a name a macro uses but nothing defines.
    /// </summary>
    public bool NamesVariable { get; init; }

    /// <summary>
    /// True when the text is written in another language's syntax rather than as a macro value:
    /// a script's body, where <c>$name</c> means that language's own variable and nothing to do
    /// with a variable the macro reads. The editor leaves such text alone when it looks for the
    /// variables a macro uses.
    /// </summary>
    public bool ForeignText { get; init; }

    /// <summary>Choices paired with the text shown for them, honouring any labels.</summary>
    public IReadOnlyList<ActionParameterOption> OptionChoices
    {
        get
        {
            var choices = new List<ActionParameterOption>(Options.Count);
            for (var index = 0; index < Options.Count; index++)
            {
                var label = index < OptionLabels.Count ? OptionLabels[index] : Options[index];
                choices.Add(new ActionParameterOption(Options[index], label));
            }

            return choices;
        }
    }

    public decimal Minimum { get; init; }

    /// <summary>
    /// The top of the number box. A number with a real limit of its own says so; everything else
    /// stops at the longest pause a step may ask for, which is the largest number a macro has a
    /// reason to write.
    /// </summary>
    public decimal Maximum { get; init; } = WhaleGenie.Core.Execution.StepMeta.LongestPauseMs;

    public decimal Increment { get; init; } = 1m;

    /// <summary>
    /// True when the number is a length of time. The editor then offers the units beside the box,
    /// while the step keeps storing milliseconds, so a unit never reaches the engine or the file.
    /// </summary>
    public bool IsDuration { get; init; }

    /// <summary>
    /// True for a setting that is worth having but not worth showing on every step, such as where
    /// the input is sent or how a search is tuned. The dialog keeps these folded away until they
    /// are asked for, which is what stops an action with a dozen parameters from being a wall.
    /// </summary>
    public bool Advanced { get; init; }
}

/// <summary>A single entry of the action catalogue, for example <c>control.delay</c>.</summary>
public class ActionDefinition
{
    /// <summary>Fully qualified action name, for example <c>input.keyPress</c>.</summary>
    public required string Key { get; init; }

    public required ActionCategory Category { get; init; }

    /// <summary>Short human readable name shown in the step list.</summary>
    public required string DisplayName { get; init; }

    /// <summary>One sentence explaining what the action does.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Name shown in the current interface language.</summary>
    public string LocalName => Strings.Get($"{Key}.name", DisplayName);

    /// <summary>Description in the current interface language.</summary>
    public string LocalDescription => Strings.Get($"{Key}.desc", Description);

    public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];

    /// <summary>
    /// True for an action that only makes sense inside a particular container, such as a switch
    /// case. It stays out of the step picker and is reached through its parent instead.
    /// </summary>
    public bool Hidden { get; init; }

    /// <summary>Line-art icon for the category, shared by every action inside it.</summary>
    public Avalonia.Media.Geometry? Icon => ActionCatalog.IconFor(Category);
}
