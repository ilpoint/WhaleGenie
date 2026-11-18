using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WhaleGenie.Mcp;

/// <summary>
/// A tool that answers a wrongly shaped argument with a coded refusal before the tool itself is
/// called. The schema stays as strict as it is — a client reading tools/list still sees
/// <c>"type": "object"</c> — but a client that sends text where the object belongs used to get
/// "An error occurred invoking 'whalegenie_put_macro'", which says nothing about the argument, the
/// shape, or what to send instead. This runs first and says all three.
/// </summary>
internal sealed class McpTolerantTool(McpServerTool inner) : McpServerTool
{
    public override Tool ProtocolTool => inner.ProtocolTool;

    public override IReadOnlyList<object> Metadata => inner.Metadata;

    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        => Refuse(inner.ProtocolTool, request.Params?.Arguments) is { } refusal
            ? new ValueTask<CallToolResult>(refusal)
            : inner.InvokeAsync(request, cancellationToken);

    /// <summary>
    /// What is wrong with these arguments, or nothing when they are shaped the way the schema says.
    /// Only containers are checked here: a string, a number or a flag that is meant to be one is
    /// read by the tool itself, which is where the value's meaning lives.
    /// </summary>
    private static CallToolResult? Refuse(Tool tool, IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is not { Count: > 0 } given)
        {
            return null;
        }

        if (!tool.InputSchema.TryGetProperty("properties", out var properties))
        {
            return null;
        }

        foreach (var property in properties.EnumerateObject())
        {
            // A parameter that may be left out is written as an array of types ("type": ["string",
            // "null"]), and reading a string out of that throws — which would turn every such call
            // into the very failure this is here to prevent.
            if (!property.Value.TryGetProperty("type", out var declared)
                || declared.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var expected = declared.GetString() switch
            {
                "object" => JsonValueKind.Object,
                "array" => JsonValueKind.Array,
                _ => (JsonValueKind?)null,
            };
            if (expected is not { } wanted || !given.TryGetValue(property.Name, out var value))
            {
                continue;
            }

            if (value.ValueKind == wanted)
            {
                continue;
            }

            return Wrong(tool, property.Name,
                wanted == JsonValueKind.Object ? "an object" : "an array", value,
                wanted == JsonValueKind.Object
                    ? $"Send the object itself rather than text that holds it. "
                        + $"whalegenie_macro_template shows the shape {property.Name} wants."
                    : $"Send {property.Name} as a list: one item after another, in square brackets.");
        }

        return null;
    }

    private static CallToolResult Wrong(Tool tool, string argument, string wanted, JsonElement value,
        string hint)
        => McpErrors.Fail(McpErrors.BadJson,
            $"{tool.Name} takes {wanted} for {argument}, but it arrived as {Say(value.ValueKind)}.",
            hint,
            details: new
            {
                tool = tool.Name,
                argument,
                expected = wanted,
                arrived = Say(value.ValueKind),
                sample = Sample(value),
            });

    /// <summary>What arrived, in the words the message uses.</summary>
    private static string Say(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "a list",
        JsonValueKind.String => "text",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "true or false",
        JsonValueKind.Null => "nothing",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// The first few characters of what arrived, so a client can see which of its own strings this
    /// was without the whole macro being quoted back at it.
    /// </summary>
    private static string? Sample(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => Clip(value.GetString()),
            JsonValueKind.Array => $"[{value.GetArrayLength()} item(s)]",
            _ => null,
        };

    private static string? Clip(string? text)
        => text is null ? null : text.Length <= 80 ? text : text[..77] + "...";
}
