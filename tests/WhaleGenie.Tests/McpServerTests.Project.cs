using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

// One client session over a project: opening it, editing a step, applying a draft and saving.
public partial class McpServerTests
{
    [Fact]
    public void A_client_can_open_edit_apply_and_save_a_project()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-server-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "project.wgmacro");
                var viewModel = new MainViewModel();
                viewModel.AddMacro(new MacroItem { Name = "原宏" });
                viewModel.CurrentPath = path;
                TestProjectFiles.Service.Save(viewModel.Workspace, path);

                await using var loop = McpLoopback.Start(viewModel);
                await using var client = await loop.ConnectAsync();

                var actions = await Call(client, "whalegenie_list_actions",
                    new Dictionary<string, object?> { ["query"] = "delay", ["limit"] = 5 });
                Assert.Contains("control.delay", actions, StringComparison.Ordinal);

                var opened = JsonDocument.Parse(await Call(client, "whalegenie_open_project",
                    new Dictionary<string, object?>()));
                var draft = opened.RootElement.GetProperty("draftId").GetString()!;

                var put = await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = JsonNode.Parse("""{"name":"AI宏","steps":[]}"""),
                });
                Assert.Equal("AI宏", JsonDocument.Parse(put).RootElement
                    .GetProperty("name").GetString());

                var applied = await Call(client, "whalegenie_apply",
                    new Dictionary<string, object?> { ["draftId"] = draft });
                Assert.Contains("revision", applied, StringComparison.Ordinal);
                Assert.Contains(viewModel.Macros, macro => macro.Name == "AI宏");

                await Call(client, "whalegenie_save_project", new Dictionary<string, object?>());
                Assert.True(File.Exists(MacroPackage.BackupPathFor(path)));
                await loop.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    [Fact]
    public void A_client_can_read_the_last_run_for_debugging()
    {
        Ui.RunAsync(async () =>
        {
            RunLogStore.Clear();
            var capture = RunLogStore.Begin("调试宏", "debug");
            capture.Add(new WhaleGenie.Core.Execution.LogEntry(DateTimeOffset.Now,
                WhaleGenie.Core.Execution.LogLevel.Info, 0, string.Empty,
                WhaleGenie.Core.Execution.LogKeys.Finished, []), "完成");
            capture.Finish(new WhaleGenie.Core.Execution.RunResult(
                WhaleGenie.Core.Execution.RunStatus.Completed, string.Empty, string.Empty, 1), "完成");

            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var runs = JsonDocument.Parse(await Call(client, "whalegenie_list_runs",
                new Dictionary<string, object?> { ["limit"] = 5 }));
            Assert.Equal("调试宏", runs.RootElement.GetProperty("runs")[0]
                .GetProperty("macro").GetString());

            var log = JsonDocument.Parse(await Call(client, "whalegenie_get_run_log",
                new Dictionary<string, object?>()));
            Assert.Equal("完成", log.RootElement.GetProperty("lines")[0]
                .GetProperty("text").GetString());
            await loop.StopAsync();
            return true;
        });
    }

    [Fact]
    public void The_tools_declare_their_allowed_values_and_their_titles()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var tools = await client.ListToolsAsync();
            Assert.All(tools, tool =>
                Assert.False(string.IsNullOrWhiteSpace(tool.ProtocolTool.Title), tool.Name));

            // A closed set of values is a set in the schema, not a sentence in a description: a
            // client can then refuse "elbow" before the call instead of after it.
            var click = tools.Single(tool => tool.Name == "whalegenie_mouse_click");
            var button = click.JsonSchema.GetProperty("properties").GetProperty("button");
            Assert.True(button.TryGetProperty("enum", out var allowed), button.GetRawText());
            Assert.Contains("Right", allowed.EnumerateArray().Select(value => value.ToString()));

            // A macro is an object, not JSON written inside a string: nothing is escaped twice.
            var put = tools.Single(tool => tool.Name == "whalegenie_put_macro");
            Assert.Equal("object", put.JsonSchema.GetProperty("properties").GetProperty("macro")
                .GetProperty("type").GetString());

            await loop.StopAsync();
            return true;
        });
    }

    [Fact]
    public void A_draft_the_validator_calls_wrong_is_not_applied()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-gate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "project.wgmacro");
                var viewModel = new MainViewModel();
                viewModel.AddMacro(new MacroItem { Name = "原宏" });
                viewModel.CurrentPath = path;
                TestProjectFiles.Service.Save(viewModel.Workspace, path);

                await using var loop = McpLoopback.Start(viewModel);
                await using var client = await loop.ConnectAsync();

                var draft = JsonDocument.Parse(await Call(client, "whalegenie_open_project",
                    new Dictionary<string, object?>())).RootElement.GetProperty("draftId").GetString()!;
                await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = JsonNode.Parse(
                        """{"name":"错的","steps":[{"type":"control.delai","params":{"ms":"1"}}]}"""),
                });

                var refused = await client.CallToolAsync("whalegenie_apply",
                    new Dictionary<string, object?> { ["draftId"] = draft });
                Assert.True(refused.IsError == true);
                var error = JsonDocument.Parse(refused.Content.OfType<TextContentBlock>().First().Text)
                    .RootElement.GetProperty("error");
                Assert.Equal(McpErrors.DraftHasBlockingProblems, error.GetProperty("code").GetString());
                Assert.DoesNotContain(viewModel.Macros, macro => macro.Name == "错的");

                // The refusal is a gate, not a wall: a person who knows better can still write it.
                var forced = await client.CallToolAsync("whalegenie_apply", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["force"] = true,
                });
                Assert.True(forced.IsError != true);
                Assert.Contains(viewModel.Macros, macro => macro.Name == "错的");

                await loop.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }
}
