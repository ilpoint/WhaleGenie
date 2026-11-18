using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Read-only projection of a JSON value: one node per value, its children underneath it. It is how
/// nested data is shown — what a step worked out that is made of named fields, one line each.
/// </summary>
public class JsonTreeNode
{
    public required string Name { get; init; }

    /// <summary>
    /// What the value is on its own line: a scalar as a person reads it, how many things are inside
    /// a record or a list, and "null" for nothing at all.
    /// </summary>
    public string Preview { get; init; } = string.Empty;

    public IReadOnlyList<JsonTreeNode> Children { get; init; } = [];

    /// <summary>
    /// The fields a value is made of, one node each: the same tree without the wrapper node for the
    /// value itself, which is what a panel that already says the value's name wants.
    /// </summary>
    public static IReadOnlyList<JsonTreeNode> Fields(JsonNode? root) => root switch
    {
        JsonObject source => [.. source.Select(property => Describe(property.Key, property.Value))],
        JsonArray source => [.. source.Select((item, index) => Describe($"[{index}]", item))],
        _ => [],
    };

    private static JsonTreeNode Describe(string name, JsonNode? node) => node switch
    {
        null => new JsonTreeNode { Name = name, Preview = "null" },
        JsonObject source => new JsonTreeNode
        {
            Name = name,
            // A record is written with how many fields are inside it: a node that has children can
            // then be read on the one line it takes up, with the fields underneath saying the rest.
            Preview = source.Count == 0 ? "{ }" : $"{{{source.Count}}}",
            Children = source.Select(property => Describe(property.Key, property.Value)).ToList(),
        },
        JsonArray source => new JsonTreeNode
        {
            Name = name,
            Preview = $"[{source.Count}]",
            Children = source.Select((item, index) => Describe($"[{index}]", item)).ToList(),
        },
        JsonValue value => new JsonTreeNode { Name = name, Preview = Render(value) },
        _ => new JsonTreeNode { Name = name, Preview = node.ToJsonString() },
    };

    /// <summary>Renders a scalar without the surrounding quotes JSON would add.</summary>
    private static string Render(JsonValue value)
        => value.TryGetValue(out string? text) ? text : value.ToJsonString();
}
