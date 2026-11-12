using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;


namespace WhaleGenie.Mcp;

// Reading what a tool answered: fields, shapes, ids and the sentences a refusal carries.
internal sealed partial class McpSelfCheck
{
    private void Add(string name, McpCheckResult result, string detail)
        => _report.Checks.Add(new McpCheck(name, result, Clip(detail)));

    private bool Declares(string tool, string parameter)
        => _parameters.TryGetValue(tool, out var names) && names.Contains(parameter, StringComparer.Ordinal);

    private static string[] ParameterNames(McpClientTool tool)
    {
        var schema = tool.JsonSchema;
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return [.. properties.EnumerateObject().Select(property => property.Name)];
    }

    /// <summary>Whether a validation answer says there is a blocking problem, in whichever shape it
    /// carries it: today's counts, or the single flag of an older surface.</summary>
    private static bool SaysBroken(string text)
    {
        if (Parse(text) is not { } document || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        return document.RootElement.TryGetProperty("hasErrors", out var errors)
            && errors.ValueKind == JsonValueKind.True;
    }

    private static List<string> Problems(string text, params string[] names)
    {
        var found = new List<string>();
        if (Parse(text) is not { } document || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return found;
        }

        foreach (var name in names)
        {
            if (!document.RootElement.TryGetProperty(name, out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    // The step a problem came from is part of the answer: a message that names no
                    // row leaves a model hunting through a macro, so it is kept beside the words.
                    var step = item.TryGetProperty("stepId", out var id)
                        && id.ValueKind == JsonValueKind.String
                        ? id.GetString()
                        : null;
                    // The code comes first: it is the part that does not depend on the language the
                    // interface happens to be set to, so it is what a client can branch on.
                    var code = item.TryGetProperty("code", out var named)
                        && named.ValueKind == JsonValueKind.String
                        ? named.GetString()
                        : string.Empty;
                    var line = message.GetString() ?? string.Empty;
                    var said = code is { Length: > 0 } ? $"{code}: {line}" : line;
                    found.Add(step is { Length: > 0 } ? $"[{step}] {said}" : said);
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    found.Add(item.GetString() ?? string.Empty);
                }
                else
                {
                    found.Add(item.ToString());
                }
            }
        }

        return found;
    }

    private static JsonDocument? Parse(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Field(string text, string name)
    {
        if (Parse(text) is not { } document
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    /// <summary>What a list answer says about how much it left out.</summary>
    private static string Shape(Answer answer)
    {
        if (answer.Failed || Parse(answer.Text) is not { } document)
        {
            return "unavailable";
        }

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return "array";
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return "other";
        }

        var parts = new List<string>();
        foreach (var name in new[] { "total", "totalLines", "hasMore", "truncated" })
        {
            if (document.RootElement.TryGetProperty(name, out _))
            {
                parts.Add(name == "totalLines" ? "total" : name);
            }
        }

        return parts.Count == 0 ? "none" : string.Join(",", parts.Distinct(StringComparer.Ordinal));
    }

    /// <summary>True for a parameter whose value is a JSON document a client has to write as text.</summary>
    /// <summary>The step ids a write answer reports, in the order the macro holds them.</summary>
    private static List<string> Ids(string text)
    {
        var ids = new List<string>();
        if (Parse(text) is not { } document
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("outline", out var outline)
            || outline.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (var entry in outline.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: > 0 } written)
            {
                ids.Add(written);
            }
        }

        return ids;
    }

    /// <summary>How many steps a macro document holds, all lists together.</summary>
    private static int StepCount(string macroText)
        => Parse(macroText) is { } document
            && document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("steps", out var steps)
            && steps.ValueKind == JsonValueKind.Array
                ? Count(steps)
                : 0;

    private static int Count(JsonElement steps)
    {
        var total = 0;
        foreach (var step in steps.EnumerateArray())
        {
            total++;
            if (step.ValueKind != JsonValueKind.Object
                || !step.TryGetProperty("params", out var parameters)
                || parameters.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var parameter in parameters.EnumerateObject())
            {
                if (parameter.Value.ValueKind == JsonValueKind.Array
                    && parameter.Value.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("type", out _)))
                {
                    total += Count(parameter.Value);
                }
            }
        }

        return total;
    }

    /// <summary>One parameter of one step, as the macro document writes it.</summary>
    private static string? ValueAt(string macroText, string stepId, string parameter)
    {
        if (Parse(macroText) is not { } document
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("steps", out var steps))
        {
            return null;
        }

        return Search(steps);

        string? Search(JsonElement list)
        {
            foreach (var step in list.EnumerateArray())
            {
                if (step.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (step.TryGetProperty("id", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && string.Equals(id.GetString(), stepId, StringComparison.OrdinalIgnoreCase)
                    && step.TryGetProperty("params", out var parameters)
                    && parameters.ValueKind == JsonValueKind.Object
                    && parameters.TryGetProperty(parameter, out var value))
                {
                    return value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : value.ToString();
                }

                if (!step.TryGetProperty("params", out var nested)
                    || nested.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var child in nested.EnumerateObject())
                {
                    if (child.Value.ValueKind == JsonValueKind.Array
                        && Search(child.Value) is { } found)
                    {
                        return found;
                    }
                }
            }

            return null;
        }
    }

    private static bool LooksLikeJson(string name)
        => name == "macro" || name == "globals" || name.EndsWith("Json", StringComparison.Ordinal);

    private static bool Mentions(string text, string word)
        => word.Length > 0 && text.Contains(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether an answer was a refusal with this code.</summary>
    private static bool Refused(Answer answer, string code)
        => answer.Failed && answer.Text.Contains(code, StringComparison.Ordinal);

    private static string Describe(Exception error)
        => $"{error.GetType().Name}: {error.Message}";

    private static string Clip(string text)
        => text.Length <= 240 ? text : text[..240] + "…";

    private sealed record Answer(bool Failed, string Text);
}
