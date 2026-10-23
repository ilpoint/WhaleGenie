using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>A compact action entry for the MCP list.</summary>
public sealed record McpActionSummary(
    string Key, string Name, string Category, string Description, bool Hidden);

/// <summary>
/// One parameter of an action, in the shape an external author needs to fill in. Record fields
/// and number facts are included because a shape alone does not say how to write the value.
/// </summary>
public sealed record McpActionParameter(
    string Name, string Label, string Kind, bool Required, string Default, string Hint,
    string ReadMode, string Shape, IReadOnlyList<string> Options,
    IReadOnlyList<string> ChildKeys, IReadOnlyList<string> Fields, string? Unit,
    string? EmptyMeans, decimal? Minimum, decimal? Maximum);

/// <summary>One result an action can hand back.</summary>
public sealed record McpActionReturn(string Key, string Label, string Type, bool Primary);

/// <summary>The full description of one action.</summary>
public sealed record McpActionDetails(
    string Key, string Name, string Category, string Description, bool Hidden,
    IReadOnlyList<McpActionParameter> Parameters, IReadOnlyList<McpActionReturn> Returns);

/// <summary>
/// The action catalogue as the MCP tools show it. It is the same catalogue the editor uses; only
/// the shape is flattened so a tool result stays readable and does not carry icons or control
/// objects.
/// </summary>
public static class ActionKnowledge
{
    public static IReadOnlyList<McpActionSummary> Search(string? query, int limit = 10)
    {
        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);

        return
        [
            .. ActionCatalog.Definitions
                .Where(definition => words.Length == 0 || words.All(word =>
                    Contains(definition.Key, word)
                    || Contains(definition.LocalName, word)
                    || Contains(definition.LocalDescription, word)
                    || Contains(definition.Category.ToString(), word)))
                .OrderBy(definition => definition.Key, StringComparer.Ordinal)
                .Take(Math.Clamp(limit, 1, 50))
                .Select(definition => new McpActionSummary(
                    definition.Key,
                    definition.LocalName,
                    definition.Category.ToString(),
                    definition.LocalDescription,
                    definition.Hidden)),
        ];
    }

    public static McpActionDetails? Find(string key)
    {
        var definition = ActionCatalog.Find(key);
        return definition is null ? null : Describe(definition);
    }

    private static McpActionDetails Describe(ActionDefinition definition)
        => new(
            definition.Key,
            definition.LocalName,
            definition.Category.ToString(),
            definition.LocalDescription,
            definition.Hidden,
            [
                .. definition.Parameters.Select(parameter => new McpActionParameter(
                    parameter.Name,
                    parameter.LocalLabel,
                    parameter.Kind.ToString(),
                    parameter.Required,
                    parameter.LocalDefault,
                    parameter.LocalHint,
                    parameter.ReadMode.ToString(),
                    parameter.Shape.ToString(),
                    [.. parameter.Options],
                    [.. parameter.ChildKeys],
                    FieldsOf(parameter.Shape),
                    parameter.Shape == ActionValueShape.Number ? parameter.Unit.ToString() : null,
                    parameter.Shape == ActionValueShape.Number
                        ? parameter.EmptyMeans.ToString()
                        : null,
                    parameter.Shape == ActionValueShape.Number ? parameter.Minimum : null,
                    parameter.Shape == ActionValueShape.Number ? parameter.Maximum : null)),
            ],
            [
                .. definition.Returns.Select(result => new McpActionReturn(
                    result.Key, result.Label, result.Type.ToString(), result.Primary)),
            ]);

    /// <summary>
    /// The names written inside a record, in the order the file writes them. A window rule is read
    /// from the same serialiser the package uses, so this list cannot drift from the field shape.
    /// </summary>
    private static IReadOnlyList<string> FieldsOf(ActionValueShape shape) => shape switch
    {
        ActionValueShape.WindowRule => [.. WindowRule.Empty.ToJson().Select(field => field.Key)],
        ActionValueShape.Point => [FieldNames.X, FieldNames.Y],
        ActionValueShape.Rect => [FieldNames.Left, FieldNames.Top, FieldNames.Width, FieldNames.Height],
        _ => [],
    };

    private static bool Contains(string text, string word)
        => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
