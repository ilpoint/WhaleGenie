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
using WhaleGenie.Storage;


namespace WhaleGenie.Mcp;

// The pages a client reads before writing: instructions, the template, the macro list and the globals.
public sealed partial class McpTools
{
    [McpServerTool(Name = "whalegenie_instructions", ReadOnly = true,
            Title = "Read the workflow"),
     Description("Returns the default writing or debugging instructions, for clients that do not show MCP prompts.")]
    public Task<CallToolResult> Instructions(
        [Description("Write or debug; write by default.")] InstructionTopic topic = InstructionTopic.Write)
        => Task.FromResult(McpErrors.Plain(
            topic == InstructionTopic.Debug ? McpPrompts.DebugMacro() : McpPrompts.WriteMacro()));

    [McpServerTool(Name = "whalegenie_macro_template", ReadOnly = true,
            Title = "Empty macro"),
     Description("Returns an empty macro in the exact JSON shape whalegenie_put_macro accepts.")]
    public Task<CallToolResult> MacroTemplate(
        [Description("The macro name to put in the template.")] string name = "New macro")
        => McpErrors.RunAsync(async () => McpErrors.Ok(
            await Ui(() => new MacroItem { Name = name }.ToJson())));

    [McpServerTool(Name = "whalegenie_list_macros", ReadOnly = true,
            Title = "List the macros in a draft"),
     Description("Lists the macros in an MCP draft with the total, and for each one the arguments it declares (name, type, required) and the results it hands back — which is what a call to it has to know. A long project is paged: hasMore says whether anything was left out.")]
    public Task<CallToolResult> ListMacros(
        [Description("The draft id returned by whalegenie_open_project.")] string draftId,
        [Description("How many to skip; empty starts at the first.")] int offset = 0,
        [Description("How many to return, 1 to 200; 100 by default.")] int limit = 100)
        => McpErrors.RunAsync(async () => McpErrors.Ok(await Ui(() =>
        {
            var all = drafts.Get(draftId).MacroSnapshot().Select(McpOutline.Summary).ToList();
            var from = Math.Clamp(offset, 0, all.Count);
            var size = Math.Clamp(limit <= 0 ? 100 : limit, 1, 200);
            var page = all.Skip(from).Take(size).ToList();
            return new
            {
                total = all.Count,
                offset = from,
                limit = size,
                hasMore = from + page.Count < all.Count,
                macros = page,
            };
        })));

    [McpServerTool(Name = "whalegenie_get_macro", ReadOnly = true,
            Title = "Read one macro"),
     Description("Returns one macro as the JSON the package stores, triggers included: the same shape whalegenie_put_macro accepts. When only one step's parameters are wanted, whalegenie_get_step reads that step alone instead of the whole macro.")]
    public Task<CallToolResult> GetMacro(
        [Description("The draft id.")] string draftId,
        [Description("The macro name.")] string name)
        => McpErrors.RunAsync(async () =>
        {
            var draft = drafts.Get(draftId);
            var macro = await Ui(() => draft.Find(name));
            return macro is null
                ? McpErrors.Fail(McpErrors.NoSuchMacro, $"There is no macro named \"{name}\".",
                    "Call whalegenie_list_macros to see the names this draft holds.",
                    retryable: true, details: new { name })
                : McpErrors.Ok(await Ui(() => draft.MacroJson(macro)));
        });

    [McpServerTool(Name = "whalegenie_get_step", ReadOnly = true,
            Title = "Read one step"),
     Description("Returns one step of a macro as the JSON the package stores, the path it sits at, and whatever validation says about that step alone. Reading one step is what a client needs before changing one parameter: fetching the whole macro for that is the cost this avoids.")]
    public Task<CallToolResult> GetStep(
        [Description("The draft id.")] string draftId,
        [Description("The macro name.")] string name,
        [Description("The four-character step id from an outline.")] string stepId)
        => McpErrors.RunAsync(async () =>
        {
            var answer = await Ui(() =>
            {
                var draft = drafts.Get(draftId);
                var macro = draft.Find(name);
                if (macro is null)
                {
                    return null;
                }

                // The outer macro first, then every document written inside it, level by level: an
                // id is unique across the whole chain, so the first place it turns up is the place
                // it belongs to, and the label is the path a patch sends back.
                var found = Find(macro.Steps, stepId, string.Empty);
                var scope = "main";
                foreach (var (sub, label) in Documents(macro.SubMacros, "main"))
                {
                    if (found is not null)
                    {
                        break;
                    }

                    found = Find(sub.Steps, stepId, string.Empty);
                    scope = label;
                }

                return found is { } place
                    ? new
                    {
                        name,
                        stepId,
                        macro = scope,
                        place.Path,
                        type = place.Step.Type,
                        step = place.Step.ToJson(),
                        problems = Problems(draft, name, stepId),
                    }
                    : null;
            });

            return answer is null
                ? McpErrors.Fail(McpErrors.NoSuchMacro, $"There is no macro named \"{name}\" with step \"{stepId}\".",
                    "Call whalegenie_get_macro and read its outline; the path there is what insert and move use.",
                    retryable: true, details: new { name, stepId })
                : McpErrors.Ok(answer);
        });

    [McpServerTool(Name = "whalegenie_get_globals", ReadOnly = true,
            Title = "Read the global declarations"),
     Description("Returns the package's global variable declarations in the shape whalegenie_set_globals takes, so a whole-list write does not have to be made blind.")]
    public Task<CallToolResult> GetGlobals(
        [Description("The draft id.")] string draftId)
        => McpErrors.RunAsync(async () => McpErrors.Ok(await Ui(() =>
        {
            var globals = drafts.Get(draftId).GlobalList();
            return new { count = globals.Count, globals };
        })));
}
