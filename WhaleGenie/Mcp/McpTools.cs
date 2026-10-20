using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WhaleGenie.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>
/// The tools an MCP client sees. They read and write a draft of the open project; the project
/// itself is only touched by apply, and its package only by save. Running is deliberately absent:
/// the person at the machine still presses the run button.
/// </summary>
[McpServerToolType]
public sealed class McpTools(McpDraftStore drafts, McpLiveProject project)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static async Task<T> Ui<T>(Func<T> work) => await Dispatcher.UIThread.InvokeAsync(work);

    private static string Encode(object value) => JsonSerializer.Serialize(value, Json);

    private static JsonObject ParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw new McpException("The value has to be a JSON object.");
        }
        catch (JsonException error)
        {
            throw new McpException($"The value is not valid JSON: {error.Message}", error);
        }
    }

    private static JsonArray ParseArray(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonArray
                ?? throw new McpException("The value has to be a JSON array.");
        }
        catch (JsonException error)
        {
            throw new McpException($"The value is not valid JSON: {error.Message}", error);
        }
    }

    [McpServerTool(Name = "whalegenie_project_status", ReadOnly = true),
     Description("Reports the open WhaleGenie project: package path, revision, dirty state and macro count.")]
    public async Task<string> ProjectStatus()
        => Encode(await project.StatusAsync());

    [McpServerTool(Name = "whalegenie_list_actions", ReadOnly = true),
     Description("Lists WhaleGenie actions, optionally filtered by a word in the key, name or description.")]
    public async Task<string> ListActions(
        [Description("Words to look for; empty lists the first actions in key order.")] string? query = null,
        [Description("How many actions to return, 1 to 50.")] int limit = 10)
        => Encode(await Ui(() => ActionKnowledge.Search(query, limit)));

    [McpServerTool(Name = "whalegenie_get_action", ReadOnly = true),
     Description("Describes one action: parameters, kinds, defaults, options, reading rules, record fields, number units and ranges, and returns.")]
    public async Task<string> GetAction(
        [Description("The action key, for example control.delay.")] string key)
        => await Ui(() => ActionKnowledge.Find(key) is { } action
            ? Encode(action)
            : throw new McpException($"There is no action \"{key}\"."));

    [McpServerTool(Name = "whalegenie_open_project", ReadOnly = true),
     Description("Takes a snapshot of the open project and returns a draft id for editing it.")]
    public async Task<string> OpenProject()
    {
        var basis = await project.SnapshotAsync();
        var draft = drafts.Create(basis);
        return Encode(new
        {
            draftId = draft.Id,
            path = basis.Path,
            revision = basis.Revision,
            macros = basis.Macros.Select(Summary),
        });
    }

    [McpServerTool(Name = "whalegenie_instructions", ReadOnly = true),
     Description("Returns the default writing or debugging instructions for clients that do not show MCP prompts.")]
    public Task<string> Instructions(
        [Description("write or debug; empty means write.")] string? topic = null)
        => Task.FromResult(string.Equals(topic?.Trim(), "debug", StringComparison.OrdinalIgnoreCase)
            ? McpPrompts.DebugMacro()
            : McpPrompts.WriteMacro());

    [McpServerTool(Name = "whalegenie_macro_template", ReadOnly = true),
     Description("Returns an empty macro in the exact JSON shape whalegenie_put_macro accepts.")]
    public async Task<string> MacroTemplate(
        [Description("The macro name to put in the template.")] string name = "New macro")
        => Encode(await Ui(() => new MacroItem { Name = name }.ToJson()));

    [McpServerTool(Name = "whalegenie_list_macros", ReadOnly = true),
     Description("Lists the macros in an MCP draft.")]
    public async Task<string> ListMacros(
        [Description("The draft id returned by whalegenie_open_project.")] string draftId)
        => Encode(await Ui(() => drafts.Get(draftId).MacroSnapshot().Select(Summary)));

    [McpServerTool(Name = "whalegenie_get_macro", ReadOnly = true),
     Description("Returns one macro as the JSON the WhaleGenie package stores.")]
    public async Task<string> GetMacro(
        [Description("The draft id.")] string draftId,
        [Description("The macro name.")] string name)
        => await Ui(() => Encode(drafts.Get(draftId).Find(name) is { } macro
            ? macro.ToJson(includeTriggers: true)
            : throw new McpException($"There is no macro named \"{name}\".")));

    [McpServerTool(Name = "whalegenie_put_macro", Destructive = true),
     Description("Adds or replaces a whole macro in a draft. The JSON has the same shape as whalegenie_get_macro.")]
    public async Task<string> PutMacro(
        [Description("The draft id.")] string draftId,
        [Description("Macro JSON: name, trigger fields, arguments, returns and steps.")] string macroJson)
    {
        var json = ParseObject(macroJson);
        var draft = drafts.Get(draftId);
        var macro = await Ui(() => draft.Put(json));
        var problems = await Ui(() => McpDraftStore.Validate(draft));
        return Encode(new
        {
            name = macro.Name,
            steps = macro.Steps.Count,
            problems = problems.Messages,
            macro = await Ui(() => macro.ToJson(includeTriggers: true)),
        });
    }

    [McpServerTool(Name = "whalegenie_delete_macro", Destructive = true),
     Description("Deletes one macro from a draft; nothing is saved until whalegenie_save_project.")]
    public async Task<string> DeleteMacro(
        [Description("The draft id.")] string draftId,
        [Description("The macro name.")] string name)
    {
        var draft = drafts.Get(draftId);
        await Ui(() =>
        {
            draft.Delete(name);
            return true;
        });
        return Encode(new { deleted = name });
    }

    [McpServerTool(Name = "whalegenie_set_globals", Destructive = true),
     Description("Replaces the global variable declarations in a draft.")]
    public async Task<string> SetGlobals(
        [Description("The draft id.")] string draftId,
        [Description("A JSON array of {name,type,default,description} objects.")] string globalsJson)
    {
        var array = ParseArray(globalsJson);
        var draft = drafts.Get(draftId);
        await Ui(() =>
        {
            draft.SetGlobals(array);
            return true;
        });
        return Encode(new { count = draft.Globals.Count });
    }

    [McpServerTool(Name = "whalegenie_validate", ReadOnly = true),
     Description("Checks every macro in a draft against the action catalogue and the interface declarations.")]
    public async Task<string> Validate(
        [Description("The draft id.")] string draftId)
        => Encode(await Ui(() => McpDraftStore.Validate(drafts.Get(draftId))));

    [McpServerTool(Name = "whalegenie_apply", Destructive = true),
     Description("Writes a draft back into the open project. It does not save the package; the person still reviews and saves it.")]
    public async Task<string> Apply(
        [Description("The draft id.")] string draftId)
    {
        var draft = drafts.Get(draftId);
        if (draft.Applied)
        {
            throw new McpException("This draft has already been applied.");
        }

        var macros = draft.MacroSnapshot();
        var triggers = draft.Triggers.Copy();
        await Ui(() =>
        {
            triggers.Attach(macros);
            return true;
        });
        var globals = await Ui(() => draft.Globals.ToList());
        var revision = await project.ApplyAsync(draft.Basis, macros, triggers, globals);
        draft.MarkApplied();
        return Encode(new { revision, macros = macros.Count });
    }

    [McpServerTool(Name = "whalegenie_save_project", Destructive = true),
     Description("Saves the open project to its current .wgmacro package, after keeping a .bak copy.")]
    public async Task<string> SaveProject()
        => Encode(new { path = await project.SaveAsync() });

    [McpServerTool(Name = "whalegenie_discard", Destructive = true),
     Description("Drops a draft. Applied changes already in the project stay there and can still be saved by the person.")]
    public Task<string> Discard(
        [Description("The draft id.")] string draftId)
    {
        drafts.Remove(draftId);
        return Task.FromResult(Encode(new { discarded = draftId }));
    }

    [McpServerTool(Name = "whalegenie_list_runs", ReadOnly = true),
     Description("Lists the last runs WhaleGenie performed. Running itself is done by the person, not by MCP.")]
    public Task<string> ListRuns(
        [Description("How many runs to return, 1 to 20.")] int limit = 10)
        => Task.FromResult(Encode(RunLogStore.Recent(limit).Select(RunSummary)));

    [McpServerTool(Name = "whalegenie_get_run_log", ReadOnly = true),
     Description("Returns one run's log and step outcomes, or the latest run when no id is given.")]
    public Task<string> GetRunLog(
        [Description("The run id from whalegenie_list_runs; empty means the latest run.")] string? runId = null)
    {
        var run = string.IsNullOrWhiteSpace(runId)
            ? RunLogStore.Latest()
            : RunLogStore.Find(runId);
        if (run is null)
        {
            throw new McpException("There is no such run.");
        }

        return Task.FromResult(Encode(new
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
            lines = Limit(run.Lines.Select(line => new
            {
                time = line.Time,
                level = line.Level.ToString(),
                depth = line.Depth,
                text = line.Text,
            })),
            outcomes = run.Outcomes,
        }));
    }

    [McpServerTool(Name = "whalegenie_get_last_failure_log", ReadOnly = true),
     Description("Returns the newest failure log WhaleGenie wrote, for debugging a run the person already performed.")]
    public Task<string> GetLastFailureLog()
    {
        var file = Directory.Exists(AppPaths.Logs)
            ? Directory.EnumerateFiles(AppPaths.Logs, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        if (file is null)
        {
            return Task.FromResult(Encode(new
            {
                path = string.Empty,
                text = string.Empty,
                truncated = false,
            }));
        }

        var text = File.ReadAllText(file);
        var truncated = text.Length > 65536;
        if (truncated)
        {
            text = text[^65536..];
        }

        return Task.FromResult(Encode(new { path = file, text, truncated }));
    }

    private static object Summary(Models.MacroItem macro) => new
    {
        name = macro.Name,
        steps = macro.Steps.Count,
        parameters = macro.Parameters.Count,
        results = macro.Results.Count,
    };

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

    private static IEnumerable<T> Limit<T>(IEnumerable<T> lines)
    {
        var list = lines.ToList();
        return list.Count <= 500 ? list : list.Take(500);
    }
}
