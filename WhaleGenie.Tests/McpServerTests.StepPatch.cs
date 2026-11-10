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

// Changing one step without sending the whole project back.
public partial class McpServerTests
{
    [Fact]
    public void A_client_can_change_one_step_without_resending_the_macro()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-patch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "project.wgmacro");
                var viewModel = new MainViewModel();
                viewModel.AddMacro(new MacroItem { Name = "原宏" });
                viewModel.CurrentPath = path;
                viewModel.SavePackage(path);

                await using var loop = McpLoopback.Start(viewModel);
                await using var client = await loop.ConnectAsync();
                var draft = JsonDocument.Parse(await Call(client, "whalegenie_open_project",
                    new Dictionary<string, object?>())).RootElement.GetProperty("draftId").GetString()!;

                var put = JsonDocument.Parse(await Call(client, "whalegenie_put_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["macro"] = JsonNode.Parse("""
                            {"name":"补丁宏","steps":[
                              {"type":"control.delay","params":{"ms":"100"}},
                              {"type":"control.repeat","params":{"times":"2","intervalMs":"0","body":[]}}]}
                            """),
                    })).RootElement;

                // The outline is what a patch targets: one line per step, with the id a run reports.
                var outline = put.GetProperty("outline").EnumerateArray().ToArray();
                Assert.Equal(2, outline.Length);
                Assert.All(outline, step => Assert.Equal(4, step.GetProperty("id").GetString()!.Length));
                Assert.Equal(2, outline.Select(step => step.GetProperty("id").GetString()).Distinct().Count());

                // The macro itself is not echoed back: it is what the caller just sent.
                Assert.False(put.TryGetProperty("macro", out _));

                var first = outline[0].GetProperty("id").GetString()!;
                var patched = JsonDocument.Parse(await Call(client, "whalegenie_patch_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["name"] = "补丁宏",
                        ["operations"] = new JsonArray(new JsonObject
                        {
                            ["op"] = "set",
                            ["stepId"] = first,
                            ["param"] = "ms",
                            ["value"] = "250",
                        }),
                    })).RootElement;
                Assert.Equal(2, patched.GetProperty("steps").GetInt32());
                Assert.Equal("set", patched.GetProperty("changed")[0].GetProperty("op").GetString());

                var written = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                    new Dictionary<string, object?> { ["draftId"] = draft, ["name"] = "补丁宏" }))
                    .RootElement;
                Assert.Equal("250", written.GetProperty("steps")[0].GetProperty("params")
                    .GetProperty("ms").ToString());

                // A case outside a switch is refused, and the refusal is the whole story: the macro
                // is exactly as it was, because a patch is one write or none.
                var refused = await client.CallToolAsync("whalegenie_patch_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["name"] = "补丁宏",
                        ["operations"] = new JsonArray(new JsonObject
                        {
                            ["op"] = "insert",
                            ["step"] = new JsonObject
                            {
                                ["type"] = "control.case",
                                ["params"] = new JsonObject { ["values"] = "1" },
                            },
                        }),
                    });
                Assert.True(refused.IsError == true);
                var error = JsonDocument.Parse(refused.Content.OfType<TextContentBlock>().First().Text)
                    .RootElement.GetProperty("error");
                Assert.Equal(McpErrors.BadPlacement, error.GetProperty("code").GetString());

                var unchanged = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                    new Dictionary<string, object?> { ["draftId"] = draft, ["name"] = "补丁宏" }))
                    .RootElement;
                Assert.Equal(2, unchanged.GetProperty("steps").GetArrayLength());

                // A key that is not in the catalogue is a typo, and the code says so: a client that
                // branches on the code has to look this up rather than fix a placement.
                var mistyped = await client.CallToolAsync("whalegenie_patch_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["name"] = "补丁宏",
                        ["operations"] = new JsonArray(new JsonObject
                        {
                            ["op"] = "insert",
                            ["step"] = new JsonObject { ["type"] = "control.delai" },
                        }),
                    });
                var typo = JsonDocument.Parse(mistyped.Content.OfType<TextContentBlock>().First().Text)
                    .RootElement.GetProperty("error");
                Assert.Equal(McpErrors.UnknownAction, typo.GetProperty("code").GetString());

                // What a call passes is a table now, so changing one argument is a row operation.
                // Without these the client would be back to resending the whole macro — the cost
                // the patch tool exists to remove — for a table rather than a single value.
                await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = JsonNode.Parse("""{"name":"子","steps":[]}"""),
                });
                var withCall = JsonDocument.Parse(await Call(client, "whalegenie_put_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["macro"] = JsonNode.Parse("""
                            {"name":"补丁宏","steps":[
                              {"type":"control.runMacro","params":{"macro":"子","arguments":[
                                 {"name":"次数","value":"1"},
                                 {"name":"标签","value":"甲"}]}}]}
                            """),
                    })).RootElement;
                var callId = withCall.GetProperty("outline")[0].GetProperty("id").GetString()!;

                var rows = JsonDocument.Parse(await Call(client, "whalegenie_patch_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["name"] = "补丁宏",
                        ["operations"] = new JsonArray(
                            new JsonObject
                            {
                                ["op"] = "addRow",
                                ["stepId"] = callId,
                                ["param"] = "arguments",
                                ["values"] = new JsonObject { ["name"] = "备注", ["value"] = "乙" },
                            },
                            new JsonObject
                            {
                                ["op"] = "setRow",
                                ["stepId"] = callId,
                                ["param"] = "arguments",
                                ["match"] = new JsonObject { ["name"] = "次数" },
                                ["values"] = new JsonObject { ["value"] = "$n" },
                            },
                            new JsonObject
                            {
                                ["op"] = "removeRow",
                                ["stepId"] = callId,
                                ["param"] = "arguments",
                                ["match"] = new JsonObject { ["name"] = "标签" },
                            }),
                    })).RootElement;
                Assert.Equal(["addRow", "setRow", "removeRow"],
                    rows.GetProperty("changed").EnumerateArray()
                        .Select(change => change.GetProperty("op").GetString() ?? string.Empty)
                        .ToArray());

                var table = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                    new Dictionary<string, object?> { ["draftId"] = draft, ["name"] = "补丁宏" }))
                    .RootElement.GetProperty("steps")[0].GetProperty("params")
                    .GetProperty("arguments").EnumerateArray().ToArray();
                Assert.Equal(2, table.Length);
                Assert.Equal("次数", table[0].GetProperty("name").GetString());
                Assert.Equal("$n", table[0].GetProperty("value").GetString());
                Assert.Equal("备注", table[1].GetProperty("name").GetString());

                // A row is named by what it says, so two rows saying the same thing can be neither
                // changed nor removed: which one was meant would be a guess.
                var ambiguous = await client.CallToolAsync("whalegenie_patch_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["name"] = "补丁宏",
                        ["operations"] = new JsonArray(new JsonObject
                        {
                            ["op"] = "removeRow",
                            ["stepId"] = callId,
                            ["param"] = "arguments",
                            ["match"] = new JsonObject { ["value"] = string.Empty },
                        }),
                    });
                Assert.True(ambiguous.IsError == true);
                Assert.Equal(McpErrors.BadPatch,
                    JsonDocument.Parse(ambiguous.Content.OfType<TextContentBlock>().First().Text)
                        .RootElement.GetProperty("error").GetProperty("code").GetString());

                // A column the catalogue does not declare is refused: nothing would read it, and a
                // value nobody reads looks like the step ignoring what it was told.
                var unknownColumn = await client.CallToolAsync("whalegenie_patch_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["name"] = "补丁宏",
                        ["operations"] = new JsonArray(new JsonObject
                        {
                            ["op"] = "addRow",
                            ["stepId"] = callId,
                            ["param"] = "arguments",
                            ["values"] = new JsonObject { ["nmae"] = "打错了" },
                        }),
                    });
                Assert.Equal(McpErrors.BadArgument,
                    JsonDocument.Parse(unknownColumn.Content.OfType<TextContentBlock>().First().Text)
                        .RootElement.GetProperty("error").GetProperty("code").GetString());

                // A window rule is the one record written as an object in the file, so it is the one
                // that can be sent whole. A rectangle is written as one value instead, and an object
                // there would be a shape the run reads as something else — so it is refused with the
                // way that field is actually written.
                var withWindow = JsonDocument.Parse(await Call(client, "whalegenie_patch_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["name"] = "补丁宏",
                        ["operations"] = new JsonArray(new JsonObject
                        {
                            ["op"] = "insert",
                            ["index"] = 0,
                            ["step"] = new JsonObject
                            {
                                ["type"] = "window.activate",
                                ["params"] = new JsonObject
                                {
                                    ["window"] = new JsonObject
                                    {
                                        ["title"] = "记事本",
                                        ["how"] = "contains",
                                    },
                                },
                            },
                        }),
                    })).RootElement;
                var windowId = withWindow.GetProperty("outline")[0].GetProperty("id").GetString()!;

                var rule = JsonDocument.Parse(await Call(client, "whalegenie_patch_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["name"] = "补丁宏",
                        ["operations"] = new JsonArray(new JsonObject
                        {
                            ["op"] = "set",
                            ["stepId"] = windowId,
                            ["param"] = "window",
                            ["value"] = new JsonObject
                            {
                                ["title"] = "计算器",
                                ["how"] = "equal",
                            },
                        }),
                    })).RootElement;
                Assert.Equal(1, rule.GetProperty("changed").GetArrayLength());

                // A field the window rule does not have is refused with the ones it has.
                var wrongField = await client.CallToolAsync("whalegenie_patch_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["name"] = "补丁宏",
                        ["operations"] = new JsonArray(new JsonObject
                        {
                            ["op"] = "set",
                            ["stepId"] = windowId,
                            ["param"] = "window",
                            ["value"] = new JsonObject { ["titel"] = "打错了" },
                        }),
                    });
                Assert.True(wrongField.IsError == true);
                Assert.Equal(McpErrors.BadArgument,
                    JsonDocument.Parse(wrongField.Content.OfType<TextContentBlock>().First().Text)
                        .RootElement.GetProperty("error").GetProperty("code").GetString());

                // The macro comes back when it is asked for, which is the one time it is wanted.
                var echoed = JsonDocument.Parse(await Call(client, "whalegenie_put_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["macro"] = JsonNode.Parse("""{"name":"补丁宏","steps":[]}"""),
                        ["includeMacro"] = true,
                    })).RootElement;
                Assert.True(echoed.TryGetProperty("macro", out _));

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
