using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Viktor.ViewModels;

/// <summary>Read-only projection of a JSON node, used to draw the macro's JSON tree.</summary>
public class JsonTreeNode
{
    public required string Name { get; init; }

    /// <summary>Scalar value, element count, or empty for an object with children.</summary>
    public string Preview { get; init; } = string.Empty;

    public IReadOnlyList<JsonTreeNode> Children { get; init; } = [];

    /// <summary>Builds the tree shown in the editor, one node per JSON value.</summary>
    public static IReadOnlyList<JsonTreeNode> Build(JsonNode? root, string name = "macro")
        => root is null ? [] : [Describe(name, root)];

    private static JsonTreeNode Describe(string name, JsonNode? node) => node switch
    {
        null => new JsonTreeNode { Name = name, Preview = "null" },
        JsonObject source => new JsonTreeNode
        {
            Name = name,
            Preview = source.Count == 0 ? "{ }" : string.Empty,
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
