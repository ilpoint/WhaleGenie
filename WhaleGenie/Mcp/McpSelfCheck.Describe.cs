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


namespace WhaleGenie.Mcp;

// What the server says it can do: the tools it lists, their schemas and what a client reads from them.
internal sealed partial class McpSelfCheck
{
    private async Task DescribeAsync(CancellationToken token)
    {
        var instructions = client.ServerInstructions;

        // Every word the client is given, gathered for one question: does it name tools that are
        // really there? A description that promises a tool nobody registered is how an AI ends up
        // calling something that does not exist.
        var said = new List<string>();
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            said.Add(instructions);
        }

        Add("server.instructions",
            string.IsNullOrWhiteSpace(instructions) ? McpCheckResult.Missing : McpCheckResult.Pass,
            string.IsNullOrWhiteSpace(instructions)
                ? "the server sends no instructions"
                : $"{instructions.Length} characters");

        var tools = await client.ListToolsAsync(cancellationToken: token).ConfigureAwait(false);
        _tools = new HashSet<string>(tools.Select(tool => tool.Name), StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            _parameters[tool.Name] = ParameterNames(tool);
        }

        _report.Metrics["tools.count"] = tools.Count;
        _report.Metrics["tools.withTitle"] =
            tools.Count(tool => !string.IsNullOrWhiteSpace(tool.ProtocolTool.Title));

        var absent = RequiredTools.Where(name => !_tools.Contains(name)).ToArray();
        Add("tools.list", absent.Length == 0 ? McpCheckResult.Pass : McpCheckResult.Fail,
            absent.Length == 0
                ? $"{tools.Count} tools, every required one present"
                : $"missing {string.Join(", ", absent)}");

        // A tool this build knows and the server does not is a difference worth reporting, not a
        // pass with a number beside it: a client that needs it would find out by being refused.
        var absentOptional = OptionalTools.Where(name => !_tools.Contains(name)).ToArray();
        _report.Metrics["tools.absentOptional"] = absentOptional.Length;
        Add("tools.optional", absentOptional.Length == 0 ? McpCheckResult.Pass : McpCheckResult.Missing,
            absentOptional.Length == 0
                ? $"every one of {OptionalTools.Length} optional tools present"
                : $"absent: {string.Join(", ", absentOptional)}");

        MeasureSchema(tools);

        var prompts = await client.ListPromptsAsync(cancellationToken: token).ConfigureAwait(false);
        _report.Metrics["prompts.count"] = prompts.Count;
        var names = new HashSet<string>(prompts.Select(prompt => prompt.Name), StringComparer.Ordinal);
        var wanted = new[] { "whalegenie_write_macro", "whalegenie_debug_macro" };
        var absentPrompts = wanted.Where(name => !names.Contains(name)).ToArray();
        Add("prompts.list", absentPrompts.Length == 0 ? McpCheckResult.Pass : McpCheckResult.Missing,
            absentPrompts.Length == 0
                ? $"{prompts.Count} prompts"
                : $"missing {string.Join(", ", absentPrompts)}");
        if (absentPrompts.Length > 0)
        {
            return;
        }

        var thin = new List<string>();
        foreach (var name in wanted)
        {
            var prompt = await client.GetPromptAsync(name, new Dictionary<string, object?>(),
                cancellationToken: token).ConfigureAwait(false);
            var text = string.Concat(prompt.Messages
                .Select(message => message.Content as TextContentBlock)
                .Where(block => block is not null)
                .Select(block => block!.Text));
            said.Add(text);
            if (text.Length < 200 || !text.Contains("whalegenie_", StringComparison.Ordinal))
            {
                thin.Add($"{name} ({text.Length} characters)");
            }
        }

        Add("prompts.text", thin.Count == 0 ? McpCheckResult.Pass : McpCheckResult.Missing,
            thin.Count == 0 ? "both prompts carry the workflow" : string.Join("; ", thin));

        var named = NamedTools(said);
        var invented = named.Where(name => !_tools.Contains(name)).ToArray();
        Add("docs.tool_names", invented.Length == 0 ? McpCheckResult.Pass : McpCheckResult.Fail,
            invented.Length == 0
                ? $"{named.Count} tool names, every one registered"
                : $"named but not registered: {string.Join(", ", invented)}");
    }

    /// <summary>
    /// Every tool name a piece of prose mentions. Names are read by the same shape the tools have,
    /// so a resource address such as <c>whalegenie://project</c> is not mistaken for one.
    /// </summary>
    internal static IReadOnlyList<string> NamedTools(IEnumerable<string> prose)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var text in prose)
        {
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(text, "whalegenie_[a-z0-9_]+"))
            {
                found.Add(match.Value);
            }
        }

        return [.. found];
    }

    private void MeasureSchema(IEnumerable<McpClientTool> tools)
    {
        var declared = 0;
        var withEnum = 0;
        var stringsWithoutEnum = 0;
        var jsonInString = 0;
        var described = 0;
        foreach (var tool in tools)
        {
            var schema = tool.JsonSchema;
            if (schema.ValueKind != JsonValueKind.Object
                || !schema.TryGetProperty("properties", out var properties)
                || properties.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            described++;
            foreach (var property in properties.EnumerateObject())
            {
                declared++;
                var field = property.Value;
                var enumerated = field.ValueKind == JsonValueKind.Object
                    && field.TryGetProperty("enum", out var values)
                    && values.ValueKind == JsonValueKind.Array;
                if (enumerated)
                {
                    withEnum++;
                }

                if (field.ValueKind != JsonValueKind.Object
                    || !field.TryGetProperty("type", out var type)
                    || type.ValueKind != JsonValueKind.String
                    || type.GetString() != "string")
                {
                    continue;
                }

                if (!enumerated)
                {
                    stringsWithoutEnum++;
                }

                if (LooksLikeJson(property.Name))
                {
                    jsonInString++;
                }
            }
        }

        _report.Metrics["schema.parameters"] = declared;
        _report.Metrics["schema.enumParameters"] = withEnum;
        _report.Metrics["schema.enumRatio"] = declared == 0 ? 0 : (double)withEnum / declared;
        _report.Metrics["schema.stringWithoutEnum"] = stringsWithoutEnum;
        _report.Metrics["schema.jsonInString"] = jsonInString;
        _report.Metrics["tools.withSchema"] = described;
    }
}
