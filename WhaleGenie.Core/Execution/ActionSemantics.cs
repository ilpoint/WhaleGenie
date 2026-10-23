using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhaleGenie.Core.Execution;

/// <summary>The group an action belongs to in the picker and in the step list.</summary>
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
}

/// <summary>What an action's result holds, which is what the variable pickers call it.</summary>
public enum ActionReturnType
{
    Text,
    Number,
    Flag,
    List,
    Record,
    Image,
}

/// <summary>
/// The shape a parameter has when the engine reads it. The editor's choice of control is a
/// separate question; this is only what the value becomes after the read.
/// </summary>
public enum ActionValueShape
{
    Text,
    Number,
    Flag,
    Choice,
    Steps,
    Rows,
    Image,

    /// <summary>A whole record: the value is a set of named fields rather than one thing.</summary>
    Record,

    /// <summary>
    /// One place on the screen, written as a record carrying <c>x</c> and <c>y</c> (the middle of
    /// what it came from) or, when nobody wrote one, as the two numbers beside it.
    /// </summary>
    Point,

    /// <summary>
    /// One rectangle, written as a record carrying <c>left</c>, <c>top</c>, <c>width</c> and
    /// <c>height</c> or, when nobody wrote one, as the four numbers beside it.
    /// </summary>
    Rect,

    /// <summary>A list of values, read whole.</summary>
    List,

    /// <summary>
    /// One window rule: the captured window plus the part and way it is compared. A literal rule
    /// is written as a record, and a variable holding a window record is read whole.
    /// </summary>
    WindowRule,
}

/// <summary>
/// What the number in a field counts. The unit is what gives the field its range and what the
/// editor puts beside the box, so a length of time and a coordinate cannot end up sharing one.
/// </summary>
public enum ActionUnit
{
    /// <summary>Screen pixels. Negative on a display left of or above the primary one.</summary>
    Pixels,

    /// <summary>Milliseconds, the one length of time the file stores.</summary>
    Milliseconds,

    /// <summary>A number of things: rows, repeats, a one-based index.</summary>
    Count,

    /// <summary>
    /// A whole number with a sign: the first and last number a counting loop runs over, and the
    /// two ends a comparison holds a value between.
    /// </summary>
    Integer,

    /// <summary>A share of a whole, from 0 to 100.</summary>
    Percent,

    /// <summary>
    /// A share that has a direction, from -100 to 100: how far a controller stick is pushed.
    /// </summary>
    Axis,
}

/// <summary>
/// What an empty number cell means. The same blank box stands for "zero" in one field and for
/// "the edge of the screen" in another, and the catalogue is what tells them apart.
/// </summary>
public enum ActionEmptyMeans
{
    /// <summary>The number nobody wrote is zero.</summary>
    Zero,

    /// <summary>The side runs from where the rectangle starts to the edge of the screen.</summary>
    ToEdge,

    /// <summary>One refresh of the screen, the soonest a second look can differ from the first.</summary>
    ScreenRefresh,

    /// <summary>The window keeps the position or size it has now.</summary>
    Keep,

    /// <summary>Nobody said, so the value the action starts with applies.</summary>
    Unset,
}

/// <summary>
/// How the whole cell is read. The three cases are exhaustive, so a field cannot be read one way
/// by the engine and another way by the editor.
/// </summary>
public enum ActionReadMode
{
    /// <summary>Text with <c>$name</c> replaced where it appears; <c>$$</c> is one literal dollar.</summary>
    Text,

    /// <summary>The whole cell is one value: a literal, a variable in <c>$name</c> form, or a formula.</summary>
    WholeValue,

    /// <summary>The whole cell is a bare variable name being written.</summary>
    VariableName,
}

/// <summary>Which picker, if any, can fill this parameter from something on the machine.</summary>
public enum ActionPickerKind
{
    None,
    Window,
    Region,
    WindowBounds,
    Element,
    Image,
    Color,
    Browser,
}

/// <summary>Where a step's timeout comes from, or <see cref="None"/> when it has none.</summary>
public enum ActionTimeoutSource
{
    None,
    Step,
    Parameter,
}

/// <summary>
/// Where the fields and results of a step come from. Most actions use their own catalogue entry;
/// a call to another macro takes both from the declaration of the macro it names.
/// </summary>
public enum ActionSignatureSource
{
    /// <summary>The action's own parameters and declared returns.</summary>
    Own,

    /// <summary>The arguments and results declared by the macro named in this step.</summary>
    CalledMacro,
}

/// <summary>One parameter's semantic facts, without anything about how the editor draws it.</summary>
public sealed class ActionParameterSemantics
{
    public required string Name { get; init; }

    public ActionValueShape Shape { get; init; } = ActionValueShape.Text;

    public ActionReadMode ReadMode { get; init; } = ActionReadMode.Text;

    public ActionPickerKind Picker { get; init; }

    public bool Required { get; init; } = true;

    /// <summary>The value written into a new step, in file form.</summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>
    /// What the number counts. Every field whose shape is a number declares one; the unit is what
    /// gives the box its range, so the same 200 cannot be a pause in one field and a row in another.
    /// </summary>
    public ActionUnit? Unit { get; init; }

    /// <summary>What a blank cell comes to. Every number field declares one; other fields do not.</summary>
    public ActionEmptyMeans? EmptyMeans { get; init; }

    /// <summary>
    /// A limit this field has of its own, narrower than its unit's. A picture is never looked at in
    /// fewer than 32 pixels and a window is never made less than one pixel wide, and neither of
    /// those is true of every pixel field.
    /// </summary>
    public decimal? Minimum { get; init; }

    /// <summary>The top of this field's own range, where its action refuses more than the unit says.</summary>
    public decimal? Maximum { get; init; }

    /// <summary>The bottom of this field's box: its own limit, or else the one its unit gives.</summary>
    public decimal MinimumValue => Minimum ?? Unit switch
    {
        ActionUnit.Pixels => -100_000m,
        ActionUnit.Axis => -100m,
        ActionUnit.Integer => -1_000_000m,
        _ => 0m,
    };

    /// <summary>The top of this field's box: its own limit, or else the one its unit gives.</summary>
    public decimal MaximumValue => Maximum ?? Unit switch
    {
        ActionUnit.Pixels => 100_000m,
        ActionUnit.Milliseconds => StepMeta.LongestPauseMs,
        ActionUnit.Percent => 100m,
        ActionUnit.Axis => 100m,
        ActionUnit.Integer => 1_000_000m,
        _ => 1_000_000m,
    };

    /// <summary>The machine values a choice offers, in the order the file writes them.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>
    /// True when the text field offers variable names while it is typed in. The read itself is
    /// still <see cref="ActionReadMode.Text"/>; this only says the names are worth suggesting.
    /// </summary>
    public bool AcceptsVariables { get; init; }

    /// <summary>
    /// The columns a row of this field may hold. A list written by hand that names a column the
    /// action never reads would otherwise be skipped without a word, which looks like the step
    /// ignoring what its author wrote.
    /// </summary>
    public IReadOnlyList<ActionRowColumn> Columns { get; init; } = [];
    /// <summary>The only action keys a step list accepts, or empty for any step.</summary>
    public IReadOnlyList<string> ChildKeys { get; init; } = [];

    /// <summary>
    /// True when the field is required only under some settings of the action — a path for the file
    /// half of a hash, the script text when the source is text. The step's own values decide whether
    /// it applies, so an empty cell is not by itself an unanswered field.
    /// </summary>
    public bool Conditional { get; init; }

    /// <summary>
    /// The failure key an unanswered field reports. Most fields report
    /// <see cref="RunKeys.MissingField"/>; a field whose action already has a word for its absence
    /// (a path, a program, a run handle) keeps that word.
    /// </summary>
    public string MissingKey { get; init; } = RunKeys.MissingField;

    /// <summary>
    /// The failure key an unknown choice reports. Most choices report
    /// <see cref="RunKeys.BadOption"/>; the comparisons and the system actions keep the words they
    /// already had.
    /// </summary>
    public string OptionKey { get; init; } = RunKeys.BadOption;

    /// <summary>
    /// True when a word outside <see cref="Options"/> is read as a fallback rather than refused.
    /// Only the window matching ways and the tab target say so: both are read wherever a window or a
    /// page is recognised, and a word nobody knows has always meant the oldest reading there.
    /// </summary>
    public bool AllowsUnknownOption { get; init; }

    /// <summary>
    /// True when the field still has no answer and none is supplied for it: it is required, the step
    /// wrote nothing, the catalogue offers no default for it, and an empty cell has no meaning of its
    /// own. Numbers are not asked: an empty number is answered by <see cref="EmptyMeans"/>. Nor are
    /// the lists, whose content is in rows rather than in the cell, the shapes that hold other steps
    /// or a condition, and a field that only applies under some settings.
    /// </summary>
    public bool MissingValue(string? written)
        => Required
           && !Conditional
           && (written ?? string.Empty).Trim().Length == 0
           && DefaultValue.Length == 0
           && EmptyMeans is null
           && Shape is not (ActionValueShape.Steps or ActionValueShape.Rows
               or ActionValueShape.Flag);
}

/// <summary>What a column of a row list is for.</summary>
public enum ActionRowColumnRole
{
    /// <summary>The name the action knows the value by.</summary>
    Key,

    /// <summary>The name the caller wants the value under.</summary>
    Name,

    /// <summary>A value read as it stands.</summary>
    Value,

    /// <summary>Free text.</summary>
    Text,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A picture path or image value.</summary>
    Image,
}

/// <summary>One column a row of a list may hold.</summary>
public sealed class ActionRowColumn
{
    public required string Name { get; init; }

    public ActionRowColumnRole Role { get; init; } = ActionRowColumnRole.Text;

    /// <summary>True when a row that does not name this column is not a row of this list.</summary>
    public bool Required { get; init; }
}
/// <summary>One field of a record a result hands over.</summary>
public sealed class ActionReturnField
{
    public required string Name { get; init; }

    public ActionReturnType Type { get; init; } = ActionReturnType.Text;
}

/// <summary>One result an action can hand over.</summary>
public sealed class ActionReturnSemantics
{
    public required string Key { get; init; }

    public ActionReturnType Type { get; init; } = ActionReturnType.Text;

    /// <summary>
    /// The fields of the record this result holds, or — for a list of records — of each item in it.
    /// A record without them is a shape the macro has to guess at, so the catalogue says.
    /// </summary>
    public IReadOnlyList<ActionReturnField> Fields { get; init; } = [];

    /// <summary>True for the result a step is usually asked for first.</summary>
    public bool Primary { get; init; }
}

/// <summary>
/// Everything the engine and the design-time checks need to know about one action. Display text,
/// icons, editor controls and page placement deliberately do not live here.
/// </summary>
public sealed class ActionSemantics
{
    public required string Key { get; init; }

    public required ActionCategory Category { get; init; }

    public bool Hidden { get; init; }

    /// <summary>True when carrying out this action puts keys, clicks or controller input into the machine.</summary>
    public bool SendsInput { get; init; }

    /// <summary>True when the action reads pixels from the screen.</summary>
    public bool LooksAtScreen { get; init; }

    /// <summary>True while the action waits for something outside the macro before it can finish.</summary>
    public bool Waits { get; init; }

    public ActionTimeoutSource TimeoutSource { get; init; }

    /// <summary>The parameter holding the timeout when <see cref="TimeoutSource"/> is Parameter.</summary>
    public string TimeoutField { get; init; } = string.Empty;

    /// <summary>The step timeout a newly written step starts with, in milliseconds; 0 for none.</summary>
    public int StepTimeoutMs { get; init; }

    /// <summary>
    /// Whether this action's form and result page use its own catalogue entry or the declaration
    /// of another macro. The editor and engine both read this fact instead of recognising action
    /// keys themselves.
    /// </summary>
    public ActionSignatureSource SignatureSource { get; init; }

    public IReadOnlyList<ActionParameterSemantics> Parameters { get; init; } = [];

    public IReadOnlyList<ActionReturnSemantics> Returns { get; init; } = [];

    /// <summary>The parameter with that name, or null when the action has none.</summary>
    public ActionParameterSemantics? Parameter(string name)
        => Parameters.FirstOrDefault(parameter => parameter.Name == name);

    /// <summary>The result with that key, or null when this action does not declare it.</summary>
    public ActionReturnSemantics? Return(string key)
        => Returns.FirstOrDefault(result =>
            string.Equals(result.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The one catalogue of action facts. It is embedded in the engine rather than read from the
/// interface so a headless run, a design-time check and the dialog all answer from the same data.
/// </summary>
public static class ActionSemanticsCatalog
{
    private const string Resource = "WhaleGenie.Core.Execution.ActionSemantics.json";

    private static readonly Dictionary<string, ActionSemantics> ByKey;

    static ActionSemanticsCatalog()
    {
        using var stream = typeof(ActionSemanticsCatalog).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The action catalogue resource {Resource} is missing.");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var actions = JsonSerializer.Deserialize<List<ActionSemantics>>(stream, options)
            ?? throw new InvalidOperationException("The action catalogue is empty.");

        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.Key))
            {
                throw new InvalidOperationException("An action has no key.");
            }

            if (action.TimeoutSource == ActionTimeoutSource.Step && action.StepTimeoutMs <= 0)
            {
                throw new InvalidOperationException($"{action.Key}: a step timeout has no length.");
            }

            if (action.TimeoutSource == ActionTimeoutSource.Parameter
                && action.Parameter(action.TimeoutField) is null)
            {
                throw new InvalidOperationException(
                    $"{action.Key}: timeout field {action.TimeoutField} is not a parameter.");
            }

            if (action.SignatureSource == ActionSignatureSource.CalledMacro
                && (action.Parameter("macro") is null
                    || action.Parameter("returns")?.Shape != ActionValueShape.Rows))
            {
                throw new InvalidOperationException(
                    $"{action.Key}: a called-macro signature needs a macro field and a returns list.");
            }

            if (action.Parameters.Select(parameter => parameter.Name)
                .Distinct(StringComparer.Ordinal).Count() != action.Parameters.Count)
            {
                throw new InvalidOperationException($"{action.Key}: a parameter name is repeated.");
            }

            if (action.Returns.Select(result => result.Key)
                .Distinct(StringComparer.Ordinal).Count() != action.Returns.Count)
            {
                throw new InvalidOperationException($"{action.Key}: a return key is repeated.");
            }

            foreach (var result in action.Returns)
            {
                if (result.Fields.Select(field => field.Name)
                    .Distinct(StringComparer.Ordinal).Count() != result.Fields.Count)
                {
                    throw new InvalidOperationException(
                        $"{action.Key}.{result.Key}: a record field is repeated.");
                }

                if (result.Fields.Count > 0
                    && result.Type is not (ActionReturnType.Record or ActionReturnType.List))
                {
                    throw new InvalidOperationException(
                        $"{action.Key}.{result.Key}: only a record has fields.");
                }

                if (result.Type == ActionReturnType.Record && result.Fields.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"{action.Key}.{result.Key}: a record does not say what its fields are.");
                }
            }

            foreach (var parameter in action.Parameters)
            {
                if (parameter.Shape != ActionValueShape.Number)
                {
                    if (parameter.Unit is not null || parameter.EmptyMeans is not null)
                    {
                        throw new InvalidOperationException(
                            $"{action.Key}.{parameter.Name}: only a number declares a unit.");
                    }

                    continue;
                }

                if (parameter.Unit is null || parameter.EmptyMeans is null)
                {
                    throw new InvalidOperationException(
                        $"{action.Key}.{parameter.Name}: a number has no unit or no empty meaning.");
                }
            }
        }

        ByKey = actions.ToDictionary(action => action.Key, StringComparer.Ordinal);
        All = actions;
        Runnable = [.. actions.Where(action => !action.Hidden)];
    }

    /// <summary>Every action and condition, in the order the picker lists them.</summary>
    public static IReadOnlyList<ActionSemantics> All { get; private set; } = [];

    /// <summary>Everything that can be a step of its own.</summary>
    public static IReadOnlyList<ActionSemantics> Runnable { get; private set; } = [];

    /// <summary>Finds one action by its fully qualified key.</summary>
    public static ActionSemantics? Find(string key)
        => ByKey.GetValueOrDefault(key);
}
