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

// Changing a draft: putting a macro, patching one, deleting one and setting the globals.
public sealed partial class McpTools
{
    [McpServerTool(Name = "whalegenie_put_macro", Destructive = true,
            Title = "Write a whole macro"),
     Description("Adds or replaces a whole macro in a draft. The object has the same shape as whalegenie_get_macro. The answer carries the outline of what was written (every step's id, type and path) and what validation says, split into blocking problems and hints. The macro itself is not echoed back unless includeMacro is set; use whalegenie_patch_macro to change one thing in a long macro instead of sending it again.")]
    public Task<CallToolResult> PutMacro(
        [Description("The draft id.")] string draftId,
        [Description("The macro as a JSON object: name, trigger fields, arguments, returns and steps.")] JsonObject macro,
        [Description("Also returns the whole macro. Off by default, because it is what the caller just sent.")] bool includeMacro = false)
        => McpErrors.RunAsync(async () =>
        {
            var draft = drafts.Get(draftId);
            var put = await Ui(() => draft.Put(macro));
            var written = put.Macro;
            var validation = await Ui(() => McpDraftStore.Validate(draft));
            var outline = await Ui(() => McpOutline.Of(written));
            return includeMacro
                ? McpErrors.Ok(new
                {
                    name = written.Name,
                    steps = written.Steps.Count,
                    outline,
                    renamed = Renamed(put.Renamed),
                    validation = Shape(validation),
                    macro = await Ui(() => written.ToJson(includeTriggers: true)),
                })
                : McpErrors.Ok(new
                {
                    name = written.Name,
                    steps = written.Steps.Count,
                    outline,
                    renamed = Renamed(put.Renamed),
                    validation = Shape(validation),
                });
        });

    [McpServerTool(Name = "whalegenie_patch_macro", Destructive = true,
            Title = "Change one step"),
     Description("Changes a macro without sending it again: one operation per change, applied in order. set changes one parameter's value, whole records included (a record with a field missing is refused); setRow changes the columns of one row of a table, addRow adds one, removeRow takes one out (a table is a call's arguments, a step's results, the places a search looks); insert adds a step to a list; remove takes one out; move puts a step in another place. A value is a shape, not a line of text: a string is the text it is, a number is a number, and a name or an operation is sent as the object a macro file writes, such as {\"name\":\"count\"} or {\"op\":\"+\",\"left\":1,\"right\":2}. A patch is one write or none — if any operation does not fit, nothing changes and the refusal says which operation and what that list accepts.")]
    public Task<CallToolResult> PatchMacro(
        [Description("The draft id.")] string draftId,
        [Description("The macro name.")] string name,
        [Description("The operations: {op:\"set\",stepId,param,value}, {op:\"setRow\"|\"addRow\"|\"removeRow\",stepId,param,values|match}, {op:\"insert\",parent,index,step}, {op:\"remove\",stepId} or {op:\"move\",stepId,parent,index}. A value and a table cell are shapes, not lines: send a string for words, a JSON number for a number, or an object such as {\"name\":\"count\"} for a reference. A parent is a list path such as 0.then, or empty for the top level. An operation that changes a step inside a submacro also carries \"macro\" with the label the outline prints for that step: \"sub:<name>\", or a path such as \"sub:a/sub:b\" for one written inside another. Without it the operation means the macro's own steps.")] JsonArray operations,
        [Description("Also returns the whole macro. Off by default, because the outline already says what it holds.")] bool includeMacro = false,
        [Description("Returns the outline of every step after the patch. On by default; turn it off when only the changed list is needed.")] bool includeOutline = true)
        => McpErrors.RunAsync(async () =>
        {
            var draft = drafts.Get(draftId);
            var macro = await Ui(() => draft.Find(name));
            if (macro is null)
            {
                return McpErrors.Fail(McpErrors.NoSuchMacro, $"There is no macro named \"{name}\".",
                    "Call whalegenie_list_macros to see the names this draft holds.",
                    retryable: true, details: new { name });
            }

            var outcome = await Ui(() => McpPatches.Apply(macro, operations));
            if (outcome.Refused is { } refusal)
            {
                return McpErrors.Fail(refusal.Code, refusal.Message, refusal.Hint,
                    refusal.Retryable, refusal.Details);
            }

            var patched = outcome.Macro!;
            var stored = await Ui(() => draft.Put(patched.ToJson()));

            var written = await Ui(() => draft.Find(name)) ?? patched;
            var validation = await Ui(() => McpDraftStore.Validate(draft));
            var outline = await Ui(() => McpOutline.Of(written));
            var changed = outcome.Touches
                .Select(touch => new
                {
                    op = touch.Op,
                    stepId = touch.Step?.Id ?? string.Empty,
                    type = touch.Step?.Type ?? string.Empty,

                    // A change to the macro itself has no step behind it; the field says what moved.
                    field = touch.Field ?? string.Empty,
                })
                .ToList();
            // What a patch answers with is decided by what the caller asked for: the outline of a
            // forty-step macro is most of the answer, and a client that only wants to know what
            // changed should not pay for it on every call.
            var answer = new Dictionary<string, object?>
            {
                ["name"] = written.Name,
                ["steps"] = written.Steps.Count,
                ["changed"] = changed,
                ["renamed"] = Renamed(stored.Renamed),
                ["validation"] = Shape(validation),
            };

            if (includeOutline)
            {
                answer["outline"] = outline;
            }

            if (includeMacro)
            {
                answer["macro"] = await Ui(() => written.ToJson(includeTriggers: true));
            }

            return McpErrors.Ok(answer);
        });

    [McpServerTool(Name = "whalegenie_delete_macro", Destructive = true,
            Title = "Delete one macro"),
     Description("Deletes one macro from a draft; nothing is saved until whalegenie_save_project.")]
    public Task<CallToolResult> DeleteMacro(
        [Description("The draft id.")] string draftId,
        [Description("The macro name.")] string name)
        => McpErrors.RunAsync(async () => McpErrors.Ok(await Ui(() =>
        {
            var draft = drafts.Get(draftId);
            draft.Delete(name);
            return new { deleted = name, macroCount = draft.MacroSnapshot().Count };
        })));

    [McpServerTool(Name = "whalegenie_set_globals", Destructive = true,
            Title = "Replace the global variables"),
     Description("Replaces the global variable declarations in a draft. Each entry is {name, type, default, description}.")]
    public Task<CallToolResult> SetGlobals(
        [Description("The draft id.")] string draftId,
        [Description("The declarations as a JSON array of {name,type,default,description} objects.")] JsonArray globals)
        => McpErrors.RunAsync(async () =>
        {
            var draft = drafts.Get(draftId);
            await Ui(() =>
            {
                draft.SetGlobals(globals);
                return true;
            });
            return McpErrors.Ok(new { count = draft.Globals.Count });
        });
}
