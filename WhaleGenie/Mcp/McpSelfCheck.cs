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

/// <summary>What a self-check run is allowed to do, and how long one tool call may take.</summary>
internal sealed class McpSelfCheckOptions
{
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Whether the run may write a draft into the project. Off for a server someone is
    /// using; on when the check owns a throwaway copy of a package.</summary>
    public bool AllowApply { get; init; }

    public bool AllowSave { get; init; }

    public string ScratchMacro { get; init; } = "MCP self check";

    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Asks a running MCP server the questions a first-time client would ask, and writes down what the
/// answers cost. It never throws and never asserts: a tool that fails becomes one line in the
/// report and the next question is still asked, because the report has to be printable on the run
/// that is half broken — that is the run that needs it.
///
/// It knows only <see cref="McpClient"/>. Who connected — a host started inside a check, a package
/// in another process — is none of its business, which is why one ruler can measure the code
/// before and after a change.
///
/// A field a newer surface does not send yet is read as absent rather than required: the ruler
/// tolerating a shape it has not met is not the product keeping a compatibility branch.
/// </summary>
internal sealed class McpSelfCheck(McpClient client, McpSelfCheckOptions? options = null)
{
    /// <summary>
    /// The macro the patch check writes a call to. It declares one required argument and one
    /// optional one, which is the smallest declaration a call can be written against.
    /// </summary>
    private const string Called = "Child (self-check)";

    private static readonly string[] RequiredTools =
    [
        "whalegenie_project_status", "whalegenie_open_project", "whalegenie_put_macro",
        "whalegenie_validate", "whalegenie_apply", "whalegenie_save_project",
        "whalegenie_list_actions", "whalegenie_get_action",
    ];

    private static readonly string[] OptionalTools =
    [
        "whalegenie_instructions", "whalegenie_macro_template", "whalegenie_get_actions",
        "whalegenie_list_macros", "whalegenie_get_macro", "whalegenie_delete_macro",
        "whalegenie_set_globals", "whalegenie_discard", "whalegenie_list_runs",
        "whalegenie_get_run_log", "whalegenie_get_last_failure_log", "whalegenie_capture_screen",
        "whalegenie_pixel_at", "whalegenie_cursor", "whalegenie_list_windows",
        "whalegenie_element_at", "whalegenie_mouse_move", "whalegenie_mouse_click",
        "whalegenie_mouse_scroll", "whalegenie_mouse_drag", "whalegenie_key",
        "whalegenie_type_text", "whalegenie_window_activate", "whalegenie_window_move",
        "whalegenie_window_close",
    ];

    private readonly McpSelfCheckOptions _options = options ?? new McpSelfCheckOptions();
    private readonly McpSelfCheckReport _report = new() { Endpoint = options?.Endpoint ?? string.Empty };
    private readonly Dictionary<string, string[]> _parameters = new(StringComparer.Ordinal);
    private readonly List<string> _drafts = [];
    private HashSet<string> _tools = new(StringComparer.Ordinal);

    public async Task<McpSelfCheckReport> RunAsync(CancellationToken token = default)
    {
        try
        {
            await DescribeAsync(token).ConfigureAwait(false);
            await LoopAsync(token).ConfigureAwait(false);
            await PatchAsync(token).ConfigureAwait(false);
            await RejectAsync(token).ConfigureAwait(false);
            await ResourcesAsync(token).ConfigureAwait(false);
            await GateAsync(token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            // A ruler that stops at the first surprise measures nothing; what it has collected up
            // to that point is still worth printing.
            Add("selfcheck.crash", McpCheckResult.Fail, Describe(error));
        }

        await DiscardAsync(token).ConfigureAwait(false);
        return _report;
    }

    private async Task DescribeAsync(CancellationToken token)
    {
        var instructions = client.ServerInstructions;
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

        var absentOptional = OptionalTools.Where(name => !_tools.Contains(name)).ToArray();
        _report.Metrics["tools.absentOptional"] = absentOptional.Length;
        Add("tools.optional", McpCheckResult.Pass, absentOptional.Length == 0
            ? "every optional tool present"
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
            if (text.Length < 200 || !text.Contains("whalegenie_", StringComparison.Ordinal))
            {
                thin.Add($"{name} ({text.Length} characters)");
            }
        }

        Add("prompts.text", thin.Count == 0 ? McpCheckResult.Pass : McpCheckResult.Missing,
            thin.Count == 0 ? "both prompts carry the workflow" : string.Join("; ", thin));
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

    private async Task LoopAsync(CancellationToken token)
    {
        var start = _report.Calls.Count;

        var status = await InvokeAsync("whalegenie_project_status", [], token).ConfigureAwait(false);
        Add("loop.status", status.Failed ? McpCheckResult.Fail : McpCheckResult.Pass,
            status.Failed
                ? status.Text
                : $"path={(Field(status.Text, "path") ?? "(none)")} revision={Field(status.Text, "revision")}");

        var opened = await InvokeAsync("whalegenie_open_project", [], token).ConfigureAwait(false);
        var draft = Field(opened.Text, "draftId");
        if (string.IsNullOrWhiteSpace(draft))
        {
            Add("loop.open", McpCheckResult.Fail, opened.Text);
            return;
        }

        Add("loop.open", McpCheckResult.Pass,
            $"draft {draft} at revision {Field(opened.Text, "revision")}");
        _drafts.Add(draft!);

        await DescribeKeysAsync(token).ConfigureAwait(false);

        await InvokeAsync("whalegenie_macro_template",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = _options.ScratchMacro },
            token).ConfigureAwait(false);

        var put = await InvokeAsync("whalegenie_put_macro", MacroArguments(draft!, Scenario()), token)
            .ConfigureAwait(false);
        var steps = Field(put.Text, "steps");
        Add("loop.put", put.Failed || string.IsNullOrWhiteSpace(steps)
                ? McpCheckResult.Fail
                : McpCheckResult.Pass,
            put.Failed ? put.Text : $"{steps} steps written");

        var validated = await InvokeAsync("whalegenie_validate",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
            .ConfigureAwait(false);
        var blocking = Problems(validated.Text, "blocking", "messages");
        var clean = !validated.Failed && !SaysBroken(validated.Text) && blocking.Count == 0;
        Add("loop.validate", clean ? McpCheckResult.Pass : McpCheckResult.Fail,
            clean
                ? "no blocking problem"
                : $"{blocking.Count} problem(s): {string.Join(" / ", blocking)}");

        if (!_options.AllowApply)
        {
            Add("loop.apply", McpCheckResult.Skip, "the run may not write the project");
            Add("loop.save", McpCheckResult.Skip, "the run may not write the project");
            MeasureCost(start);
            await MeasurePagingAsync(draft!, token).ConfigureAwait(false);
            return;
        }

        var applied = await InvokeAsync("whalegenie_apply",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
            .ConfigureAwait(false);
        Add("loop.apply", applied.Failed ? McpCheckResult.Fail : McpCheckResult.Pass,
            applied.Failed ? applied.Text : $"revision {Field(applied.Text, "revision")}");

        // A second apply of the same draft has to be refused: it would carry the same basis and
        // quietly write the same work a second time.
        var again = await InvokeAsync("whalegenie_apply",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
            .ConfigureAwait(false);
        Add("reject.double_apply", again.Failed ? McpCheckResult.Pass : McpCheckResult.Fail,
            again.Failed ? again.Text : "a second apply was accepted");

        if (!_options.AllowSave)
        {
            Add("loop.save", McpCheckResult.Skip, "the run may not write the package");
            MeasureCost(start);
            await MeasurePagingAsync(draft!, token).ConfigureAwait(false);
            return;
        }

        var saved = await InvokeAsync("whalegenie_save_project", [], token).ConfigureAwait(false);
        var path = Field(saved.Text, "path");
        Add("loop.save", saved.Failed || string.IsNullOrWhiteSpace(path)
                ? McpCheckResult.Fail
                : McpCheckResult.Pass,
            saved.Failed ? saved.Text : path!);

        MeasureCost(start);
        await MeasurePagingAsync(draft!, token).ConfigureAwait(false);
    }

    /// <summary>
    /// The three action keys the scenario needs. A surface offering one batch call is allowed to
    /// take one; one that does not costs a search per key plus a describe per key, and that is the
    /// round trip the client really pays.
    /// </summary>
    private async Task DescribeKeysAsync(CancellationToken token)
    {
        var keys = new[] { "control.if", "control.repeat", "control.delay" };
        if (_tools.Contains("whalegenie_get_actions"))
        {
            var batch = await InvokeAsync("whalegenie_get_actions",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["keys"] = keys }, token)
                .ConfigureAwait(false);
            Add("discovery.batch", batch.Failed ? McpCheckResult.Missing : McpCheckResult.Pass,
                batch.Failed
                    ? batch.Text
                    : $"{keys.Length} actions in one call, {batch.Text.Length} characters");
            return;
        }

        foreach (var word in new[] { "if", "repeat", "delay" })
        {
            await InvokeAsync("whalegenie_list_actions",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["query"] = word,
                    ["limit"] = 5,
                }, token).ConfigureAwait(false);
        }

        foreach (var key in keys)
        {
            await InvokeAsync("whalegenie_get_action",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["key"] = key }, token)
                .ConfigureAwait(false);
        }

        Add("discovery.batch", McpCheckResult.Missing,
            "no batch describe tool: one search and one describe per action");
    }

    /// <summary>An if, a loop and a delay: the smallest macro that needs a branch, a nested list and
    /// a number with a unit.</summary>
    private JsonObject Scenario()
    {
        static JsonObject Step(string type, JsonObject parameters) => new()
        {
            ["type"] = type,
            ["params"] = parameters,
        };

        return new JsonObject
        {
            ["name"] = _options.ScratchMacro,
            ["steps"] = new JsonArray(
                Step("control.if", new JsonObject
                {
                    ["condition"] = "$sys.screenWidth > 1",
                    ["then"] = new JsonArray(Step("control.delay", new JsonObject { ["ms"] = "200" })),
                    ["else"] = new JsonArray(),
                }),
                Step("control.repeat", new JsonObject
                {
                    ["times"] = "3",
                    ["intervalMs"] = "0",
                    ["body"] = new JsonArray(Step("control.delay", new JsonObject { ["ms"] = "100" })),
                })),
        };
    }

    /// <summary>
    /// The macro handed over in the argument the server declares: an object when the surface takes
    /// one, text when it still takes JSON inside a string. Reading the declared names is how the
    /// ruler follows the server instead of assuming a shape.
    /// </summary>
    private Dictionary<string, object?> MacroArguments(string draft, JsonObject macro)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft };
        var name = Declares("whalegenie_put_macro", "macro") ? "macro" : "macroJson";
        arguments[name] = name == "macro" ? macro : macro.ToJsonString();
        return arguments;
    }

    /// <summary>
    /// Changing one thing in a macro that is already written. The macro here is forty steps long,
    /// because the point of the measurement is what a whole resend costs against one patch — on a
    /// three-step macro there is nothing to save and nothing to learn.
    /// </summary>
    private async Task PatchAsync(CancellationToken token)
    {
        var opened = await InvokeAsync("whalegenie_open_project", [], token).ConfigureAwait(false);
        var draft = Field(opened.Text, "draftId");
        if (string.IsNullOrWhiteSpace(draft))
        {
            Add("patch.open", McpCheckResult.Fail, opened.Text);
            return;
        }

        _drafts.Add(draft!);
        var name = _options.ScratchMacro + " (patch)";

        var steps = new JsonArray();
        for (var at = 0; at < 40; at++)
        {
            steps.Add(new JsonObject
            {
                ["type"] = "control.delay",
                ["params"] = new JsonObject { ["ms"] = "10" },
            });
        }

        var macro = new JsonObject { ["name"] = name, ["steps"] = steps };
        var countedFrom = _report.Calls.Count;
        var put = await InvokeAsync("whalegenie_put_macro", MacroArguments(draft!, macro), token)
            .ConfigureAwait(false);
        var resend = _report.Calls.Skip(countedFrom).Sum(call => call.RequestBytes);
        _report.Metrics["macro.resendBytes"] = resend;
        _report.Metrics["macro.steps"] = 40;

        var ids = Ids(put.Text);
        _report.Metrics["macro.ids"] = ids.Count;
        Add("patch.put", put.Failed ? McpCheckResult.Fail : McpCheckResult.Pass,
            put.Failed ? put.Text : $"{Field(put.Text, "steps")} steps, {ids.Count} ids in the outline");

        // A step written by a client arrives without an id, and a run reports it as step..outcome.
        // Forty distinct ids is what a macro has to carry before a patch or a condition can name one.
        Add("patch.ids", ids.Count == 40 && ids.Distinct(StringComparer.Ordinal).Count() == 40
                ? McpCheckResult.Pass
                : McpCheckResult.Missing,
            ids.Count == 0
                ? "the write answer carries no step ids"
                : $"{ids.Count} ids, {ids.Distinct(StringComparer.Ordinal).Count()} distinct");

        if (ids.Count == 0)
        {
            Add("patch.set", McpCheckResult.Missing, "there is no step id to patch by");
            Add("patch.value", McpCheckResult.Missing, "there is no step id to read back");
            Add("patch.structure", McpCheckResult.Missing, "there is no step id to work with");
            Add("patch.refuse", McpCheckResult.Missing, "there is no step id to work with");
            return;
        }

        var first = ids[0];
        var patchedFrom = _report.Calls.Count;
        var set = await InvokeAsync("whalegenie_patch_macro", new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["draftId"] = draft,
            ["name"] = name,
            ["operations"] = new JsonArray(new JsonObject
            {
                ["op"] = "set",
                ["stepId"] = first,
                ["param"] = "ms",
                ["value"] = "777",
            }),
        }, token).ConfigureAwait(false);
        var patchBytes = _report.Calls.Skip(patchedFrom).Sum(call => call.RequestBytes);
        _report.Metrics["patch.requestBytes"] = patchBytes;
        _report.Metrics["patch.saving"] = patchBytes == 0 ? 0 : (double)resend / patchBytes;

        Add("patch.set", set.Failed ? McpCheckResult.Fail : McpCheckResult.Pass,
            set.Failed
                ? set.Text
                : $"one parameter for {patchBytes} bytes against {resend} for the whole macro");

        var read = await InvokeAsync("whalegenie_get_macro", new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["draftId"] = draft,
            ["name"] = name,
        }, token).ConfigureAwait(false);
        var written = ValueAt(read.Text, first, "ms");
        Add("patch.value", written == "777" ? McpCheckResult.Pass : McpCheckResult.Fail,
            written is null ? "the step or its ms field is not in the macro read back" : $"ms is {written}");

        var before = StepCount(read.Text);
        var inserted = await InvokeAsync("whalegenie_patch_macro", new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["draftId"] = draft,
            ["name"] = name,
            ["operations"] = new JsonArray(
                new JsonObject
                {
                    ["op"] = "insert",
                    ["index"] = 0,
                    ["step"] = new JsonObject
                    {
                        ["type"] = "control.delay",
                        ["params"] = new JsonObject { ["ms"] = "5" },
                    },
                },
                new JsonObject { ["op"] = "remove", ["stepId"] = first }),
        }, token).ConfigureAwait(false);

        var after = before;
        if (!inserted.Failed)
        {
            var again = await InvokeAsync("whalegenie_get_macro", new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["draftId"] = draft,
                ["name"] = name,
            }, token).ConfigureAwait(false);
            after = StepCount(again.Text);
        }

        Add("patch.structure",
            inserted.Failed ? McpCheckResult.Fail
                : after == before ? McpCheckResult.Pass
                : McpCheckResult.Fail,
            inserted.Failed
                ? inserted.Text
                : $"insert and remove kept the count at {after} (was {before})");

        // A table is as cheap to edit as one value. Without the row operations, a call's arguments —
        // the very thing the row list was introduced for — would be the one place a client still had
        // to resend the whole macro to change a single value.
        await InvokeAsync("whalegenie_put_macro", MacroArguments(draft!, new JsonObject
        {
            ["name"] = Called,
            ["arguments"] = new JsonArray(
                new JsonObject { ["name"] = "数量", ["type"] = "Number", ["required"] = true },
                new JsonObject { ["name"] = "标签", ["type"] = "Text", ["required"] = false }),
            ["steps"] = new JsonArray(new JsonObject
            {
                ["type"] = "control.delay",
                ["params"] = new JsonObject { ["ms"] = "1" },
            }),
        }), token).ConfigureAwait(false);

        var withCall = await InvokeAsync("whalegenie_patch_macro", new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["draftId"] = draft,
            ["name"] = name,
            ["operations"] = new JsonArray(new JsonObject
            {
                ["op"] = "insert",
                ["index"] = 0,
                ["step"] = new JsonObject
                {
                    ["type"] = "control.runMacro",
                    ["params"] = new JsonObject
                    {
                        [FieldNames.Macro] = Called,
                        [FieldNames.Arguments] = new JsonArray(new JsonObject
                        {
                            [FieldNames.Name] = "数量",
                            [FieldNames.Value] = "1",
                        }),
                    },
                },
            }),
        }, token).ConfigureAwait(false);

        var callId = Ids(withCall.Text).FirstOrDefault();
        if (callId is null)
        {
            Add("patch.row", McpCheckResult.Missing,
                withCall.Failed ? withCall.Text : "the write answer carries no step id to patch by");
        }
        else
        {
            var rowsFrom = _report.Calls.Count;
            var rows = await InvokeAsync("whalegenie_patch_macro", new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["draftId"] = draft,
                ["name"] = name,
                ["operations"] = new JsonArray(
                    new JsonObject
                    {
                        ["op"] = "addRow",
                        ["stepId"] = callId,
                        ["param"] = FieldNames.Arguments,
                        ["values"] = new JsonObject
                        {
                            [FieldNames.Name] = "标签",
                            [FieldNames.Value] = "甲",
                        },
                    },
                    new JsonObject
                    {
                        ["op"] = "setRow",
                        ["stepId"] = callId,
                        ["param"] = FieldNames.Arguments,
                        ["match"] = new JsonObject { [FieldNames.Name] = "数量" },
                        ["values"] = new JsonObject { [FieldNames.Value] = "$n" },
                    }),
            }, token).ConfigureAwait(false);
            var rowBytes = _report.Calls.Skip(rowsFrom).Sum(call => call.RequestBytes);
            _report.Metrics["patch.rowBytes"] = rowBytes;

            var reread = rows.Failed
                ? string.Empty
                : (await InvokeAsync("whalegenie_get_macro", new Dictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["draftId"] = draft,
                    ["name"] = name,
                }, token).ConfigureAwait(false)).Text;

            // Two rows, one changed and one added, both read back where a run would find them.
            var done = !rows.Failed
                && Mentions(reread, "$n")
                && Mentions(reread, "标签");
            Add("patch.row", rows.Failed ? McpCheckResult.Fail
                    : done ? McpCheckResult.Pass : McpCheckResult.Missing,
                rows.Failed
                    ? rows.Text
                    : done
                        ? $"one row changed and one added for {rowBytes} bytes"
                        : "the rows are not in the macro read back");
        }

        // A case outside a switch is the mistake the catalogue exists to catch. The patch has to
        // refuse it and leave the draft alone, not write it and leave the cleanup to the caller.
        var refused = await InvokeAsync("whalegenie_patch_macro", new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["draftId"] = draft,
            ["name"] = name,
            ["operations"] = new JsonArray(new JsonObject
            {
                ["op"] = "insert",
                ["step"] = new JsonObject
                {
                    ["type"] = "control.case",
                    ["params"] = new JsonObject { ["values"] = "1" },
                },
            }),
        }, token).ConfigureAwait(false);
        Add("patch.refuse", refused.Failed ? McpCheckResult.Pass : McpCheckResult.Fail,
            refused.Failed ? refused.Text : "a case outside a switch was inserted");
    }

    private async Task RejectAsync(CancellationToken token)
    {
        var opened = await InvokeAsync("whalegenie_open_project", [], token).ConfigureAwait(false);
        var draft = Field(opened.Text, "draftId");
        if (string.IsNullOrWhiteSpace(draft))
        {
            Add("reject.open", McpCheckResult.Fail, opened.Text);
            return;
        }

        _drafts.Add(draft!);

        // Every probe carries a step id, and what is looked for is the code rather than the
        // sentence: the sentence is in the interface's language, so a client that compared sentences
        // would break the day someone translates it. The step id stays in the answer for the reader.
        var cases = new (string Check, string Token, string Id, string Macro)[]
        {
            ("reject.action_key", "UnknownAction", "aa22",
                """{"name":"Probe","steps":[{"type":"control.delai","id":"aa22","params":{"ms":"500"}}]}"""),
            ("reject.child_keys", "CaseOutsideSwitch", "bb22",
                """{"name":"Probe","steps":[{"type":"control.case","id":"bb22","params":{"values":"1","body":[]}}]}"""),
            ("reject.record_field", "WindowBounds", "cc22",
                """{"name":"Probe","steps":[{"type":"window.move","id":"cc22","params":{"rect":{"left":1,"top":2,"height":100}}}]}"""),
            ("reject.enum_value", "BadOption", "dd22",
                """{"name":"Probe","steps":[{"type":"control.switch","id":"dd22","params":{"switchValue":"1","switchMode":"bogus","cases":[{"type":"control.case","params":{"values":"1","body":[]}}]}}]}"""),
        };

        foreach (var (check, word, id, macro) in cases)
        {
            var written = await InvokeAsync("whalegenie_put_macro",
                MacroArguments(draft!, JsonNode.Parse(macro)!.AsObject()), token).ConfigureAwait(false);
            if (written.Failed)
            {
                Add(check, McpCheckResult.Pass, $"refused at the write: {written.Text}");
                continue;
            }

            var validated = await InvokeAsync("whalegenie_validate",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
                .ConfigureAwait(false);
            var problems = Problems(validated.Text, "blocking", "messages");
            var named = problems.FirstOrDefault(problem => Mentions(problem, word))
                ?? problems.FirstOrDefault(problem => Mentions(problem, id));
            Add(check, named is not null ? McpCheckResult.Pass
                    : problems.Count == 0 ? McpCheckResult.Fail : McpCheckResult.Missing,
                named is not null
                    ? $"located it: {named}"
                    : problems.Count == 0
                        ? "the wrong macro was accepted without a word"
                        : $"reported, but neither \"{word}\" nor step {id} is named: {string.Join(" / ", problems)}");
        }

        // A value that is not JSON at all. Whatever parameter carries the macro, text that does not
        // parse has to come back as a refusal rather than a half-written macro.
        var name = Declares("whalegenie_put_macro", "macro") ? "macro" : "macroJson";
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft };
        arguments[name] = """{"name":""";
        var broken = await InvokeAsync("whalegenie_put_macro", arguments, token).ConfigureAwait(false);
        Add("reject.bad_json", broken.Failed ? McpCheckResult.Pass : McpCheckResult.Fail,
            broken.Failed ? broken.Text : "a truncated value was accepted");

        // What a macro takes and gives back lives in that macro, not in the catalogue: for a step
        // that calls it, the signature tool is the only place an outside client can read the names.
        var child = new JsonObject
        {
            ["name"] = "Child",
            ["arguments"] = new JsonArray(new JsonObject
            {
                ["name"] = "数量",
                ["type"] = "Number",
                ["required"] = true,
            }),
            ["returns"] = new JsonArray(new JsonObject { ["name"] = "答案", ["type"] = "Number" }),
            ["steps"] = new JsonArray(new JsonObject
            {
                ["type"] = "control.delay",
                ["params"] = new JsonObject { ["ms"] = "1" },
            }),
        };
        await InvokeAsync("whalegenie_put_macro", MacroArguments(draft!, child), token)
            .ConfigureAwait(false);
        var signature = await InvokeAsync("whalegenie_get_macro_signature",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["draftId"] = draft,
                ["name"] = "Child",
            }, token).ConfigureAwait(false);
        // A call passes rows now, so what the signature has to say is which columns a row has and
        // what a skeleton of them looks like — the names themselves are in the list either way.
        var described = !signature.Failed
            && Mentions(signature.Text, "argumentColumns")
            && Mentions(signature.Text, "callTemplate")
            && Mentions(signature.Text, "数量");
        Add("signature.tool", described ? McpCheckResult.Pass : McpCheckResult.Missing,
            described ? "the signature names the argument columns and gives a call skeleton" : signature.Text);

        // The catalogue can only say where a call's parameters come from; that it says so, and that
        // the argument list is the shape a row list has, is what a client reads before writing one.
        var call = await InvokeAsync("whalegenie_get_action",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["key"] = "control.runMacro" },
            token).ConfigureAwait(false);
        var shaped = !call.Failed
            && Mentions(call.Text, "signatureSource")
            && Mentions(call.Text, "arguments")
            && Mentions(call.Text, "given");
        Add("action.callShape", shaped ? McpCheckResult.Pass : McpCheckResult.Missing,
            shaped ? "the catalogue names where a call's parameters come from and the rows they have"
                : call.Text);

        await InvokeAsync("whalegenie_discard",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
            .ConfigureAwait(false);
        _drafts.Remove(draft!);
    }

    /// <summary>
    /// The catalogue as a resource: a client that can cache it reads it once, and one that cannot
    /// falls back to the tools. Both say the same thing, which is why this only checks that the
    /// resource is there and reads back as the catalogue.
    /// </summary>
    private async Task ResourcesAsync(CancellationToken token)
    {
        try
        {
            var uris = (await client.ListResourcesAsync(cancellationToken: token)
                    .ConfigureAwait(false))
                .Select(resource => resource.Uri).ToList();
            var templates = (await client.ListResourceTemplatesAsync(cancellationToken: token)
                    .ConfigureAwait(false))
                .Select(template => template.UriTemplate).ToList();

            Add("resources.listed",
                uris.Count > 0 || templates.Count > 0 ? McpCheckResult.Pass : McpCheckResult.Missing,
                uris.Count + templates.Count == 0
                    ? "the server lists no resources"
                    : $"{uris.Count} resources, {templates.Count} templates: "
                        + string.Join(" / ", uris.Concat(templates)));

            if (!uris.Any(uri => uri.Contains("actions", StringComparison.Ordinal)))
            {
                Add("resources.read", McpCheckResult.Missing, "there is no catalogue resource to read");
                return;
            }

            var read = await client.ReadResourceAsync(
                uris.First(uri => uri.Contains("actions", StringComparison.Ordinal)),
                cancellationToken: token).ConfigureAwait(false);
            var text = string.Concat(read.Contents
                .OfType<ModelContextProtocol.Protocol.TextResourceContents>()
                .Select(content => content.Text));
            var total = Field(text, "total");
            Add("resources.read",
                total is { Length: > 0 } ? McpCheckResult.Pass : McpCheckResult.Missing,
                total is { Length: > 0 }
                    ? $"the catalogue reads back as a resource with {total} actions"
                    : text);
        }
        catch (Exception error)
        {
            // A client older than resources, or a server without them, is a fact to record rather
            // than a reason to stop measuring everything else.
            Add("resources.listed", McpCheckResult.Missing, Describe(error));
        }
    }

    private async Task GateAsync(CancellationToken token)
    {
        if (!_options.AllowApply)
        {
            Add("gate.apply_invalid", McpCheckResult.Skip, "the run may not write the project");
            return;
        }

        var opened = await InvokeAsync("whalegenie_open_project", [], token).ConfigureAwait(false);
        var draft = Field(opened.Text, "draftId");
        if (string.IsNullOrWhiteSpace(draft))
        {
            Add("gate.apply_invalid", McpCheckResult.Fail, opened.Text);
            return;
        }

        _drafts.Add(draft!);
        var wrong = new JsonObject
        {
            ["name"] = "Gate probe",
            ["steps"] = new JsonArray(new JsonObject
            {
                ["type"] = "control.delai",
                ["params"] = new JsonObject { ["ms"] = "1" },
            }),
        };
        await InvokeAsync("whalegenie_put_macro", MacroArguments(draft!, wrong), token).ConfigureAwait(false);

        var applied = await InvokeAsync("whalegenie_apply",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
            .ConfigureAwait(false);
        Add("gate.apply_invalid", applied.Failed ? McpCheckResult.Pass : McpCheckResult.Missing,
            applied.Failed
                ? applied.Text
                : "apply accepted a draft the validator calls wrong");
    }

    /// <summary>
    /// Whether the list tools say how much they did not show. Read from real answers, because the
    /// shape is the question; nothing here is inspection of the schema.
    /// </summary>
    private async Task MeasurePagingAsync(string draft, CancellationToken token)
    {
        var actions = await InvokeAsync("whalegenie_list_actions",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["limit"] = 5 }, token)
            .ConfigureAwait(false);
        var macros = await InvokeAsync("whalegenie_list_macros",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
            .ConfigureAwait(false);
        var runs = await InvokeAsync("whalegenie_list_runs",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["limit"] = 5 }, token)
            .ConfigureAwait(false);
        var log = await InvokeAsync("whalegenie_get_run_log",
            new Dictionary<string, object?>(StringComparer.Ordinal), token).ConfigureAwait(false);

        _report.Facts["paging.whalegenie_list_actions"] = Shape(actions);
        _report.Facts["paging.whalegenie_list_macros"] = Shape(macros);
        _report.Facts["paging.whalegenie_list_runs"] = Shape(runs);
        _report.Facts["paging.whalegenie_get_run_log"] = Shape(log);

        _report.Metrics["paging.withTotal"] = _report.Facts
            .Count(pair => pair.Key.StartsWith("paging.", StringComparison.Ordinal)
                && pair.Value.Contains("total", StringComparison.Ordinal));
        _report.Metrics["paging.measured"] = _report.Facts
            .Count(pair => pair.Key.StartsWith("paging.", StringComparison.Ordinal));
    }

    private void MeasureCost(int start)
    {
        var window = _report.Calls.Skip(start).ToList();
        _report.Metrics["scenario.calls"] = window.Count;
        _report.Metrics["scenario.requestBytes"] = window.Sum(call => call.RequestBytes);
        _report.Metrics["scenario.responseBytes"] = window.Sum(call => call.ResponseBytes);
        _report.Metrics["scenario.longestCall"] = window.Count == 0 ? 0 : window.Max(call => call.ResponseBytes);
        _report.Facts["scenario.policy"] = _tools.Contains("whalegenie_get_actions")
            ? "status, open, one batch describe, template, put, validate, apply, save"
            : "status, open, a search and a describe per key, template, put, validate, apply, save";
    }

    private async Task DiscardAsync(CancellationToken token)
    {
        foreach (var draft in _drafts.ToArray())
        {
            var answer = await InvokeAsync("whalegenie_discard",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
                .ConfigureAwait(false);
            Add($"cleanup.{draft}", answer.Failed ? McpCheckResult.Missing : McpCheckResult.Pass,
                answer.Failed ? answer.Text : "discarded");
        }

        _drafts.Clear();
    }

    private async Task<Answer> InvokeAsync(string tool, Dictionary<string, object?> arguments,
        CancellationToken token)
    {
        var requestBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(arguments));
        var watch = Stopwatch.StartNew();
        if (!_tools.Contains(tool))
        {
            _report.Calls.Add(new McpCall(tool, requestBytes, 0, 0, true));
            return new Answer(true, $"\"{tool}\" is not among the tools this server lists");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(_options.CallTimeout);
        try
        {
            var result = await client.CallToolAsync(tool, arguments, cancellationToken: limit.Token)
                .ConfigureAwait(false);
            var text = string.Concat((result.Content ?? [])
                .OfType<TextContentBlock>()
                .Select(block => block.Text));
            var failed = result.IsError == true;
            _report.Calls.Add(new McpCall(tool, requestBytes, Encoding.UTF8.GetByteCount(text),
                watch.ElapsedMilliseconds, failed));
            return new Answer(failed, text);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _report.Calls.Add(new McpCall(tool, requestBytes, 0, watch.ElapsedMilliseconds, true));
            return new Answer(true, Describe(error));
        }
    }

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

    private static string Describe(Exception error)
        => $"{error.GetType().Name}: {error.Message}";

    private static string Clip(string text)
        => text.Length <= 240 ? text : text[..240] + "…";

    private sealed record Answer(bool Failed, string Text);
}
