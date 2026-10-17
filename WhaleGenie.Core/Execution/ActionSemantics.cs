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
    Condition,
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
    Condition,
    Rows,
    Image,
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

    /// <summary>The machine values a choice offers, in the order the file writes them.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>
    /// True when the text field offers variable names while it is typed in. The read itself is
    /// still <see cref="ActionReadMode.Text"/>; this only says the names are worth suggesting.
    /// </summary>
    public bool AcceptsVariables { get; init; }

    /// <summary>True when a step list accepts conditions rather than ordinary steps.</summary>
    public bool ConditionsOnly { get; init; }

    /// <summary>The only action keys a step list accepts, or empty for any step.</summary>
    public IReadOnlyList<string> ChildKeys { get; init; } = [];
}

/// <summary>One result an action can hand over.</summary>
public sealed class ActionReturnSemantics
{
    public required string Key { get; init; }

    public ActionReturnType Type { get; init; } = ActionReturnType.Text;

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

    /// <summary>True for actions asked inside an if, a loop or a wait rather than run as a step.</summary>
    public bool IsCondition { get; init; }

    /// <summary>True when the action reads pixels from the screen.</summary>
    public bool LooksAtScreen { get; init; }

    /// <summary>True while the action waits for something outside the macro before it can finish.</summary>
    public bool Waits { get; init; }

    public ActionTimeoutSource TimeoutSource { get; init; }

    /// <summary>The parameter holding the timeout when <see cref="TimeoutSource"/> is Parameter.</summary>
    public string TimeoutField { get; init; } = string.Empty;

    /// <summary>The step timeout a newly written step starts with, in milliseconds; 0 for none.</summary>
    public int StepTimeoutMs { get; init; }

    public IReadOnlyList<ActionParameterSemantics> Parameters { get; init; } = [];

    public IReadOnlyList<ActionReturnSemantics> Returns { get; init; } = [];

    /// <summary>The parameter with that name, or null when the action has none.</summary>
    public ActionParameterSemantics? Parameter(string name)
        => Parameters.FirstOrDefault(parameter => parameter.Name == name);
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

            if (action.IsCondition != (action.Category == ActionCategory.Condition))
            {
                throw new InvalidOperationException(
                    $"{action.Key}: IsCondition does not match its category.");
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
        }

        ByKey = actions.ToDictionary(action => action.Key, StringComparer.Ordinal);
        All = actions;
        Conditions = [.. actions.Where(action => action.IsCondition)];
        Runnable = [.. actions.Where(action => !action.IsCondition && !action.Hidden)];
    }

    /// <summary>Every action and condition, in the order the picker lists them.</summary>
    public static IReadOnlyList<ActionSemantics> All { get; private set; } = [];

    /// <summary>The conditions offered inside if, while and wait.</summary>
    public static IReadOnlyList<ActionSemantics> Conditions { get; private set; } = [];

    /// <summary>Everything that can be a step of its own.</summary>
    public static IReadOnlyList<ActionSemantics> Runnable { get; private set; } = [];

    /// <summary>Finds one action by its fully qualified key.</summary>
    public static ActionSemantics? Find(string key)
        => ByKey.GetValueOrDefault(key);
}
