using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Reading and writing a JSON file: the path a step writes, and where a value sits along it. Both
/// directions read the path the same way, so anything a macro can read it can also write.
/// </summary>
internal static class FileJsonActions
{
    private static readonly JsonSerializerOptions Writable = new() { WriteIndented = true };

    /// <summary>
    /// The JSON actions, each answered by the method written for it. A step of another family is
    /// not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["file.readJson"] = (run, step, depth, _) =>
            {
                ReadJson(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.writeJson"] = (run, step, depth, _) =>
            {
                WriteJson(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },
        };

    // ------------------------------------------------------------------ JSON

    private static void ReadJson(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var document = Json(path, run.Devices.Files.ReadText(path, FileActions.EncodingOf(step)));
        var query = run.Args(step).ValueOf("query").AsText().Trim();
        var node = query.Length == 0
            ? document
            : Select(document, query) ?? throw new StepFailure(RunKeys.BadJsonPath, query);

        var value = FromJson(node);
        run.Store(step, "value", value, depth);
    }

    private static void WriteJson(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var query = run.Args(step).ValueOf("query").AsText().Trim();
        if (query.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingJsonPath);
        }

        var document = run.Devices.Files.Exists(path)
            ? Json(path, run.Devices.Files.ReadText(path, FileActions.EncodingOf(step)))
            : new JsonObject();

        Assign(document, query, ToJson(run.Args(step).ValueOf("writeValue")));

        var text = document.ToJsonString(Writable);
        run.Devices.Files.WriteText(path, text, false, FileActions.EncodingOf(step));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.WroteFile, path, text.Length);
    }

    /// <summary>Reads a JSON file, reporting a broken one as a failed step.</summary>
    internal static JsonNode Json(string path, string text)
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

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var position)
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
