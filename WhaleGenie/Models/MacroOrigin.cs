using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Models;

/// <summary>
/// Where a copied submacro came from. The version is the one that was copied, not one to follow:
/// changing the public macro afterwards must not change an existing project.
/// </summary>
public sealed record MacroOrigin(string Id, string Name, int Version)
{
    public JsonObject ToJson() => new()
    {
        [FieldNames.Id] = Id,
        [FieldNames.Name] = Name,
        [FieldNames.Version] = Version,
    };

    public static MacroOrigin? FromJson(JsonObject? node)
    {
        var id = node?[FieldNames.Id]?.GetValue<string>()?.Trim() ?? string.Empty;
        var name = node?[FieldNames.Name]?.GetValue<string>()?.Trim() ?? string.Empty;
        var version = node?[FieldNames.Version] is JsonValue value
            && value.TryGetValue<int>(out var number)
            ? number
            : 0;

        return id.Length == 0 ? null : new MacroOrigin(id, name, version);
    }
}
