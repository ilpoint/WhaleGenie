using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;


namespace WhaleGenie.Core.Expressions;

// JSON held in a value: reading it, walking a path, listing keys, and setting a field.
internal static partial class FunctionLibrary
{

    // -------------------------------------------------------------------- json

    /// <summary>Reads JSON text, refusing text that is not JSON so a typo shows up as a failure.</summary>
    private static JsonNode? ParseJson(string text)
    {
        if (text.Trim().Length == 0)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw ExpressionException.TypeMismatch($"\"{text}\" is not JSON");
        }
    }

    /// <summary>The same read for the writer, which treats text that is not JSON as a fresh document.</summary>
    private static JsonNode? LooseJson(string text)
    {
        try
        {
            return ParseJson(text);
        }
        catch (ExpressionException)
        {
            return null;
        }
    }

    private static Value JsonGet(IReadOnlyList<Func<Value>> args)
    {
        var (found, node) = Located(args);
        return found ? JsonValues.FromJson(node) : Value.Null;
    }

    private static (bool Found, JsonNode? Node) Located(IReadOnlyList<Func<Value>> args)
    {
        var current = ParseJson(S(args, 0));
        if (current is null)
        {
            return (false, null);
        }

        foreach (var step in Path(S(args, 1)))
        {
            switch (step)
            {
                case int position when current is JsonArray array && position >= 0 && position < array.Count:
                    current = array[position];
                    break;
                case string name when current is JsonObject obj && obj.TryGetPropertyValue(name, out var child):
                    current = child;
                    break;
                default:
                    return (false, null);
            }
        }

        return (true, current);
    }

    private static IEnumerable<Value> JsonKeys(string text)
    {
        if (ParseJson(text) is not JsonObject obj)
        {
            return [];
        }

        return obj.Select(pair => Value.FromText(pair.Key));
    }

    /// <summary>
    /// Names and values in turn, made into one object and kept in the order they were written.
    /// A name with no value after it is a mistake worth saying out loud, because joining the two
    /// halves up wrongly would otherwise quietly produce a document with the wrong fields in it.
    /// </summary>
    private static string JsonOf(IReadOnlyList<Func<Value>> args)
    {
        if (args.Count % 2 != 0)
        {
            throw ExpressionException.ArgumentCount(
                $"jsonOf({args.Count} arguments) - names and values have to come in pairs");
        }

        var made = new JsonObject();
        for (var index = 0; index < args.Count; index += 2)
        {
            var name = S(args, index).Trim();
            if (name.Length == 0)
            {
                throw ExpressionException.TypeMismatch("a field of an object has to be named");
            }

            made[name] = JsonValues.Node(args[index + 1]());
        }

        return made.ToJsonString();
    }

    private static string JsonSet(IReadOnlyList<Func<Value>> args)
    {
        var steps = Path(S(args, 1));
        var value = JsonValues.Node(V(args, 2));
        if (steps.Count == 0)
        {
            // Setting the document itself replaces it, so a macro can start from nothing.
            return value?.ToJsonString() ?? "null";
        }

        JsonNode root = LooseJson(S(args, 0)) ?? Empty(steps[0]);
        if (root is not JsonObject && root is not JsonArray)
        {
            root = Empty(steps[0]);
        }

        var current = root;
        for (var index = 0; index < steps.Count - 1; index++)
        {
            current = Step(current, steps[index], steps[index + 1]);
        }

        Put(current, steps[^1], value);
        return root.ToJsonString();
    }

    /// <summary>A fresh container: a list when the path steps into a position, an object otherwise.</summary>
    private static JsonNode Empty(object step) => step is int ? new JsonArray() : new JsonObject();

    /// <summary>
    /// The part of the document at a path step, made on the way when the path names a field
    /// that is not there yet.
    /// </summary>
    private static JsonNode Step(JsonNode current, object step, object next)
    {
        if (step is int position)
        {
            var array = AsArray(current, position);
            while (array.Count <= position)
            {
                array.Add(null);
            }

            if (array[position] is { } made)
            {
                return made;
            }

            var created = Empty(next);
            array[position] = created;
            return created;
        }

        if (current is not JsonObject obj)
        {
            throw ExpressionException.TypeMismatch(
                $"the path names \"{step}\" but that part is not an object");
        }

        var name = (string)step;
        if (obj.TryGetPropertyValue(name, out var child) && child is not null)
        {
            return child;
        }

        var container = Empty(next);
        obj[name] = container;
        return container;
    }

    private static void Put(JsonNode current, object step, JsonNode? value)
    {
        if (step is int position)
        {
            var array = AsArray(current, position);
            while (array.Count <= position)
            {
                array.Add(null);
            }

            array[position] = value;
            return;
        }

        if (current is not JsonObject obj)
        {
            throw ExpressionException.TypeMismatch(
                $"the path names \"{step}\" but that part is not an object");
        }

        obj[(string)step] = value;
    }

    private static JsonArray AsArray(JsonNode current, int position)
        => current as JsonArray
           ?? throw ExpressionException.TypeMismatch(
               $"the path steps into [{position}] but that part is not a list");

    /// <summary>
    /// Reads a path such as <c>a.b[0].c</c> or <c>$.a.b</c> into the field names and the
    /// positions it walks through, in the order they are walked.
    /// </summary>
    private static List<object> Path(string text)
    {
        var parts = new List<object>();
        var trimmed = text.Trim();
        if (trimmed.StartsWith("$.", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed.StartsWith('$'))
        {
            trimmed = trimmed[1..];
        }

        var name = new StringBuilder();
        for (var index = 0; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            if (character == '.')
            {
                TakeName();
                continue;
            }

            if (character != '[')
            {
                name.Append(character);
                continue;
            }

            TakeName();
            var close = trimmed.IndexOf(']', index);
            if (close < 0)
            {
                throw ExpressionException.TypeMismatch($"the path \"{text}\" has an unclosed [");
            }

            var inside = trimmed[(index + 1)..close].Trim().Trim('\'', '"');
            if (inside.Length > 0)
            {
                parts.Add(int.TryParse(inside, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var position) ? position : inside);
            }

            index = close;
        }

        TakeName();
        return parts;

        void TakeName()
        {
            if (name.Length > 0)
            {
                parts.Add(name.ToString());
                name.Clear();
            }
        }
    }
}
