using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WhaleGenie.Mcp;

/// <summary>
/// Turns a declaration into the two halves of an audit line: what the call was about, from its
/// argument template, and how it ended. The line itself is written by <see cref="McpAudit"/>, which
/// is also what keeps tabs and line breaks out of it.
/// </summary>
internal static class McpAuditSummary
{
    /// <summary>
    /// The longest one substituted value may be. A name, a path or a key is what this is for; a
    /// macro body or a paragraph of typed text is not, and a line that carries one is a line nobody
    /// reads.
    /// </summary>
    private const int ValueLimit = 80;

    /// <summary>
    /// The template with each <c>{argument}</c> replaced by what that argument was. An argument the
    /// call left out writes nothing, so a template naming an optional one reads as one sentence
    /// either way rather than ending in a gap.
    /// </summary>
    public static string Render(string template, IDictionary<string, JsonElement>? arguments)
    {
        var text = new StringBuilder(template.Length + 32);
        for (var at = 0; at < template.Length; at++)
        {
            var letter = template[at];
            if (letter != '{')
            {
                text.Append(letter);
                continue;
            }

            var end = template.IndexOf('}', at + 1);
            if (end < 0)
            {
                text.Append(letter);
                continue;
            }

            text.Append(Say(arguments, template[(at + 1)..end].Trim()));
            at = end;
        }

        return Space(text.ToString());
    }

    /// <summary>How one call ended: "ok", or the code it was refused by.</summary>
    public static string Outcome(CallToolResult result)
        => result.IsError == true ? Code(result) : "ok";

    /// <summary>How a call ended when it stopped with an exception instead of a result.</summary>
    public static string Outcome(Exception error)
        => error is McpFailure failure ? failure.Code : error.GetType().Name;

    private static string Say(IDictionary<string, JsonElement>? arguments, string name)
        => arguments is not null && arguments.TryGetValue(name, out var value)
            ? Say(value)
            : string.Empty;

    /// <summary>
    /// One argument as a line may hold it. Text is the text, a number or a flag is what it says, and
    /// a list or an object is how many of what it holds: the size is the part a person reading the
    /// record needs, and the content is what makes a log too big to read.
    /// </summary>
    private static string Say(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => Clip(value.GetString() ?? string.Empty),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => Count(value.GetArrayLength(), "item"),
        JsonValueKind.Object => Count(Fields(value), "field"),
        _ => string.Empty,
    };

    private static int Fields(JsonElement value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateObject())
        {
            count++;
        }

        return count;
    }

    private static string Count(int count, string unit)
        => count == 1 ? $"1 {unit}" : $"{count} {unit}s";

    private static string Clip(string text)
        => text.Length <= ValueLimit ? text : text[..(ValueLimit - 3)] + "...";

    /// <summary>
    /// The refusal code inside a failed result. A refusal is the JSON document
    /// <see cref="McpErrors"/> builds, so its code is read from there rather than from the sentence,
    /// which is in whatever language the interface is set to.
    /// </summary>
    private static string Code(CallToolResult result)
    {
        foreach (var block in result.Content)
        {
            if (block is not TextContentBlock { Text: { Length: > 0 } answer })
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(answer);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("code", out var code)
                    && code.ValueKind == JsonValueKind.String)
                {
                    return code.GetString() ?? McpErrors.Internal;
                }
            }
            catch (JsonException)
            {
                // A failed result that is not the document above says nothing better than "failed".
            }

            break;
        }

        return "failed";
    }

    /// <summary>One space between words, none at either end: a template may leave a gap where an
    /// optional argument was.</summary>
    private static string Space(string text)
    {
        var kept = new StringBuilder(text.Length);
        var gap = false;
        foreach (var letter in text)
        {
            if (char.IsWhiteSpace(letter))
            {
                gap = kept.Length > 0;
                continue;
            }

            if (gap)
            {
                kept.Append(' ');
                gap = false;
            }

            kept.Append(letter);
        }

        return kept.ToString();
    }
}
