using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Storage;

// Rewriting asset paths in a loaded project, and the small readers of a written object.
public static partial class MacroPackage
{
    /// <summary>
    /// Rewrites every <c>image</c> value through <paramref name="map"/>. Shared with the export,
    /// which writes a package of its own out of a few chosen macros.
    /// </summary>
    internal static void RewriteAssets(JsonNode? node, Func<string, string> map)
    {
        switch (node)
        {
            case JsonObject container:
                foreach (var name in container.Select(pair => pair.Key).ToList())
                {
                    var child = container[name];
                    if (name == ImageParameter && child is JsonValue value
                        && value.TryGetValue<string>(out var text) && text.Length > 0)
                    {
                        container[name] = map(text);
                    }
                    else
                    {
                        RewriteAssets(child, map);
                    }
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    RewriteAssets(child, map);
                }

                break;
        }
    }

    private static void RewriteMenuIcons(JsonNode? node, Func<string, string> map)
    {
        switch (node)
        {
            case JsonObject container:
                if (container["kind"]?.GetValue<string>() is { } kind
                    && string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase)
                    && container["value"] is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && text.Length > 0)
                {
                    container["value"] = map(text);
                    return;
                }

                foreach (var child in container.Select(pair => pair.Value))
                {
                    RewriteMenuIcons(child, map);
                }
                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    RewriteMenuIcons(child, map);
                }
                break;
        }
    }

    private static void ExpandMenuIcons(ProjectTriggers triggers, Func<string, string> map)
    {
        foreach (var entry in triggers.Entries)
        {
            if (entry.Spec is MenuSpec { Icon: { Kind: MenuIconKind.Image } icon })
            {
                entry.SetMenuIcon(icon with { Value = map(icon.Value) });
            }
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static JsonObject? ReadObject(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        if (entry is null)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return JsonNode.Parse(reader.ReadToEnd()) as JsonObject;
    }
}
