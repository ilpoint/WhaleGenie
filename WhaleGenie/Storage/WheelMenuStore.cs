using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Models;

namespace WhaleGenie.Storage;

/// <summary>Everything the wheel remembers, kept in a file of its own.</summary>
public sealed class WheelMenuState
{
    public WheelOptions Options { get; set; } = new();

    public WheelMenuRules Rules { get; set; } = new();
}

/// <summary>
/// The wheel reads and writes its own file rather than settings.json or the macro package: neither
/// the machine's other choices nor a project's format version has to change for a wheel to exist.
/// A missing or unreadable file means an empty wheel with the default options, never an error.
/// </summary>
public static class WheelMenuStore
{
    /// <summary>Lets a check point the store at a file of its own instead of the person's.</summary>
    internal static string? FilePathOverride { get; set; }

    private static string FilePath => FilePathOverride
        ?? Path.Combine(AppPaths.State, "wheel-menu.json");

    public static event Action? Changed;

    public static WheelMenuState Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new WheelMenuState();
            }

            if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject node)
            {
                return new WheelMenuState();
            }

            var options = node["options"]?.Deserialize<WheelOptions>() ?? new WheelOptions();
            return new WheelMenuState
            {
                Options = options.Clamped(),
                Rules = WheelMenuRules.FromJson(node["rules"] as JsonObject),
            };
        }
        catch
        {
            return new WheelMenuState();
        }
    }

    public static void Save(WheelMenuState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var node = new JsonObject
            {
                ["options"] = JsonSerializer.SerializeToNode(state.Options.Clamped()),
                ["rules"] = state.Rules.ToJson(),
            };
            File.WriteAllText(FilePath, node.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
            Changed?.Invoke();
        }
        catch
        {
            // A file that cannot be written must not stop a wheel from opening.
        }
    }
}
