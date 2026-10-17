using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia.Media;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Models;

/// <summary>
/// The editor's projection of the engine's action catalogue. The facts — keys, parameters,
/// defaults, choices, reading rules, pickers, input and waiting — come from
/// <see cref="ActionSemanticsCatalog"/>; this file adds only what a person sees: labels, hints,
/// editor controls and icons.
/// </summary>
public static class ActionCatalog
{
    private const string DisplayResource = "WhaleGenie.Models.ActionDisplay.json";

    private static readonly Geometry ControlIcon =
        Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,7 L12,12 L15,14");

    private static readonly Geometry InputIcon =
        Geometry.Parse("M3,7 H21 V17 H3 Z M6,10 H8 M10,10 H12 M14,10 H16 M18,10 H18.01 M6,14 H10 M12,14 H16");

    private static readonly Geometry VisionIcon =
        Geometry.Parse("M2,12 C6,6 18,6 22,12 C18,18 6,18 2,12 Z M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9 Z");

    private static readonly Geometry OcrIcon =
        Geometry.Parse("M5,4 H19 V20 H5 Z M8,9 H16 M8,12.5 H16 M8,16 H13");

    private static readonly Geometry UiaIcon =
        Geometry.Parse("M3,5 H21 V19 H3 Z M3,9 H21 M6,7 H6.01");

    private static readonly Geometry ScriptIcon =
        Geometry.Parse("M3,5 H21 V19 H3 Z M3,8.5 H21 M6,12 L8.5,14.5 L6,17 M11,17 H15");

    private static readonly Geometry FileIcon =
        Geometry.Parse("M6,3 H14 L18,7 V21 H6 Z M14,3 V7 H18 M9,12 H15 M9,16 H15");

    private static readonly Geometry DataIcon =
        Geometry.Parse("M9,4 L6,12 L9,20 M15,4 L18,12 L15,20");

    private static readonly Geometry BrowserIcon =
        Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M3,9 H21 M3,15 H21 M12,3 C9,7 9,17 12,21");

    private static readonly Geometry ClipboardIcon =
        Geometry.Parse("M6,5 H18 V21 H6 Z M9,3 H15 V6 H9 Z M9,11 H15 M9,15 H13");

    private static readonly Geometry ProcessIcon =
        Geometry.Parse("M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 " +
                       "M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22 " +
                       "M5,5 L7,7 M17,17 L19,19 M19,5 L17,7 M7,17 L5,19");

    private static readonly Geometry SystemIcon =
        Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,10.5 V16.5 M12,7.5 H12.01");

    private static readonly Geometry WindowIcon =
        Geometry.Parse("M3,5 H21 V19 H3 Z M3,9 H21 M6,7 H6.01 M13,12 H18 V16 H13 Z");

    private static readonly Geometry ConditionIcon =
        Geometry.Parse("M12,3 A2.5,2.5 0 1 0 12,8 A2.5,2.5 0 1 0 12,3 M12,8 V12.5 " +
                       "M12,12.5 L7,15.5 M12,12.5 L17,15.5 " +
                       "M7,15.5 A2.5,2.5 0 1 0 7,20.5 A2.5,2.5 0 1 0 7,15.5 " +
                       "M17,15.5 A2.5,2.5 0 1 0 17,20.5 A2.5,2.5 0 1 0 17,15.5");

    static ActionCatalog()
    {
        var display = LoadDisplay();
        var byKey = display.ToDictionary(action => action.Key, StringComparer.Ordinal);
        var definitions = new List<ActionDefinition>(ActionSemanticsCatalog.All.Count);

        foreach (var semantics in ActionSemanticsCatalog.All)
        {
            if (!byKey.TryGetValue(semantics.Key, out var shown))
            {
                throw new InvalidOperationException(
                    $"{semantics.Key} has engine semantics but no display metadata.");
            }

            var shownParameters = shown.Parameters.ToDictionary(
                parameter => parameter.Name, StringComparer.Ordinal);
            var parameters = new List<ActionParameter>(semantics.Parameters.Count);
            foreach (var facts in semantics.Parameters)
            {
                if (!shownParameters.TryGetValue(facts.Name, out var field))
                {
                    throw new InvalidOperationException(
                        $"{semantics.Key}.{facts.Name} has engine semantics but no display metadata.");
                }

                parameters.Add(new ActionParameter
                {
                    Name = facts.Name,
                    Label = field.Label,
                    Semantics = facts,
                    Kind = KindOf(field.Kind, $"{semantics.Key}.{facts.Name}"),
                    Hint = field.Hint,
                    Placeholder = field.Placeholder,
                    OptionLabels = field.OptionLabels,
                    PathIntent = (PathIntent)field.PathIntent,
                    PathFilter = field.PathFilter,
                    EnabledBySibling = field.EnabledBySibling,
                    AppliesWhen = field.AppliesWhen,
                    AppliesWhenValues = field.AppliesWhenValues,
                    AppliesWhenAlso = field.AppliesWhenAlso,
                    AppliesWhenAlsoValues = field.AppliesWhenAlsoValues,
                    EmptiesToEdge = field.EmptiesToEdge,
                    ForeignText = field.ForeignText,
                    Tall = field.Tall,
                    Minimum = field.Minimum,
                    Maximum = field.Maximum,
                    Increment = field.Increment,
                    IsDuration = field.IsDuration,
                    Advanced = field.Advanced,
                    AddLabelKey = field.AddLabelKey,
                });
            }

            var shownReturns = shown.Returns.ToDictionary(result => result.Key, StringComparer.Ordinal);
            var returns = new List<ActionReturn>(semantics.Returns.Count);
            foreach (var facts in semantics.Returns)
            {
                if (!shownReturns.TryGetValue(facts.Key, out var result))
                {
                    throw new InvalidOperationException(
                        $"{semantics.Key}.return.{facts.Key} has engine semantics but no display metadata.");
                }

                returns.Add(new ActionReturn
                {
                    Key = facts.Key,
                    Label = result.Label,
                    Hint = result.Hint,
                    Type = facts.Type,
                    Primary = facts.Primary,
                    OwnerKey = semantics.Key,
                });
            }

            foreach (var parameter in parameters)
            {
                parameter.OwnerKey = semantics.Key;
            }

            definitions.Add(new ActionDefinition
            {
                Key = semantics.Key,
                Semantics = semantics,
                DisplayName = shown.DisplayName,
                Description = shown.Description,
                Parameters = parameters,
                Returns = returns,
            });
        }

        Definitions = definitions;
        ByKey = definitions.ToDictionary(definition => definition.Key, StringComparer.Ordinal);
        Conditions = [.. definitions.Where(definition => definition.IsCondition)];
        RunnableActions = [.. definitions.Where(definition =>
            !definition.IsCondition && !definition.Hidden)];
    }

    /// <summary>Every action and condition, in the order the picker lists them.</summary>
    public static IReadOnlyList<ActionDefinition> Definitions { get; private set; } = [];

    private static readonly Dictionary<string, ActionDefinition> ByKey;

    /// <summary>Catalogue subset used by the condition pickers.</summary>
    public static IReadOnlyList<ActionDefinition> Conditions { get; private set; } = [];

    /// <summary>Everything that can be a step of its own, hidden actions excluded.</summary>
    public static IReadOnlyList<ActionDefinition> RunnableActions { get; private set; } = [];

    /// <summary>Finds a definition by its fully qualified key.</summary>
    public static ActionDefinition? Find(string key)
        => ByKey.TryGetValue(key, out var definition) ? definition : null;

    /// <summary>
    /// The catalogue entries for a fixed list of keys, in the order given. Used where a nested
    /// editor accepts one particular kind of child, such as the cases of a switch.
    /// </summary>
    public static IReadOnlyList<ActionDefinition> ForKeys(IReadOnlyList<string> keys)
        => [.. keys.Select(Find).OfType<ActionDefinition>()];

    /// <summary>Line-art icon shared by every action of a category.</summary>
    public static Geometry? IconFor(ActionCategory category) => category switch
    {
        ActionCategory.Control => ControlIcon,
        ActionCategory.File => FileIcon,
        ActionCategory.Data => DataIcon,
        ActionCategory.Clipboard => ClipboardIcon,
        ActionCategory.Process => ProcessIcon,
        ActionCategory.System => SystemIcon,
        ActionCategory.Window => WindowIcon,
        ActionCategory.Input => InputIcon,
        ActionCategory.Vision => VisionIcon,
        ActionCategory.Ocr => OcrIcon,
        ActionCategory.Uia => UiaIcon,
        ActionCategory.Browser => BrowserIcon,
        ActionCategory.Script => ScriptIcon,
        ActionCategory.Condition => ConditionIcon,
        _ => null,
    };

    private static IReadOnlyList<DisplayAction> LoadDisplay()
    {
        using var stream = typeof(ActionCatalog).Assembly.GetManifestResourceStream(DisplayResource)
            ?? throw new InvalidOperationException(
                $"The action display resource {DisplayResource} is missing.");
        var display = JsonSerializer.Deserialize<List<DisplayAction>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The action display catalogue is empty.");

        if (display.Select(action => action.Key).Distinct(StringComparer.Ordinal).Count()
            != display.Count)
        {
            throw new InvalidOperationException("The action display catalogue repeats an action key.");
        }

        return display;
    }

    private static ActionParameterKind KindOf(string written, string field)
        => Enum.TryParse<ActionParameterKind>(written, ignoreCase: true, out var kind)
            ? kind
            : throw new InvalidOperationException($"{field}: unknown editor kind {written}.");

    /// <summary>One action's display-only fields, read from the embedded JSON.</summary>
    private sealed class DisplayAction
    {
        public required string Key { get; init; }

        public required string DisplayName { get; init; }

        public string Description { get; init; } = string.Empty;

        public IReadOnlyList<DisplayParameter> Parameters { get; init; } = [];

        public IReadOnlyList<DisplayReturn> Returns { get; init; } = [];
    }

    /// <summary>One parameter's display-only fields.</summary>
    private sealed class DisplayParameter
    {
        public required string Name { get; init; }

        public required string Label { get; init; }

        public string Hint { get; init; } = string.Empty;

        public string Placeholder { get; init; } = string.Empty;

        public required string Kind { get; init; }

        public IReadOnlyList<string> OptionLabels { get; init; } = [];

        public string PathFilter { get; init; } = string.Empty;

        public int PathIntent { get; init; }

        public string EnabledBySibling { get; init; } = string.Empty;

        public string AppliesWhen { get; init; } = string.Empty;

        public IReadOnlyList<string> AppliesWhenValues { get; init; } = [];

        public string AppliesWhenAlso { get; init; } = string.Empty;

        public IReadOnlyList<string> AppliesWhenAlsoValues { get; init; } = [];

        public bool EmptiesToEdge { get; init; }

        public bool ForeignText { get; init; }

        public bool Tall { get; init; }

        public decimal Minimum { get; init; }

        public decimal Maximum { get; init; } = StepMeta.LongestPauseMs;

        public decimal Increment { get; init; } = 1m;

        public bool IsDuration { get; init; }

        public bool Advanced { get; init; }

        public string AddLabelKey { get; init; } = string.Empty;
    }

    /// <summary>One result's display-only fields.</summary>
    private sealed class DisplayReturn
    {
        public required string Key { get; init; }

        public required string Label { get; init; }

        public string Hint { get; init; } = string.Empty;
    }
}
