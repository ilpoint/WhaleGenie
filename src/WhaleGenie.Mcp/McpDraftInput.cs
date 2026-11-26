using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>What a client's macro JSON says, parsed before a draft or a lock is involved.</summary>
internal sealed record McpDraftPutInput(MacroItem Macro, IReadOnlyList<JsonObject>? Triggers);

/// <summary>
/// The client JSON readers of a draft. They return model objects and do not touch a draft, a lock,
/// the clock or the project; the host still decides whether the answer may be written and audits it.
/// Missing trigger and step ids are left to the host, which is the only place an id is issued.
/// </summary>
internal static class McpDraftInput
{
    public static McpDraftPutInput ReadPut(JsonObject json)
    {
        var macro = MacroItem.FromJson(json);
        macro.Name = macro.Name.Trim();
        if (macro.Name.Length == 0)
        {
            throw new McpFailure(McpErrors.BadArgument, "The macro has no name.",
                "Every macro needs a name: it is how apply, get_macro and the trigger list find it.");
        }

        return new McpDraftPutInput(macro, Triggers(json, macro.Name));
    }

    public static IReadOnlyList<VariableDefinition> ReadGlobals(JsonArray array)
    {
        var globals = new List<VariableDefinition>();
        foreach (var item in array)
        {
            if (item is not JsonObject variable)
            {
                throw new McpFailure(McpErrors.BadArgument, "Every global has to be an object.",
                    "A declaration is {name, type, default, description, persistent}.");
            }

            var name = Text(variable["name"]).Trim();
            if (name.Length == 0)
            {
                throw new McpFailure(McpErrors.BadArgument, "A global has no name.",
                    "Every declaration needs a name; it is the word a macro reads as a name.");
            }

            globals.Add(new VariableDefinition
            {
                Name = name,
                Scope = VariableScope.Global,
                Type = VariableTypes.KindOf(Default(Text(variable["type"]), "text")),
                Default = Formula.Text(Text(variable["default"])),
                Description = Text(variable["description"]),
                PersistValue = variable["persistent"] is JsonValue flag
                    && flag.TryGetValue<bool>(out var persistent) && persistent,
            });
        }

        return globals;
    }

    /// <summary>
    /// The trigger nodes a client sent, or null when it sent none. The macro name is filled in and
    /// ids are left alone: issuing one is the host's job, as is replacing the project's trigger list.
    /// </summary>
    private static List<JsonObject>? Triggers(JsonObject json, string macroName)
    {
        if (json["triggers"] is not JsonArray array)
        {
            return null;
        }

        var entries = new List<JsonObject>();
        foreach (var item in array.OfType<JsonObject>())
        {
            var node = (JsonObject)item.DeepClone();
            node["macro"] = macroName;
            entries.Add(node);
        }

        return entries;
    }

    private static string Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private static string Default(string text, string fallback) => text.Length > 0 ? text : fallback;
}
