using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;


namespace WhaleGenie.Mcp;

// Filling a declaration and reading the types and record shapes it accepts.
internal static partial class McpPatches
{
    /// <summary>Puts one declaration at another place in the list, which is the order a call form uses.</summary>
    private static Refusal? Move<T>(List<T> declarations, int found, string name, JsonObject operation,
        string kind, List<Touch> touches)
    {
        if (found < 0)
        {
            return Refused(McpErrors.BadArgument, $"There is no {kind} named \"{name}\".",
                "Read the declarations with whalegenie_get_macro_signature.", true, new { name });
        }

        var asked = Number(operation, "index");
        if (asked is not { } at || at < 0 || at >= declarations.Count)
        {
            return Refused(McpErrors.BadArgument, "move needs an index inside the list.",
                $"index is 0 to {declarations.Count - 1}; it is where the declaration lands.",
                true, new { name, count = declarations.Count });
        }

        var moved = declarations[found];
        declarations.RemoveAt(found);
        declarations.Insert(at, moved);
        touches.Add(new Touch("declare", null, $"{kind}:{name}"));
        return null;
    }

    /// <summary>What a declaration can say, whichever kind of declaration it is.</summary>
    private static Refusal? Fill(MacroParameter argument, JsonObject operation)
    {
        if (Said(operation, "default") is { } fallback)
        {
            argument.Default = fallback;
        }

        if (Flag(operation, "required") is { } required)
        {
            argument.Required = required;
        }

        if (Said(operation, "description") is { } description)
        {
            argument.Description = description;
        }

        if (Typed(operation) is { } type && type.Refusal is null)
        {
            argument.Type = type.Value;
        }

        if (Reading(operation) is { } fields)
        {
            if (fields.Refusal is { } refusal)
            {
                return refusal;
            }

            argument.Fields = fields.Value ?? [];
        }

        // A value that is required and has a default is two answers to one question: the call must
        // give it, and the call may leave it out.
        return argument.Required && argument.Default.Length > 0
            ? Refused(McpErrors.BadArgument,
                $"\"{argument.Name}\" cannot be both required and defaulted.",
                "Drop required, or drop default.", true, new { name = argument.Name })
            : null;
    }

    /// <summary>The same, for a result, which has no such thing as required.</summary>
    private static Refusal? Fill(MacroResult result, JsonObject operation)
    {
        if (Said(operation, "description") is { } description)
        {
            result.Description = description;
        }

        if (Typed(operation) is { } type && type.Refusal is null)
        {
            result.Type = type.Value;
        }

        if (Reading(operation) is { } fields)
        {
            if (fields.Refusal is { } refusal)
            {
                return refusal;
            }

            result.Fields = fields.Value ?? [];
        }

        return null;
    }

    /// <summary>The declared type of a declaration, or null when the operation did not name one.</summary>
    private static (ValueKind Value, Refusal? Refusal)? Typed(JsonObject operation)
        => Enum<ValueKind>(ValueKind.Text, operation, "type");

    /// <summary>The fields of a record declaration, or null when the operation did not name any.</summary>
    private static (List<MacroField>? Value, Refusal? Refusal)? Reading(JsonObject operation)
    {
        if (operation["fields"] is not JsonArray fields)
        {
            return null;
        }

        var read = new List<MacroField>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in fields)
        {
            if (entry is not JsonObject field)
            {
                return (null, Refused(McpErrors.BadArgument, "A field has to be an object.",
                    "A field is {name, type}.", true, null));
            }

            var name = Text(field["name"]).Trim();
            if (name.Length == 0 || !seen.Add(name))
            {
                return (null, Refused(McpErrors.BadArgument,
                    $"\"{name}\" is empty or appears twice in the field list.",
                    "Every field of a record needs a name of its own.", true, new { name }));
            }

            var type = System.Enum.TryParse<ValueKind>(Text(field["type"]).Trim(),
                ignoreCase: true, out var parsed)
                ? parsed
                : ValueKind.Text;
            read.Add(new MacroField { Name = name, Type = type });
        }

        return (read, null);
    }

    /// <summary>A string field as written, or null when the operation did not name it.</summary>
    private static string? Said(JsonObject operation, string field)
        => operation.TryGetPropertyValue(field, out var node) && node is not null
            ? Text(node)
            : null;

    /// <summary>A flag as written, or null when the operation did not name it.</summary>
    private static bool? Flag(JsonObject operation, string field)
    {
        if (!operation.TryGetPropertyValue(field, out var node) || node is null)
        {
            return null;
        }

        return bool.TryParse(Text(node).Trim(), out var flag) ? flag : null;
    }

    /// <summary>A whole number as written, or null when the operation did not name it.</summary>
    private static int? Number(JsonObject operation, string field)
    {
        if (!operation.TryGetPropertyValue(field, out var node) || node is null)
        {
            return null;
        }

        return int.TryParse(Text(node).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var number)
            ? number
            : null;
    }

    /// <summary>One of a closed set of words, or null when the operation did not name it.</summary>
    private static T? Picked<T>(JsonObject operation, string field) where T : struct, Enum
        => Enum<T>(default, operation, field)?.Value;

    /// <summary>One of an enum's words, read without regard to case, with the words named on refusal.</summary>
    private static (T Value, Refusal? Refusal)? Enum<T>(T current, JsonObject operation, string field)
        where T : struct, Enum
    {
        if (!operation.TryGetPropertyValue(field, out var node) || node is null)
        {
            return null;
        }

        var written = Text(node).Trim();
        if (System.Enum.TryParse<T>(written, ignoreCase: true, out var value))
        {
            return (value, null);
        }

        var words = string.Join(", ", System.Enum.GetNames<T>().Select(name => char.ToLowerInvariant(name[0]) + name[1..]));
        return (current, Refused(McpErrors.BadArgument, $"\"{written}\" is not a {field}.",
            $"One of: {words}.", true, new { field, value = written }));
    }
}
