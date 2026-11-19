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

// One full loop: opening a project, changing a step and applying the draft back.
internal sealed partial class McpSelfCheck
{
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

        if (!_writes)
        {
            var why = _controlAllowed ? "the run may not write the project" : WriteOff;
            Add("loop.apply", McpCheckResult.Skip, why);
            Add("loop.save", McpCheckResult.Skip, why);
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
}
