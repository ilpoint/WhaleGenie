using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The draft path: a project is snapshotted, edited in memory, checked, applied and only then
/// written. The project revision is what keeps an old draft from overwriting the person's work.
/// </summary>
public class McpDraftTests
{
    [Fact]
    public void A_draft_can_add_a_macro_and_save_it()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "project.wgmacro");
            try
            {
                var project = new MainViewModel();
                project.AddMacro(new MacroItem { Name = "原宏" });
                project.CurrentPath = path;
                project.SavePackage(path);

                var live = new McpLiveProject(project);
                var basis = await live.SnapshotAsync();
                var drafts = new McpDraftStore();
                var draft = drafts.Create(basis).Draft;
                draft.Put(new JsonObject
                {
                    ["name"] = "新宏",
                    ["steps"] = new JsonArray(),
                });

                var validation = McpDraftStore.Validate(draft);
                Assert.True(validation.Ok);

                await live.ApplyAsync(draft.Basis, draft.MacroSnapshot(), draft.Globals);
                await live.SaveAsync();

                var loaded = MacroPackage.Load(path);
                Assert.Contains(loaded.Macros, macro => macro.Name == "新宏");
                Assert.Contains(loaded.Macros, macro => macro.Name == "原宏");
                Assert.True(File.Exists(path + ".bak"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    [Fact]
    public void A_macro_written_by_a_client_gets_step_ids()
    {
        Ui.RunAsync(async () =>
        {
            var project = new MainViewModel();
            var live = new McpLiveProject(project);
            var draft = new McpDraftStore().Create(await live.SnapshotAsync()).Draft;

            // A run reports every step as step.<id>.outcome, so a macro written through MCP has to
            // carry ids the moment it lands, not only after somebody opens it in the editor.
            var macro = draft.Put(new JsonObject
            {
                ["name"] = "无标识",
                ["steps"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "control.delay",
                        ["params"] = new JsonObject { ["ms"] = "1" },
                    },
                    new JsonObject
                    {
                        ["type"] = "control.if",
                        ["params"] = new JsonObject
                        {
                            ["condition"] = "$sys.screenWidth > 1",
                            ["then"] = new JsonArray(new JsonObject
                            {
                                ["type"] = "control.delay",
                                ["params"] = new JsonObject { ["ms"] = "2" },
                            }),
                        },
                    }),
            });

            var ids = Ids(macro.Macro.Steps);
            Assert.Equal(3, ids.Count);
            Assert.All(ids, id => Assert.Equal(4, id.Length));
            Assert.Equal(3, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            return true;
        });
    }

    private static List<string> Ids(IEnumerable<MacroStep> steps)
    {
        var ids = new List<string>();
        foreach (var step in steps)
        {
            ids.Add(step.Id);
            foreach (var parameter in step.StepLists)
            {
                ids.AddRange(Ids(parameter.Steps));
            }
        }

        return ids;
    }

    /// <summary>
    /// One session holds a limited number of drafts. Each one is a deep copy of the project, so a
    /// client that opens one on every turn and never discards one would grow until the server ends.
    /// </summary>
    [Fact]
    public void A_session_holds_a_limited_number_of_drafts()
    {
        Ui.RunAsync(async () =>
        {
            var store = new McpDraftStore();
            var basis = await new McpLiveProject(new MainViewModel()).SnapshotAsync();

            for (var index = 0; index < McpDraftStore.MaxDrafts; index++)
            {
                store.Create(basis);
            }

            var refusal = Assert.Throws<McpFailure>(() => store.Create(basis));
            Assert.Equal(McpErrors.TooManyDrafts, refusal.Code);
            Assert.Equal(McpDraftStore.MaxDrafts, store.Open);
            return true;
        });
    }

    /// <summary>
    /// A draft that was applied has already reached the project, so it is the one let go when the
    /// room is needed — and the answer says which, so a client's own list does not go quietly wrong.
    /// A draft that was never applied is never dropped behind the client's back.
    /// </summary>
    [Fact]
    public void An_applied_draft_makes_room_for_the_next_one()
    {
        Ui.RunAsync(async () =>
        {
            var store = new McpDraftStore();
            var basis = await new McpLiveProject(new MainViewModel()).SnapshotAsync();

            var first = store.Create(basis).Draft;
            for (var index = 1; index < McpDraftStore.MaxDrafts; index++)
            {
                store.Create(basis);
            }

            first.MarkApplied();

            var (opened, dropped) = store.Create(basis);

            Assert.Equal([first.Id], dropped);
            Assert.Equal(McpDraftStore.MaxDrafts, store.Open);
            Assert.DoesNotContain(store.All(), draft => draft.Id == first.Id);
            Assert.Contains(store.All(), draft => draft.Id == opened.Id);
            return true;
        });
    }

    [Fact]
    public void A_draft_is_refused_after_the_project_moves()
    {
        Ui.RunAsync(async () =>
        {
            var project = new MainViewModel();
            project.AddMacro(new MacroItem { Name = "原宏" });
            var live = new McpLiveProject(project);
            var basis = await live.SnapshotAsync();
            var draft = new McpDraftStore().Create(basis).Draft;

            project.AddMacro(new MacroItem { Name = "后来加的" });

            await Assert.ThrowsAsync<McpFailure>(() =>
                live.ApplyAsync(draft.Basis, draft.MacroSnapshot(), draft.Globals));
            return true;
        });
    }

    [Fact]
    public void Saving_without_a_package_path_is_refused()
    {
        Ui.RunAsync(async () =>
        {
            var live = new McpLiveProject(new MainViewModel());
            await Assert.ThrowsAsync<McpFailure>(() => live.SaveAsync());
            return true;
        });
    }

    [Fact]
    public void MCP_validation_reports_a_case_outside_a_switch()
    {
        Ui.RunAsync(async () =>
        {
            var project = new MainViewModel();
            project.AddMacro(new MacroItem { Name = "原宏" });
            var live = new McpLiveProject(project);
            var draft = new McpDraftStore().Create(await live.SnapshotAsync()).Draft;
            var bad = new MacroItem { Name = "非法分支" };
            bad.Steps.Add(new MacroStep
            {
                Type = "control.case",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "values",
                        Kind = ActionParameterKind.Text,
                        Value = "a",
                    },
                    new StepParameter
                    {
                        Name = "body",
                        Kind = ActionParameterKind.Steps,
                    },
                ],
            });
            draft.Put(bad.ToJson());

            var validation = McpDraftStore.Validate(draft);

            Assert.False(validation.Ok);
            Assert.Contains(validation.Blocking, problem =>
                problem.Message.Contains(Strings.Get("Editor.Problem.CaseOutsideSwitch"),
                    StringComparison.Ordinal));
            return true;
        });
    }
}
