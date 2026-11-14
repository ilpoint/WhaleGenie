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
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

// What a draft would change, and the drafts a project has open.
public partial class McpServerTests
{
    /// <summary>
    /// Applying writes into the open project, so a client and the person want to see what would change
    /// before it does: which macros come, go or change, which of their steps, and which globals move.
    /// </summary>
    [Fact]
    public void A_draft_says_what_applying_it_would_change()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-preview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "project.wgmacro");
                var viewModel = new MainViewModel();
                var existing = new MacroItem { Name = "原宏" };
                existing.Steps.Add(new MacroStep
                {
                    Id = "aa11",
                    Type = "control.delay",
                    Parameters =
                    [
                        new StepParameter
                        {
                            Name = "ms",
                            Kind = ActionParameterKind.Number,
                            Written = Formula.Number(1000),
                        },
                    ],
                });
                viewModel.AddMacro(existing);
                viewModel.CurrentPath = path;
                viewModel.SavePackage(path);

                await using var loop = McpLoopback.Start(viewModel);
                await using var client = await loop.ConnectAsync();

                var draft = JsonDocument.Parse(await Call(client, "whalegenie_open_project",
                    new Dictionary<string, object?>())).RootElement.GetProperty("draftId").GetString()!;

                // A draft nobody has edited has nothing to say.
                var empty = JsonDocument.Parse(await Call(client, "whalegenie_preview_apply",
                    new Dictionary<string, object?> { ["draftId"] = draft })).RootElement;
                Assert.Empty(empty.GetProperty("macros").EnumerateArray());
                Assert.Empty(empty.GetProperty("globals").EnumerateArray());

                // A macro that is not in the project yet is an addition.
                await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = JsonNode.Parse(
                        """{"name":"新宏","steps":[{"type":"control.delay","params":{"ms":1000}}]}"""),
                });

                var added = JsonDocument.Parse(await Call(client, "whalegenie_preview_apply",
                    new Dictionary<string, object?> { ["draftId"] = draft })).RootElement;
                Assert.Equal("added", added.GetProperty("macros").EnumerateArray()
                    .Single(macro => macro.GetProperty("name").GetString() == "新宏")
                    .GetProperty("change").GetString());
                Assert.Equal(1, added.GetProperty("totals").GetProperty("added").GetInt32());

                // A step of a macro that was already there, saying something different.
                await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = JsonNode.Parse(
                        """{"name":"原宏","steps":[{"id":"aa11","type":"control.delay","params":{"ms":2500}}]}"""),
                });

                var changed = JsonDocument.Parse(await Call(client, "whalegenie_preview_apply",
                    new Dictionary<string, object?> { ["draftId"] = draft })).RootElement;
                var macro = changed.GetProperty("macros").EnumerateArray()
                    .Single(entry => entry.GetProperty("name").GetString() == "原宏");
                Assert.Equal("changed", macro.GetProperty("change").GetString());

                var stepChange = macro.GetProperty("changedSteps").EnumerateArray().Single();
                Assert.Equal("aa11", stepChange.GetProperty("step").GetProperty("id").GetString());
                Assert.Contains("ms", stepChange.GetProperty("parameters").EnumerateArray()
                    .Select(name => name.GetString()));

                await loop.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    /// <summary>
    /// The drafts the server is holding, listed with the id to use and whether they have been applied:
    /// the answer to "what is still pending" that used to require remembering the id.
    /// </summary>
    [Fact]
    public void The_drafts_that_are_open_are_listed()
    {
        Ui.RunAsync(async () =>
        {
            var viewModel = new MainViewModel();
            viewModel.AddMacro(new MacroItem { Name = "原宏" });

            await using var loop = McpLoopback.Start(viewModel);
            await using var client = await loop.ConnectAsync();

            var first = JsonDocument.Parse(await Call(client, "whalegenie_open_project",
                new Dictionary<string, object?>())).RootElement.GetProperty("draftId").GetString()!;
            var second = JsonDocument.Parse(await Call(client, "whalegenie_open_project",
                new Dictionary<string, object?>())).RootElement.GetProperty("draftId").GetString()!;

            var listed = JsonDocument.Parse(await Call(client, "whalegenie_list_drafts",
                new Dictionary<string, object?>())).RootElement;
            Assert.Equal(2, listed.GetProperty("total").GetInt32());
            var ids = listed.GetProperty("drafts").EnumerateArray()
                .Select(draft => draft.GetProperty("draftId").GetString()).ToList();
            Assert.Contains(first, ids);
            Assert.Contains(second, ids);
            Assert.All(listed.GetProperty("drafts").EnumerateArray(),
                draft => Assert.False(draft.GetProperty("applied").GetBoolean()));

            await Call(client, "whalegenie_discard",
                new Dictionary<string, object?> { ["draftId"] = first });
            Assert.Equal(1, JsonDocument.Parse(await Call(client, "whalegenie_list_drafts",
                new Dictionary<string, object?>())).RootElement.GetProperty("total").GetInt32());

            await loop.StopAsync();
            return true;
        });
    }
}
