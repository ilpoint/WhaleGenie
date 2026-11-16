using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

    static ActionCatalog()
    {
        var display = LoadDisplay();
        Display = [.. display.Select(Shown)];

        // Both files have to describe the same actions, and every action the same parameters and
        // results. The whole list is collected before anything is thrown, so a broken pair of files
        // is one message naming every gap rather than a message naming whichever gap came first —
        // fixing them one startup at a time is how a report gets read as "it is always broken".
        var issues = ActionContract.Check(ActionSemanticsCatalog.All, Display);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                "The action catalogues disagree:" + Environment.NewLine
                + string.Join(Environment.NewLine, issues));
        }

        var byKey = display.ToDictionary(action => action.Key, StringComparer.Ordinal);
        var definitions = new List<ActionDefinition>(ActionSemanticsCatalog.All.Count);

        foreach (var semantics in ActionSemanticsCatalog.All)
        {
            var shown = byKey[semantics.Key];
            var parameters = new List<ActionParameter>(semantics.Parameters.Count);
            foreach (var facts in semantics.Parameters)
            {
                var field = shown.Fields.GetValueOrDefault(facts.Name) ?? new DisplayField();
                parameters.Add(new ActionParameter
                {
                    Name = facts.Name,
                    Label = field.Label,
                    Semantics = facts,
                    Kind = KindOf(facts.Shape, facts.ValueForm, facts.Picker,
                        facts.Options.Count > 0, field.Kind, $"{semantics.Key}.{facts.Name}"),
                    Hint = field.Hint,
                    Placeholder = field.Placeholder,
                    OptionLabels = field.OptionLabels,
                    PathIntent = (PathIntent)field.PathIntent,
                    PathFilter = field.PathFilter,
                    PathFilterLabel = field.PathFilterLabel,
                    EnabledBySibling = field.EnabledBySibling,
                    AppliesWhen = field.AppliesWhen,
                    AppliesWhenValues = field.AppliesWhenValues,
                    AppliesWhenAlso = field.AppliesWhenAlso,
                    AppliesWhenAlsoValues = field.AppliesWhenAlsoValues,
                    ForeignText = field.ForeignText,
                    Increment = field.Increment,
                    Advanced = field.Advanced,
                    PerChannelPercent = field.PerChannelPercent,
                    WrittenByTool = field.WrittenByTool,
                    AddLabelKey = field.AddLabelKey,
                });
            }

            var returns = new List<ActionReturn>(semantics.Returns.Count);
            foreach (var facts in semantics.Returns)
            {
                var result = shown.Results.GetValueOrDefault(facts.Key) ?? new DisplayResult();
                returns.Add(new ActionReturn
                {
                    Key = facts.Key,
                    Label = result.Label,
                    Hint = result.Hint,
                    Type = facts.Type,
                    Fields = facts.Fields,
                    Table = facts.Table,
                    Primary = facts.Primary,
                    Hidden = facts.Hidden,
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
                DisplayName = string.Empty,
                Description = string.Empty,
                Preview = PreviewOf(shown.Preview, semantics.Key),
                Parameters = parameters,
                Returns = returns,
            });
        }

        Definitions = definitions;
        ByKey = definitions.ToDictionary(definition => definition.Key, StringComparer.Ordinal);
        RunnableActions = [.. definitions.Where(definition => !definition.Hidden)];
        PlaceableActions = [.. definitions.Where(definition => !definition.Hidden)];
    }

    /// <summary>
    /// The display file's own facts, in the shape the contract check reads. Exposed so the check can
    /// be run against the shipped files from a test rather than only when the program starts.
    /// </summary>
    public static IReadOnlyList<ActionContractDisplay> Display { get; } = [];

    /// <summary>Every action and condition, in the order the picker lists them.</summary>
    public static IReadOnlyList<ActionDefinition> Definitions { get; private set; } = [];

    private static readonly Dictionary<string, ActionDefinition> ByKey;

    /// <summary>
    /// Everything a new macro may use: hidden and retired actions left out. This is what the palette
    /// and the step pickers list.
    /// </summary>
    public static IReadOnlyList<ActionDefinition> RunnableActions { get; private set; } = [];

    /// <summary>
    /// Everything that can sit in a step list, including the actions no longer offered. A macro
    /// written before an action was retired still has to open without being told it is wrong.
    /// </summary>
    public static IReadOnlyList<ActionDefinition> PlaceableActions { get; private set; } = [];

    /// <summary>Finds a definition by its fully qualified key.</summary>
    public static ActionDefinition? Find(string key)
        => ByKey.TryGetValue(key, out var definition) ? definition : null;

    /// <summary>
    /// The catalogue entries for a fixed list of keys, in the order given. Used where a nested
    /// editor accepts one particular kind of child, such as the cases of a switch.
    /// </summary>
    public static IReadOnlyList<ActionDefinition> ForKeys(IReadOnlyList<string> keys)
        => [.. keys.Select(Find).OfType<ActionDefinition>()];

    private static IReadOnlyList<DisplayAction> LoadDisplay()
    {
        using var stream = typeof(ActionCatalog).Assembly.GetManifestResourceStream(DisplayResource)
            ?? throw new InvalidOperationException(
                $"The action display resource {DisplayResource} is missing.");
        var display = JsonSerializer.Deserialize<List<DisplayAction>>(stream, DisplayOptions)
            ?? throw new InvalidOperationException("The action display catalogue is empty.");

        return display;
    }

    /// <summary>The one reader of the display resource; built once rather than per load.</summary>
    private static readonly JsonSerializerOptions DisplayOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>One display entry cut down to what the contract check reads.</summary>
    private static ActionContractDisplay Shown(DisplayAction action)
        => new(action.Key,
            [.. action.Fields.Select(field => new ActionContractParameter(field.Key, field.Value.Kind))],
            [.. action.Results.Keys]);

    /// <summary>
    /// The editor control for one field: what the display file asks for, or — when it asks for
    /// nothing — what the shape of the value already implies. A field is only written down when the
    /// obvious control would be wrong, so the two catalogues cannot disagree about the rest.
    /// </summary>
    private static ActionParameterKind KindOf(ActionValueShape shape, ActionValueForm form,
        ActionPickerKind picker, bool hasOptions, string written, string field)
    {
        if (written.Length > 0)
        {
            return Enum.TryParse<ActionParameterKind>(written, ignoreCase: true, out var kind)
                ? kind
                : throw new InvalidOperationException($"{field}: unknown editor kind {written}.");
        }

        return DefaultKind(shape, form, picker, hasOptions) ?? throw new InvalidOperationException(
            $"{field}: a {shape} field whose editor the shape does not decide needs one.");
    }

    /// <summary>
    /// The control the shape of a value implies, or null when it implies none. A table of rows is
    /// the case that decides nothing: what a row holds differs from one action to the next, so the
    /// display file has to say which table editor to draw.
    /// </summary>
    private static ActionParameterKind? DefaultKind(ActionValueShape shape, ActionValueForm form,
        ActionPickerKind picker, bool hasOptions)
    {
        if (hasOptions)
        {
            return ActionParameterKind.Choice;
        }

        return shape switch
        {
            ActionValueShape.Number => ActionParameterKind.Number,
            ActionValueShape.Bool => ActionParameterKind.Bool,
            ActionValueShape.Choice => ActionParameterKind.Choice,
            ActionValueShape.Steps => ActionParameterKind.Steps,
            ActionValueShape.Rows => picker switch
            {
                ActionPickerKind.Image => ActionParameterKind.Images,
                ActionPickerKind.Region => ActionParameterKind.Region,
                _ => null,
            },
            ActionValueShape.Image => ActionParameterKind.Image,
            ActionValueShape.Point => ActionParameterKind.Point,
            ActionValueShape.Rect => ActionParameterKind.Rect,
            ActionValueShape.WindowRule => ActionParameterKind.WindowRule,
            ActionValueShape.Text when form == ActionValueForm.Name
                => ActionParameterKind.Variable,
            ActionValueShape.Text when picker == ActionPickerKind.Color
                => ActionParameterKind.Color,
            ActionValueShape.Text => ActionParameterKind.Text,
            _ => null,
        };
    }

    /// <summary>
    /// What the form shows live, read the way a field's editor is read: a word the program does not
    /// know is a mistake in the file rather than something to fall back from.
    /// </summary>
    private static ActionPreview PreviewOf(string written, string field)
        => written.Length == 0
            ? ActionPreview.None
            : Enum.TryParse<ActionPreview>(written, ignoreCase: true, out var preview)
                ? preview
                : throw new InvalidOperationException($"{field}: unknown preview {written}.");

    /// <summary>One action's display-only fields, read from the embedded JSON.</summary>
    private sealed class DisplayAction
    {
        public required string Key { get; init; }

        /// <summary>What the form shows live under its fields, or empty for nothing.</summary>
        public string Preview { get; init; } = string.Empty;

        /// <summary>
        /// What each field adds to the form, by the parameter name the engine's catalogue gives it.
        /// The name is the key here rather than a field of its own, so the display file can only
        /// ever say something about a parameter that exists; a name it invents is an orphan the
        /// contract check reports.
        /// </summary>
        public IReadOnlyDictionary<string, DisplayField> Fields { get; init; }
            = new Dictionary<string, DisplayField>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, DisplayResult> Results { get; init; }
            = new Dictionary<string, DisplayResult>(StringComparer.Ordinal);
    }

    /// <summary>One parameter's display-only fields.</summary>
    private sealed class DisplayField
    {
        public string Label { get; init; } = string.Empty;

        public string Hint { get; init; } = string.Empty;

        public string Placeholder { get; init; } = string.Empty;

        /// <summary>The control to draw, when the shape of the value does not already say.</summary>
        public string Kind { get; init; } = string.Empty;

        /// <summary>
        /// True for a percentage that is measured against a colour channel, where "5%" means
        /// ±13 out of 255 and the editor can say so. A share of an area or a probability is not
        /// one of these: the same 5% would be a different number entirely.
        /// </summary>
        public bool PerChannelPercent { get; init; }

        public IReadOnlyList<string> OptionLabels { get; init; } = [];

        public string PathFilter { get; init; } = string.Empty;

        public string PathFilterLabel { get; init; } = string.Empty;

        public int PathIntent { get; init; }

        public string EnabledBySibling { get; init; } = string.Empty;

        public string AppliesWhen { get; init; } = string.Empty;

        public IReadOnlyList<string> AppliesWhenValues { get; init; } = [];

        public string AppliesWhenAlso { get; init; } = string.Empty;

        public IReadOnlyList<string> AppliesWhenAlsoValues { get; init; } = [];

        public bool ForeignText { get; init; }

        public decimal Increment { get; init; } = 1m;

        public bool Advanced { get; init; }

        public bool WrittenByTool { get; init; }

        public string AddLabelKey { get; init; } = string.Empty;
    }

    /// <summary>One result's display-only fields, keyed by the result's own key.</summary>
    private sealed class DisplayResult
    {
        public string Label { get; init; } = string.Empty;

        public string Hint { get; init; } = string.Empty;
    }
}
