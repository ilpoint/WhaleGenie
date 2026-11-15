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

// What a draft would do: validating it, previewing the change, applying it, and saving the package.
public sealed partial class McpTools
{
    [McpServerTool(Name = "whalegenie_validate", ReadOnly = true,
            Title = "Check a draft"),
     Description("Checks every macro in a draft against the action catalogue, including hidden actions and each child list's childKeys, and the interface declarations. Blocking problems stop apply; hints never do. Every problem names the macro and, where there is one, the step id it came from.")]
    public Task<CallToolResult> Validate(
        [Description("The draft id.")] string draftId)
        => McpErrors.RunAsync(async () => McpErrors.Ok(
            Shape(await Ui(() => McpDraftStore.Validate(drafts.Get(draftId))))));

    [McpServerTool(Name = "whalegenie_preview_apply", ReadOnly = true,
            Title = "See what apply would change"),
     Description("Says what whalegenie_apply would change, before anything is written: macros that appear, disappear or change (and for a changed one the steps that came, went, moved, or no longer say the same thing), plus the global declarations that move. The validation verdict comes with it, so one call answers whether this draft is worth applying.")]
    public Task<CallToolResult> PreviewApply(
        [Description("The draft id.")] string draftId)
        => McpErrors.RunAsync(async () =>
        {
            var draft = drafts.Get(draftId);
            var validation = await Ui(() => McpDraftStore.Validate(draft));
            var preview = await Ui(() => McpPreview.Of(draft));
            return McpErrors.Ok(new
            {
                draftId = draft.Id,
                path = draft.Basis.Path,
                revision = draft.Basis.Revision,
                applied = draft.Applied,
                ok = validation.Ok && validation.Blocking.Count == 0,
                blocking = validation.Blocking.Count,
                hints = validation.Hints.Count,
                problems = validation.Blocking.Take(5),
                preview.Totals,
                preview.Macros,
                preview.Globals,
            });
        });

    [McpServerTool(Name = "whalegenie_apply", Destructive = true,
            Title = "Apply a draft to the project"),
     Description("Writes a draft back into the open project. It refuses a draft the validator calls blocking, because a macro that cannot run is not worth putting in front of the person; pass force to write it anyway. It does not save the package.")]
    public Task<CallToolResult> Apply(
        [Description("The draft id.")] string draftId,
        [Description("Writes the draft even when validation reports blocking problems.")] bool force = false)
        => McpErrors.RunAsync(async () =>
        {
            if (RefuseWrite() is { } refused)
            {
                return refused;
            }

            var draft = drafts.Get(draftId);
            if (draft.Applied)
            {
                return McpErrors.Fail(McpErrors.AlreadyApplied, "This draft has already been applied.",
                    "Open the project again to get a fresh draft; one draft is written once.");
            }

            var validation = await Ui(() => McpDraftStore.Validate(draft));
            if (!force && validation.Blocking.Count > 0)
            {
                return McpErrors.Fail(McpErrors.DraftHasBlockingProblems,
                    $"The draft has {validation.Blocking.Count} blocking problem(s), so it was not applied.",
                    "Call whalegenie_validate, fix what it names, then apply again. Pass force when the problems are known and wanted.",
                    retryable: true,
                    details: new { problems = validation.Blocking.Take(5), total = validation.Blocking.Count });
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
            return McpErrors.Ok(new
            {
                revision,
                macros = macros.Count,
                changed = Changed(draft),
                hints = validation.Hints.Count,
            });
        });

    [McpServerTool(Name = "whalegenie_save_project", Destructive = true,
            Title = "Save the package"),
     Description("Saves the open project to its current .wgmacro package, after keeping a .bak copy.")]
    public Task<CallToolResult> SaveProject()
        => McpErrors.RunAsync(async () =>
        {
            if (RefuseWrite() is { } refused)
            {
                return refused;
            }

            var path = await project.SaveAsync();
            var status = await project.StatusAsync();

            // Saving writes the project, not a draft: a draft that was never applied is not in the
            // file, and a client that saved instead of applying would otherwise believe it is.
            var open = drafts.Open;
            return open == 0
                ? McpErrors.Ok(new { path, status.Revision, status.Dirty })
                : McpErrors.Ok(new
                {
                    path,
                    status.Revision,
                    status.Dirty,
                    openDrafts = open,
                    hint = "This saved the project, not a draft. Call whalegenie_apply first for draft changes to reach the file.",
                });
        });
}
