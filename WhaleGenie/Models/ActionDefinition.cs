using System;
using System.Collections.Generic;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>Top-level grouping used by the action catalogue and its icons.</summary>
public enum ActionCategory
{
    Control,
    File,
    Data,
    Clipboard,
    Process,
    System,
    Window,
    Input,
    Vision,
    Ocr,
    Uia,
    Browser,
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
    /// Several pictures the action looks for, edited as one row each. A search tries them in the
    /// order they are listed and goes with the first one that turns up, which is how one step
    /// covers a thing that is drawn differently from one screen to the next — a button whose
    /// caption changed, the same icon at another size — without the macro branching on it.
    /// </summary>
    Images,

    /// <summary>
    /// The places on the screen to look at, edited as a list of rectangles that can be dragged out
    /// one by one: two halves of a screen are two rows rather than one line of punctuation.
    /// </summary>
    Region,

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

    /// <summary>
    /// The name of a step of this macro, chosen from the steps that are in it. The value written
    /// is the step's name and nothing else, because that is what the run knows a step by.
    /// </summary>
    Step,

    /// <summary>
    /// A combination of keys — Ctrl+Shift+S — written in a box one is typed into, with the keyboard
    /// drawn on screen behind a button. It differs from <see cref="Key"/> in what that keyboard does
    /// with the key it hands back: a combination is put together a key at a time, so the key joins
    /// what is written instead of standing in its place.
    /// </summary>
    Keys,

    /// <summary>
    /// A run of combinations, edited as one row each: what the macro presses in order, with a row's
    /// own hold and gap where the beat differs from the rest of the run. A row holding nothing but
    /// empty numbers is left out of the run rather than pressed as an empty combination.
    /// </summary>
    KeySequence,
}

/// <summary>One choice of a <see cref="ActionParameterKind.Choice"/> parameter:
/// the value written to JSON together with the text shown for it.</summary>
public sealed record ActionParameterOption(string Value, string Display);

/// <summary>
/// What a file dialog is being opened for. A path field is typed by hand like any other value —
/// a macro may well build one out of variables — but it is also the one kind of value the machine
/// can offer a list of, and the list is different depending on which way the file is going.
/// </summary>
public enum PathIntent
{
    /// <summary>Not a path: no dialog is offered.</summary>
    None,

    /// <summary>A file that is already there, chosen with the open dialog.</summary>
    Read,

    /// <summary>A file to write, chosen with the save dialog so a new name can be given to it.</summary>
    Write,

    /// <summary>A folder, chosen with the folder dialog.</summary>
    Folder,
}

/// <summary>Describes one parameter of a catalogued action.</summary>
public class ActionParameter
{
    /// <summary>Key used inside the step's <c>params</c> object.</summary>
    public required string Name { get; init; }

    /// <summary>Label shown next to the editor.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Which file dialog the field beside this one offers, or <see cref="PathIntent.None"/> when
    /// the value is not a path at all.
    /// </summary>
    public PathIntent PathIntent { get; init; }

    /// <summary>
    /// The kinds of file the dialog should show first, as a semicolon-separated list of patterns
    /// such as <c>*.csv</c>. Empty means every file, which is what a field that accepts anything
    /// gets.
    /// </summary>
    public string PathFilter { get; init; } = string.Empty;

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
    /// Name of a sibling field this one follows: it only takes part in the step while that field
    /// says one of <see cref="AppliesWhenValues"/>. Empty means it always takes part.
    /// </summary>
    /// <remarks>
    /// This is not the same question as <see cref="Advanced"/>. A folded field is one that is worth
    /// having and not worth showing; a field whose answer nothing reads is not worth showing at all,
    /// and leaving it on the form is how a step comes to look broken — the ways of recognising a
    /// picture ask different questions, and "which hit" has no answer when the way chosen returns
    /// a single one.
    /// </remarks>
    public string AppliesWhen { get; init; } = string.Empty;

    /// <summary>The values of the field named by <see cref="AppliesWhen"/> this field belongs to.</summary>
    public IReadOnlyList<string> AppliesWhenValues { get; init; } = [];

    /// <summary>
    /// True when the value always names a variable rather than holding a literal. The
    /// Variable Center uses it to spot a name a macro uses but nothing defines.
    /// </summary>
    public bool NamesVariable { get; init; }

    /// <summary>
    /// True when the field holds the name of a variable this step **creates**, rather than one it
    /// reads. Those are the names that have to be told apart from each other: two steps writing
    /// into the same name is a mistake nobody sees, so such a field is filled in with a name that
    /// carries the step's own name.
    /// </summary>
    public bool IsOutputVariable => OutputNames.Contains(Name);

    /// <summary>
    /// The parameter names that hold a value the step leaves behind. Written down rather than
    /// asked of the engine, because what an action does with a field is not something the catalogue
    /// declares — and the set is small and stable: <c>resultVariable</c> and the handful of others
    /// that were split off from it to say what the value is.
    /// </summary>
    private static readonly HashSet<string> OutputNames = new(StringComparer.Ordinal)
    {
        "resultVariable",
        "headerVariable",
        "columnsVariable",
        "errorVariable",
        "elapsedVariable",
        "exitCodeVariable",
        "saveTo",
        "itemVariable",
        "indexVariable",
    };

    /// <summary>
    /// True when the text may name variables, so the editor offers them while the field is typed
    /// in. It is what keeps a hint that says "written out or held in a variable" honest: without
    /// it the field is a plain box and the name has to be spelled from memory. The value itself is
    /// still text — the engine fills <c>$name</c> in rather than working anything out.
    /// </summary>
    public bool AcceptsVariables { get; init; }

    /// <summary>
    /// True when the whole value is read as one: a variable, a formula, or plain text. Such a
    /// field is offered the expression editor, because what it builds there is exactly what the
    /// engine reads. False for text that is only filled in, where a formula would be taken
    /// literally and quietly mean something else.
    /// </summary>
    public bool AcceptsFormula { get; init; }

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
