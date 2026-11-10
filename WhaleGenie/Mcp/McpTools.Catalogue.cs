using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Mcp;

// What a client can ask about: the open project, the actions and their schemas, and a macro signature.
public sealed partial class McpTools
{
    [McpServerTool(Name = "whalegenie_project_status", ReadOnly = true,
            Title = "Project status"),
     Description("Reports the open WhaleGenie project: package path, revision, dirty state and macro count, and which of the two permissions a client has right now — sending input, and running a macro.")]
    public Task<CallToolResult> ProjectStatus()
        => McpErrors.RunAsync(async () =>
        {
            var status = await project.StatusAsync();
            return McpErrors.Ok(new
            {
                path = status.Path,
                revision = status.Revision,
                dirty = status.Dirty,
                macroCount = status.MacroCount,

                // What a client is allowed to do, so it can say "the switch is off" instead of
                // finding out by being refused.
                control = new { allowed = McpControl.Allowed, runAllowed = McpControl.AllowsRun },
            });
        });

    [McpServerTool(Name = "whalegenie_list_actions", ReadOnly = true,
            Title = "Search the action catalogue"),
     Description("Searches the action catalogue and returns one page with the total it was cut from, so a client knows whether anything was left out. Hidden actions are excluded unless asked for: they are only valid inside a child list whose childKeys name them.")]
    public Task<CallToolResult> ListActions(
        [Description("Words to look for in the key, name, description or category; empty lists every action.")] string? query = null,
        [Description("Only actions in this category, for example Control, Input, Vision, Window, Uia, Browser. Empty means every category.")] string? category = null,
        [Description("Whether hidden actions are included. They are only valid in a child list whose childKeys name them; off by default.")] bool includeHidden = false,
        [Description("How many actions to skip. Pass the returned total to see whether anything is left.")] int offset = 0,
        [Description("How many actions to return, 1 to 200; 25 by default.")] int limit = 25)
        => McpErrors.RunAsync(async () => McpErrors.Ok(
            await Ui(() => ActionKnowledge.Search(query, category, includeHidden, offset, limit))));

    [McpServerTool(Name = "whalegenie_get_action", ReadOnly = true,
            Title = "Describe one action"),
     Description("Describes one action: hidden state, whether it sends input or looks at the screen, whether and how it waits, parameters, childKeys, kinds, defaults, options, reading rules, record fields, number units and ranges, and returns. A required field marked conditional is required only in the modes its hint names, such as the window rule of a background click. An action marked irreversible, or a choice parameter whose irreversibleWhen names values, destroys something that cannot be brought back: the person is asked before that step runs, and a run with nobody there fails with Run.NotConfirmed. A wrong key is refused with the nearest keys as candidates.")]
    public Task<CallToolResult> GetAction(
        [Description("The action key, for example control.delay.")] string key)
        => McpErrors.RunAsync(async () =>
        {
            if (await Ui(() => ActionKnowledge.Find(key)) is { } action)
            {
                return McpErrors.Ok(action);
            }

            var candidates = await Ui(() => ActionKnowledge.Suggest(key));
            return McpErrors.Fail(McpErrors.UnknownAction, $"There is no action \"{key}\".",
                "Action keys come from the catalogue; search with a word from the key to find the right one.",
                retryable: true, details: new { key, candidates });
        });

    [McpServerTool(Name = "whalegenie_get_actions", ReadOnly = true,
            Title = "Describe several actions"),
     Description("Describes several actions in one call, which is what writing a macro needs: one round trip for the three or four keys a step list uses. The same fields as get_action come back, including irreversible and irreversibleWhen. Keys that do not exist come back under missing instead of failing the whole answer.")]
    public Task<CallToolResult> GetActions(
        [Description("The action keys to describe, for example [\"control.if\",\"control.repeat\"].")] string[] keys)
        => McpErrors.RunAsync(async () => McpErrors.Ok(await Ui(() =>
        {
            var found = ActionKnowledge.FindMany(keys);
            return new
            {
                count = found.Count(lookup => lookup.Found),
                actions = found.Where(lookup => lookup.Found).Select(lookup => lookup.Action),
                missing = found.Where(lookup => !lookup.Found).Select(lookup => lookup.Key),
            };
        })));

    [McpServerTool(Name = "whalegenie_get_macro_signature", ReadOnly = true,
            Title = "What a macro takes and gives back"),
     Description("Returns what one macro of the draft declares: its arguments (name, type, required, default, description) and the results it hands back, plus a call template. A control.runMacro step passes each argument as one row of params.arguments (columns name, value, given) and asks for results in returns rows of key and name; those names come from the called macro, not from the action catalogue, so this is where they are read.")]
    public Task<CallToolResult> GetMacroSignature(
        [Description("The draft id.")] string draftId,
        [Description("The macro name.")] string name)
        => McpErrors.RunAsync(async () =>
        {
            var macro = await Ui(() => drafts.Get(draftId).Find(name));
            if (macro is null)
            {
                return McpErrors.Fail(McpErrors.NoSuchMacro, $"There is no macro named \"{name}\".",
                    "Call whalegenie_list_macros to see the names this draft holds.",
                    retryable: true, details: new { name });
            }

            return McpErrors.Ok(await Ui(() => Signature(macro)));
        });
}
