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

// Step-level editing through patches: what changes, what is refused and what the result says.
internal sealed partial class McpSelfCheck
{
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
}
