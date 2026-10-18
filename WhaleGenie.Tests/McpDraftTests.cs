using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using ModelContextProtocol;
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
                var draft = drafts.Create(basis);
                draft.Put(new JsonObject
                {
                    ["name"] = "新宏",
                    ["steps"] = new JsonArray(),
                });

                var validation = McpDraftStore.Validate(draft);
                Assert.False(validation.HasErrors);

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
    public void A_draft_is_refused_after_the_project_moves()
    {
        Ui.RunAsync(async () =>
        {
            var project = new MainViewModel();
            project.AddMacro(new MacroItem { Name = "原宏" });
            var live = new McpLiveProject(project);
            var basis = await live.SnapshotAsync();
            var draft = new McpDraftStore().Create(basis);

            project.AddMacro(new MacroItem { Name = "后来加的" });

            await Assert.ThrowsAsync<McpException>(() =>
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
            await Assert.ThrowsAsync<McpException>(() => live.SaveAsync());
            return true;
        });
    }
}
