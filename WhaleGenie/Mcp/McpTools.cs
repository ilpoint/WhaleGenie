using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>
/// The tools an MCP client sees. They read and write a draft of the open project; the project
/// itself is only touched by apply, and its package only by save. Running is deliberately absent:
/// the person at the machine still presses the run button.
///
/// Every answer is a JSON document and every refusal carries a code from <see cref="McpErrors"/>,
/// so a client can branch on what went wrong instead of reading English back to itself.
/// </summary>
[McpServerToolType]
public sealed class McpTools(McpDraftStore drafts, McpLiveProject project)
{
    private static async Task<T> Ui<T>(Func<T> work) => await Dispatcher.UIThread.InvokeAsync(work);

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
     Description("Describes one action: hidden state, whether it sends input or looks at the screen, whether and how it waits, parameters, childKeys, kinds, defaults, options, reading rules, record fields, number units and ranges, and returns. A required field marked conditional is required only in the modes its hint names, such as the window rule of a background click. A wrong key is refused with the nearest keys as candidates.")]
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
     Description("Describes several actions in one call, which is what writing a macro needs: one round trip for the three or four keys a step list uses. Keys that do not exist come back under missing instead of failing the whole answer.")]
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
     Description("Returns what one macro of the draft declares: its arguments (name, type, required, default, description) and the results it hands back, plus a call template. A control.runMacro or control.startMacro step passes each argument as one row of params.arguments (columns name, value, given) and asks for results in returns rows of key and name; those names come from the called macro, not from the action catalogue, so this is where they are read.")]
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

    [McpServerTool(Name = "whalegenie_open_project",
            Title = "Open a draft"),
     Description("Takes a snapshot of the open project and returns a draft id for editing it. Everything is edited through that id; nothing reaches the window until apply.")]
    public Task<CallToolResult> OpenProject()
        => McpErrors.RunAsync(async () =>
        {
            var basis = await project.SnapshotAsync();
            var draft = drafts.Create(basis);
            return McpErrors.Ok(new
            {
                draftId = draft.Id,
                path = basis.Path,
                revision = basis.Revision,
                macros = basis.Macros.Select(Summary),
            });
        });

    [McpServerTool(Name = "whalegenie_read_picture", ReadOnly = true,
            Title = "Read a picture a macro saved"),
     Description("Returns a PNG that a step saved, as an image. Only files inside the project folder or the log folder are read: this is for looking at what a capture step produced, not for reading any file on the machine.")]
    public Task<CallToolResult> ReadPicture(
        [Description("The path a capture step reported, such as the path result of vision.capture.")] string path)
        => McpErrors.RunAsync(async () =>
        {
            var status = await project.StatusAsync();
            var roots = new List<string> { AppPaths.Logs };
            if (Path.GetDirectoryName(status.Path) is { Length: > 0 } folder)
            {
                roots.Add(folder);
            }

            var full = Path.GetFullPath((path ?? string.Empty).Trim());
            var allowed = roots.Any(root => full.StartsWith(
                Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

            if (!allowed)
            {
                return McpErrors.Fail(McpErrors.BadArgument,
                    "That file is outside the project folder and the log folder.",
                    "Pass the path a capture step reported; only what the project itself wrote is read.");
            }

            if (!full.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                return McpErrors.Fail(McpErrors.BadArgument, $"There is no PNG at \"{full}\".",
                    "A capture step reports the path it wrote; call it again if the file is gone.");
            }

            var bytes = await File.ReadAllBytesAsync(full);
            if (bytes.Length > 8 * 1024 * 1024)
            {
                return McpErrors.Fail(McpErrors.BadArgument, "That picture is larger than 8 MB.",
                    "Capture a smaller region, or read it on the machine instead.");
            }

            return new CallToolResult
            {
                Content =
                [
                    ImageContentBlock.FromBytes(bytes, "image/png"),
                    new TextContentBlock
                    {
                        Text = JsonSerializer.Serialize(new { path = full, bytes = bytes.Length }),
                    },
                ],
            };
        });

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
            var all = drafts.Get(draftId).MacroSnapshot().Select(Summary).ToList();
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
     Description("Returns one macro as the JSON the WhaleGenie package stores.")]
    public Task<CallToolResult> GetMacro(
        [Description("The draft id.")] string draftId,
        [Description("The macro name.")] string name)
        => McpErrors.RunAsync(async () =>
        {
            var macro = await Ui(() => drafts.Get(draftId).Find(name));
            return macro is null
                ? McpErrors.Fail(McpErrors.NoSuchMacro, $"There is no macro named \"{name}\".",
                    "Call whalegenie_list_macros to see the names this draft holds.",
                    retryable: true, details: new { name })
                : McpErrors.Ok(await Ui(() => macro.ToJson(includeTriggers: true)));
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

                // The outer macro first, then each submacro: a step id is unique across the whole
                // document, so the first place it turns up is the place it belongs to.
                var found = Find(macro.Steps, stepId, string.Empty);
                var scope = "main";
                foreach (var sub in macro.SubMacros)
                {
                    if (found is not null)
                    {
                        break;
                    }

                    found = Find(sub.Steps, stepId, string.Empty);
                    scope = "sub:" + sub.Name;
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
            var written = await Ui(() => draft.Put(macro));
            var validation = await Ui(() => McpDraftStore.Validate(draft));
            var outline = await Ui(() => Outline(written));
            return includeMacro
                ? McpErrors.Ok(new
                {
                    name = written.Name,
                    steps = written.Steps.Count,
                    outline,
                    validation = Shape(validation),
                    macro = await Ui(() => written.ToJson(includeTriggers: true)),
                })
                : McpErrors.Ok(new
                {
                    name = written.Name,
                    steps = written.Steps.Count,
                    outline,
                    validation = Shape(validation),
                });
        });

    [McpServerTool(Name = "whalegenie_patch_macro", Destructive = true,
            Title = "Change one step"),
     Description("Changes a macro without sending it again: one operation per change, applied in order. set changes one parameter's value, whole records included (a record with a field missing is refused); setRow changes the columns of one row of a table, addRow adds one, removeRow takes one out (a table is a call's arguments, a step's results, the places a search looks); insert adds a step to a list; remove takes one out; move puts a step in another place. A patch is one write or none — if any operation does not fit, nothing changes and the refusal says which operation and what that list accepts.")]
    public Task<CallToolResult> PatchMacro(
        [Description("The draft id.")] string draftId,
        [Description("The macro name.")] string name,
        [Description("The operations: {op:\"set\",stepId,param,value}, {op:\"insert\",parent,index,step}, {op:\"remove\",stepId} or {op:\"move\",stepId,parent,index}. A parent is a list path such as 0.then, or empty for the top level.")] JsonArray operations,
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
            await Ui(() =>
            {
                draft.Put(patched.ToJson());
                return true;
            });

            var written = await Ui(() => draft.Find(name)) ?? patched;
            var validation = await Ui(() => McpDraftStore.Validate(draft));
            var outline = await Ui(() => Outline(written));
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

    [McpServerTool(Name = "whalegenie_validate", ReadOnly = true,
            Title = "Check a draft"),
     Description("Checks every macro in a draft against the action catalogue, including hidden actions and each child list's childKeys, and the interface declarations. Blocking problems stop apply; hints never do. Every problem names the macro and, where there is one, the step id it came from.")]
    public Task<CallToolResult> Validate(
        [Description("The draft id.")] string draftId)
        => McpErrors.RunAsync(async () => McpErrors.Ok(
            Shape(await Ui(() => McpDraftStore.Validate(drafts.Get(draftId))))));

    [McpServerTool(Name = "whalegenie_apply", Destructive = true,
            Title = "Apply a draft to the project"),
     Description("Writes a draft back into the open project. It refuses a draft the validator calls blocking, because a macro that cannot run is not worth putting in front of the person; pass force to write it anyway. It does not save the package.")]
    public Task<CallToolResult> Apply(
        [Description("The draft id.")] string draftId,
        [Description("Writes the draft even when validation reports blocking problems.")] bool force = false)
        => McpErrors.RunAsync(async () =>
        {
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

    [McpServerTool(Name = "whalegenie_discard", Destructive = true,
            Title = "Discard a draft"),
     Description("Drops a draft. Applied changes already in the project stay there and can still be saved by the person.")]
    public Task<CallToolResult> Discard(
        [Description("The draft id.")] string draftId)
    {
        if (!drafts.Remove(draftId))
        {
            return Task.FromResult(McpErrors.Fail(McpErrors.DraftNotFound,
                $"There is no draft \"{draftId}\" to discard.",
                "Call whalegenie_open_project for a new one; a draft is also dropped when the server stops.",
                retryable: true, details: new { draftId }));
        }

        return Task.FromResult(McpErrors.Ok(new { discarded = draftId }));
    }

    [McpServerTool(Name = "whalegenie_list_runs", ReadOnly = true,
            Title = "List the last runs"),
     Description("Lists the last runs WhaleGenie performed, newest first, with the total. Running itself is done by the person, not by MCP.")]
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
        var run = string.IsNullOrWhiteSpace(runId)
            ? RunLogStore.Latest()
            : RunLogStore.Find(runId);
        if (run is null)
        {
            return Task.FromResult(McpErrors.Fail(McpErrors.NoSuchRun, "There is no such run.",
                "Call whalegenie_list_runs first; it lists what the person has run.",
                retryable: true));
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

    /// <summary>The validation answer as a client reads it: what blocks, what is only worth knowing.</summary>
    private static object Shape(McpDraftValidation validation) => new
    {
        ok = validation.Ok,
        counts = new { blocking = validation.Blocking.Count, hints = validation.Hints.Count },
        blocking = validation.Blocking,
        hints = validation.Hints,
    };

    /// <summary>
    /// What a macro asks a call to fill in and what it may hand back, with a skeleton a client can
    /// copy into a call step. Both are rows: an argument row names the argument and gives its
    /// value, a result row names the result and says where it lands — facts that live in the called
    /// macro, which is why no action entry can state them.
    /// </summary>
    private static object Signature(MacroItem macro)
    {
        var call = macro.Callable;
        var arguments = CallableSignature.Arguments(call)
            .Select(argument => new
            {
                name = argument.Name,
                type = argument.Type.ToString(),
                required = argument.Required,
                @default = argument.Default,
                description = argument.Description,
                fields = argument.Fields
                    .Select(field => new { name = field.Name, type = field.Type.ToString() })
                    .ToList(),
            })
            .ToList();

        var results = CallableSignature.Results(call)
            .Select(result => new
            {
                name = result.Name,
                type = result.Type.ToString(),
                description = result.Description,
                fields = result.Fields
                    .Select(field => new { name = field.Name, type = field.Type.ToString() })
                    .ToList(),
            })
            .ToList();

        // Only the arguments a call has to fill in are in the skeleton: leaving an optional one out
        // is how its declared default applies, and writing it down empty would say something else.
        var template = new JsonObject
        {
            [FieldNames.Macro] = macro.Name.Trim(),
            [FieldNames.Arguments] = new JsonArray(
                [.. arguments
                    .Where(argument => argument.required)
                    .Select(argument => new JsonObject
                    {
                        [FieldNames.Name] = argument.name,
                        [FieldNames.Value] = string.Empty,
                    })]),
            ["returns"] = new JsonArray([.. results.Select(result => new JsonObject
            {
                [FieldNames.Key] = result.name,
                [FieldNames.Name] = result.name,
            })]),
        };

        return new
        {
            name = macro.Name.Trim(),
            arguments,
            results,
            argumentColumns = new[] { FieldNames.Name, FieldNames.Value, FieldNames.Given },
            callTemplate = template,
            notes = new[]
            {
                "A call step names the macro in params.macro and passes its arguments in params.arguments, one row per argument: name is the name the called macro declared, value is what goes into it, and given says the empty value is meant rather than left out.",
                "An argument with no row takes its declared default; a required one with no row fails the call with Run.MissingArgument before the called macro runs.",
                "To pass an empty value instead of the default, write the row with an empty value and given true. An empty value without given means the argument was left out.",
                "Results are asked for in params.returns, one row per value: key is the name the called macro declared, name is the local variable it lands in.",
                "control.runMacro waits for the called macro; control.startMacro gives a run handle whose results exist only after control.waitMacro.",
            },
        };
    }

    /// <summary>
    /// What the macro holds now, one line per step: its id, its action and where it sits. The path
    /// is what an insert or a move names as its parent, so the next step can be placed without
    /// reading the whole macro back. Submacros are left out: they are written with put_macro.
    /// </summary>
    /// <summary>
    /// Every step of a macro, in the order it runs, with the macro each one belongs to. The submacros
    /// are listed after the outer steps rather than inside them: they are separate documents, and a
    /// path is only meaningful inside the macro it was read from.
    /// </summary>
    private static IReadOnlyList<object> Outline(MacroItem macro)
        =>
        [
            .. Walk(macro.Steps, string.Empty, "main"),
            .. macro.SubMacros.SelectMany(sub => Walk(sub.Steps, string.Empty, "sub:" + sub.Name)),
        ];

    private static IEnumerable<object> Walk(IReadOnlyList<MacroStep> steps, string prefix, string scope)
    {
        for (var at = 0; at < steps.Count; at++)
        {
            var step = steps[at];
            var path = prefix.Length == 0
                ? at.ToString(CultureInfo.InvariantCulture)
                : $"{prefix}.{at.ToString(CultureInfo.InvariantCulture)}";
            yield return new { id = step.Id, type = step.Type, path, macro = scope };

            foreach (var parameter in step.StepLists)
            {
                foreach (var child in Walk(parameter.Steps, $"{path}.{parameter.Name}", scope))
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>Where a step sits: the step itself and the path an insert or a move would name.</summary>
    private static (MacroStep Step, string Path)? Find(IReadOnlyList<MacroStep> steps, string id, string prefix)
    {
        for (var at = 0; at < steps.Count; at++)
        {
            var step = steps[at];
            var path = prefix.Length == 0
                ? at.ToString(CultureInfo.InvariantCulture)
                : $"{prefix}.{at.ToString(CultureInfo.InvariantCulture)}";
            if (string.Equals(step.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return (step, path);
            }

            foreach (var parameter in step.StepLists)
            {
                if (Find(parameter.Steps, id, $"{path}.{parameter.Name}") is { } child)
                {
                    return child;
                }
            }
        }

        return null;
    }

    /// <summary>What validation says about one step, both what blocks and what is only worth reading.</summary>
    private static IReadOnlyList<object> Problems(McpDraft draft, string macro, string stepId)
    {
        var validation = McpDraftStore.Validate(draft);
        return
        [
            .. validation.Blocking
                .Where(problem => problem.Macro == macro && problem.StepId == stepId)
                .Select(problem => (object)new { kind = "blocking", problem.Code, problem.Message }),
            .. validation.Hints
                .Where(problem => problem.Macro == macro && problem.StepId == stepId)
                .Select(problem => (object)new { kind = "hint", problem.Code, problem.Message }),
        ];
    }

    /// <summary>
    /// The macros this draft moved, named against the snapshot it came from. Saying what changed is
    /// cheaper for a reader than diffing two macro dumps, and it is what "what did you do" means.
    /// </summary>
    private static IReadOnlyList<string> Changed(McpDraft draft)
    {
        var before = draft.Basis.Macros.ToDictionary(
            macro => macro.Name.Trim(), macro => macro.ToJson().ToJsonString(), StringComparer.OrdinalIgnoreCase);
        var after = draft.MacroSnapshot().ToDictionary(
            macro => macro.Name.Trim(), macro => macro.ToJson().ToJsonString(), StringComparer.OrdinalIgnoreCase);

        var changed = new List<string>();
        foreach (var (name, json) in after)
        {
            if (!before.TryGetValue(name, out var was) || !string.Equals(was, json, StringComparison.Ordinal))
            {
                changed.Add(name);
            }
        }

        changed.AddRange(before.Keys.Where(name => !after.ContainsKey(name)).Select(name => "-" + name));
        return changed;
    }

    /// <summary>
    /// One macro in a list: what it is called, how long it is, and what a call to it would have to
    /// know. The names are here rather than behind another call because picking which macro to call
    /// is exactly when a client needs them — opening every macro to find out would be one call each.
    /// </summary>
    private static object Summary(MacroItem macro)
    {
        var call = macro.Callable;
        return new
        {
            name = macro.Name,
            steps = macro.Steps.Count,
            arguments = CallableSignature.Arguments(call)
                .Select(argument => new
                {
                    name = argument.Name,
                    type = argument.Type.ToString(),
                    required = argument.Required,
                })
                .ToList(),
            results = CallableSignature.Results(call)
                .Select(result => new { name = result.Name, type = result.Type.ToString() })
                .ToList(),
        };
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
