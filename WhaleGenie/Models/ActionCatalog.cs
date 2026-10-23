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
                    PathFilterLabel = field.PathFilterLabel,
                    EnabledBySibling = field.EnabledBySibling,
                    AppliesWhen = field.AppliesWhen,
                    AppliesWhenValues = field.AppliesWhenValues,
                    AppliesWhenAlso = field.AppliesWhenAlso,
                    AppliesWhenAlsoValues = field.AppliesWhenAlsoValues,
                    ForeignText = field.ForeignText,
                    Tall = field.Tall,
                    Increment = field.Increment,
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
                    Fields = facts.Fields,
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
        RunnableActions = [.. definitions.Where(definition => !definition.Hidden)];
    }

    /// <summary>Every action and condition, in the order the picker lists them.</summary>
    public static IReadOnlyList<ActionDefinition> Definitions { get; private set; } = [];

    private static readonly Dictionary<string, ActionDefinition> ByKey;

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
    public static Lucide.Avalonia.LucideIconKind? IconFor(ActionCategory category) => category switch
    {
        ActionCategory.Control => AppIcons.Workflow,
        ActionCategory.File => AppIcons.File,
        ActionCategory.Data => AppIcons.Variable,
        ActionCategory.Clipboard => AppIcons.Clipboard,
        ActionCategory.Process => AppIcons.Cpu,
        ActionCategory.System => AppIcons.Settings,
        ActionCategory.Window => AppIcons.AppWindow,
        ActionCategory.Input => AppIcons.Keyboard,
        ActionCategory.Vision => AppIcons.Eye,
        ActionCategory.Ocr => AppIcons.TextScan,
        ActionCategory.Uia => AppIcons.CursorClick,
        ActionCategory.Browser => AppIcons.Browser,
        ActionCategory.Script => AppIcons.Code,
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

        public string PathFilterLabel { get; init; } = string.Empty;

        public int PathIntent { get; init; }

        public string EnabledBySibling { get; init; } = string.Empty;

        public string AppliesWhen { get; init; } = string.Empty;

        public IReadOnlyList<string> AppliesWhenValues { get; init; } = [];

        public string AppliesWhenAlso { get; init; } = string.Empty;

        public IReadOnlyList<string> AppliesWhenAlsoValues { get; init; } = [];

        public bool ForeignText { get; init; }

        public bool Tall { get; init; }

        public decimal Increment { get; init; } = 1m;

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
