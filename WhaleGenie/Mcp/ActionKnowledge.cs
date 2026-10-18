using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>A compact action entry for the MCP list.</summary>
public sealed record McpActionSummary(
    string Key, string Name, string Category, string Description, bool Condition);

/// <summary>One parameter of an action, in the shape an external author needs to fill in.</summary>
public sealed record McpActionParameter(
    string Name, string Label, string Kind, bool Required, string Default, string Hint,
    string ReadMode, string Shape, IReadOnlyList<string> Options);

/// <summary>One result an action can hand back.</summary>
public sealed record McpActionReturn(string Key, string Label, string Type, bool Primary);

/// <summary>The full description of one action.</summary>
public sealed record McpActionDetails(
    string Key, string Name, string Category, string Description,
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
                    definition.IsCondition)),
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
                    [.. parameter.Options])),
            ],
            [
                .. definition.Returns.Select(result => new McpActionReturn(
                    result.Key, result.Label, result.Type.ToString(), result.Primary)),
            ]);

    private static bool Contains(string text, string word)
        => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
