using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace WhaleGenie.Core.Expressions;

/// <summary>
/// The one place a value and a piece of JSON are turned into each other. Reading a document, writing
/// one and putting a record into a file all go through it, so a record read back from a file is the
/// same record that went in.
/// </summary>
public static class JsonValues
{
    /// <summary>A value written as JSON, so it can go into a document or into a file.</summary>
    public static JsonNode? Node(Value value)
    {
        switch (value.Kind)
        {
            case ValueKind.Null:
                return null;
            case ValueKind.Number:
                return JsonValue.Create(value.Number);
            case ValueKind.Bool:
                return JsonValue.Create(value.Flag);
            case ValueKind.Text:
                return JsonValue.Create(value.Text);
            case ValueKind.Date:
                // JSON has no date. The one field an object must have to be read back as a moment
                // is $date, so a saved date returns as a date instead of as the line it was written
                // as — which is what a persistent global needs to survive a restart. What is written
                // carries its offset: a moment is an instant, and a stamp without one is read at the
                // offset of whichever machine opens the file, which moves it by the difference.
                return new JsonObject
                {
                    ["$date"] = value.Date.ToString("o", CultureInfo.InvariantCulture),
                };
            case ValueKind.Image:
                // The handle, not the pixels: a picture belongs to the run that took it, and what
                // is written down is how to name it.
                return new JsonObject { ["$image"] = value.AsText() };
            case ValueKind.Record:
                {
                    var made = new JsonObject();
                    foreach (var (name, field) in value.Fields)
                    {
                        made[name] = Node(field);
                    }

                    return made;
                }
            default:
                {
                    var array = new JsonArray();
                    foreach (var item in value.Items)
                    {
                        array.Add(Node(item));
                    }

                    return array;
                }
        }
    }

    /// <summary>A piece of JSON as one of the engine's values; an object comes back as a record.</summary>
    public static Value FromJson(JsonNode? node) => node switch
    {
        null => Value.Null,
        JsonArray array => Value.FromList(array.Select(FromJson)),
        JsonObject stamped when Moment(stamped) is { } moment => Value.FromDate(moment),
        JsonObject picture when Handle(picture) is { } handle => Value.FromImage(handle),
        JsonObject obj => Value.FromRecord(obj.Select(pair => (pair.Key, FromJson(pair.Value)))),
        JsonValue value => Scalar(value),
        _ => Value.Null,
    };

    /// <summary>
    /// The moment an object stands for, or none. Only an object holding exactly one <c>$date</c>
    /// is a moment: anything with more fields is a record that happens to have that name.
    /// </summary>
    private static DateTimeOffset? Moment(JsonObject node)
        => node.Count == 1
            && node["$date"] is JsonValue stamp
            && stamp.TryGetValue<string>(out var written)
            && DateTimeOffset.TryParse(written, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var moment)
                ? moment
                : null;

    /// <summary>The handle an object stands for, or none. Only a lone <c>$image</c> is a picture.</summary>
    private static string? Handle(JsonObject node)
        => node.Count == 1 && node["$image"] is JsonValue stamp
            && stamp.TryGetValue<string>(out var written)
                ? written
                : null;

    /// <summary>A value written out as the JSON text it is worth.</summary>
    public static string Written(Value value) => Node(value)?.ToJsonString() ?? "null";

    private static Value Scalar(JsonValue value)
    {
        if (value.TryGetValue<bool>(out var flag))
        {
            return Value.FromBool(flag);
        }

        if (value.TryGetValue<double>(out var number))
        {
            return Value.FromNumber(number);
        }

        return value.TryGetValue<string>(out var text)
            ? Value.FromText(text)
            : Value.FromText(value.ToJsonString());
    }
}
