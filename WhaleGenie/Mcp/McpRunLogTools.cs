using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Mcp;

// The run log: listing runs and reading one, or the newest failure log.
[McpServerToolType]
public sealed class McpRunLogTools
{
    [McpServerTool(Name = "whalegenie_list_runs", ReadOnly = true,
            Title = "List the last runs"),
     Description("Lists the last runs WhaleGenie performed, newest first, with the total. A run comes from the person pressing Run, from a trigger, or from whalegenie_run_macro.")]
    public Task<CallToolResult> ListRuns(
        [Description("How many runs to return, 1 to 20.")] int limit = 10)
    {
        var kept = RunLogStore.Recent(20);
        var recent = kept.Take(Math.Clamp(limit, 1, 20)).Select(RunSummary).ToList();
        return Task.FromResult(McpErrors.Ok(new
        {
            total = kept.Count,
            returned = recent.Count,
            runs = recent,
        }));
    }

    [McpServerTool(Name = "whalegenie_get_run_log", ReadOnly = true,
            Title = "Read one run's log"),
     Description("Returns one run's log and step outcomes, or the latest run when no id is given. A long log is cut off and says so: totalLines is what the run wrote, returned is what came back.")]
    public Task<CallToolResult> GetRunLog(
        [Description("The run id from whalegenie_list_runs; empty means the latest run.")] string? runId = null,
        [Description("How many lines to return, counted from the end: the last ones are where a run goes wrong. 500 at most.")] int tail = 500)
    {
        var asked = (runId ?? string.Empty).Trim();
        var run = asked.Length == 0 ? RunLogStore.Latest() : RunLogStore.Find(asked);
        if (run is null && asked.Length == 0)
        {
            // Nothing has run yet is the state a new session starts in, not a mistake: an empty
            // answer with the same fields says so, so a client can read the log before anything has
            // happened instead of treating its first look as an error to retry or branch on.
            return Task.FromResult(McpErrors.Ok(new
            {
                totalLines = 0,
                returned = 0,
                truncated = false,
                fromLine = 0,
                lines = Array.Empty<object>(),
                outcomes = Array.Empty<object>(),
                note = "No run has been recorded yet. whalegenie_run_macro runs one (it needs the run "
                    + "switch in the MCP flyout), or the person presses Run in WhaleGenie; read this "
                    + "again afterwards.",
            }));
        }

        if (run is null)
        {
            return Task.FromResult(McpErrors.Fail(McpErrors.NoSuchRun,
                $"There is no run \"{asked}\".",
                "Call whalegenie_list_runs first; it lists what the person has run, newest first.",
                retryable: true, details: new { runId = asked }));
        }

        var lines = run.Lines.Select(line => new
        {
            time = line.Time,
            level = line.Level.ToString(),
            depth = line.Depth,
            text = line.Text,
        }).ToList();
        var returned = Math.Clamp(tail <= 0 ? 500 : tail, 1, 500);
        var from = Math.Max(0, lines.Count - returned);
        return Task.FromResult(McpErrors.Ok(new
        {
            run.Id,
            run.Macro,
            run.Source,
            run.Started,
            run.Finished,
            run.Status,
            run.Key,
            run.Detail,
            run.Steps,
            run.FailureLogPath,
            totalLines = lines.Count,
            returned,
            truncated = lines.Count > returned,
            fromLine = from,
            lines = lines.Skip(from),
            outcomes = run.Outcomes,
        }));
    }

    [McpServerTool(Name = "whalegenie_get_last_failure_log", ReadOnly = true,
            Title = "Read the newest failure log"),
     Description("Returns the newest failure log WhaleGenie wrote, or the one at a path an earlier answer named. Only files a failed run writes are read: the call log the server keeps is not one of them.")]
    public Task<CallToolResult> GetLastFailureLog(
        [Description("A path from a run's failureLogPath; empty means the newest failure log.")] string? path = null)
    {
        var file = McpFailureLogs.Named(AppPaths.Logs, path);

        if (file is null)
        {
            return Task.FromResult(McpErrors.Ok(new
            {
                path = string.Empty,
                text = string.Empty,
                truncated = false,
            }));
        }

        try
        {
            var text = File.ReadAllText(file);
            var truncated = text.Length > 65536;
            if (truncated)
            {
                text = text[^65536..];
            }

            return Task.FromResult(McpErrors.Ok(new { path = file, text, truncated }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(McpErrors.Fail(McpErrors.BadArgument,
                $"The failure log could not be read: {error.Message}",
                "The file may still be open in another program.", details: new { file }));
        }
    }
    private static object RunSummary(RunLogSnapshot run) => new
    {
        run.Id,
        run.Macro,
        run.Source,
        run.Started,
        run.Finished,
        run.Status,
        run.Key,
        run.Detail,
        run.Steps,
        run.FailureLogPath,
        lines = run.Lines.Count,
    };
}
