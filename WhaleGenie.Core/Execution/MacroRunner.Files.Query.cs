using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Walking into JSON: the path a step writes, and where a value sits along it.
public sealed partial class MacroRunner
{
    /// <summary>Reads a JSON file, reporting a broken one as a failed step.</summary>
    private static JsonNode Json(string path, string text)
    {
        try
        {
            return JsonNode.Parse(text) ?? new JsonObject();
        }
        catch (JsonException error)
        {
            throw new StepFailure(RunKeys.BadJson, $"{path}: {error.Message}");
        }
    }

    /// <summary>Walks a path such as <c>server.name</c> or <c>items[0].id</c>.</summary>
    private static JsonNode? Select(JsonNode? root, string query)
    {
        var node = root;
        foreach (var token in Tokens(query))
        {
            node = Within(node, token);
            if (node is null)
            {
                break;
            }
        }

        return node;
    }

    /// <summary>The node one step along a path, or nothing when there is no such place.</summary>
    private static JsonNode? Within(JsonNode? node, Part token) => node switch
    {
        JsonArray array when token.IsIndex && token.Index >= 0 && token.Index < array.Count
            => array[token.Index],
        JsonObject parent when !token.IsIndex
            => parent.TryGetPropertyValue(token.Name, out var child) ? child : null,
        _ => null,
    };

    /// <summary>One step of a path: a name, or a place in a list.</summary>
    private readonly record struct Part(string Name, int Index, bool IsIndex);

    /// <summary>
    /// A path split into its steps: names separated by dots, and a place in a list written between
    /// brackets — by number, counting from zero, or by name in quotes for a name that has a dot in
    /// it. Reading and writing share this, so a path that can be read can also be written.
    /// </summary>
    private static List<Part> Tokens(string query)
    {
        var tokens = new List<Part>();
        var text = query.Trim();
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '.')
            {
                index++;
                continue;
            }

            if (text[index] == '[')
            {
                var close = text.IndexOf(']', index);
                if (close < 0)
                {
                    throw new StepFailure(RunKeys.BadJsonPath, query);
                }

                tokens.Add(Token(text[(index + 1)..close].Trim().Trim('\'', '"'), query));
                index = close + 1;
                continue;
            }

            var end = index;
            while (end < text.Length && text[end] != '.' && text[end] != '[')
            {
                end++;
            }

            tokens.Add(Token(text[index..end], query));
            index = end;
        }

        return tokens;
    }

    /// <summary>One step, read from the words a path writes it with.</summary>
    private static Part Token(string written, string query)
    {
        var trimmed = written.Trim();
        if (trimmed.Length == 0)
        {
            throw new StepFailure(RunKeys.BadJsonPath, query);
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)
            ? new Part(string.Empty, position, true)
            : new Part(trimmed, 0, false);
    }

    /// <summary>
    /// Sets a value at a path, making the objects along the way. The path is read exactly the way
    /// <see cref="Select"/> reads one, so anything a macro can read it can also write: a path that
    /// names a place inside a list — the third item's price — is the everyday case, and refusing it
    /// would leave a file that can be read and not changed.
    /// </summary>
    private static void Assign(JsonNode root, string query, JsonNode? value)
    {
        var path = Tokens(query);
        if (path.Count == 0)
        {
            throw new StepFailure(RunKeys.BadJsonPath, query);
        }

        var node = root;
        for (var index = 0; index < path.Count - 1; index++)
        {
            node = Descend(node, path[index], query);
        }

        var last = path[^1];
        switch (node)
        {
            case JsonObject parent when !last.IsIndex:
                parent[last.Name] = value;
                return;
            case JsonArray list when last.IsIndex && last.Index >= 0 && last.Index < list.Count:
                list[last.Index] = value;
                return;
            default:
                throw new StepFailure(RunKeys.BadJsonPath, query);
        }
    }

    /// <summary>
    /// One step along a path, on the way to the place being written. An object that is not there
    /// yet is made, because "set this value" is how a macro writes a setting a file does not have;
    /// a place in a list is never made, because the place only means something inside a list that
    /// is already there and as long as it is.
    /// </summary>
    private static JsonNode Descend(JsonNode node, Part token, string query) => node switch
    {
        JsonObject parent when !token.IsIndex => parent[token.Name] is { } child
            ? child
            : parent[token.Name] = new JsonObject(),
        JsonArray list when token.IsIndex && token.Index >= 0 && token.Index < list.Count
            => list[token.Index] ?? throw new StepFailure(RunKeys.BadJsonPath, query),
        _ => throw new StepFailure(RunKeys.BadJsonPath, query),
    };

    // A document and the values a macro works with are the same shape, and the one place that
    // knows how the two are written as each other is JsonValues, so a record read out of a file is
    // the record that went into it.
    private static JsonNode? ToJson(Value value) => JsonValues.Node(value);

    private static Value FromJson(JsonNode? node) => JsonValues.FromJson(node);
}
