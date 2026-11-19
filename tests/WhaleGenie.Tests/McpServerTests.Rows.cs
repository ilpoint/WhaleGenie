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

// Rows of a table and the triggers a project starts on.
public partial class McpServerTests
{
    /// <summary>
    /// The first row of a table that has never had one can be added. The old answer told a client
    /// to use addRow and then refused that very call, which was a loop with no exit.
    /// </summary>
    [Fact]
    public void The_first_row_of_a_table_can_be_added()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            var put = JsonDocument.Parse(await Call(client, "whalegenie_put_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = JsonNode.Parse("""
                        {"name":"调用","steps":[
                          {"type":"control.runMacro","params":{"macro":"别的"}}
                        ]}
                        """),
                })).RootElement;
            var id = put.GetProperty("outline")[0].GetProperty("id").GetString()!;

            var answer = await client.CallToolAsync("whalegenie_patch_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "调用",
                    ["operations"] = JsonNode.Parse(
                        "[{\"op\":\"addRow\",\"stepId\":\"" + id
                        + "\",\"param\":\"arguments\",\"values\":{\"name\":\"数量\",\"value\":\"3\"}}]"),
                });
            Assert.True(answer.IsError is not true,
                answer.Content.OfType<TextContentBlock>().First().Text);

            var macro = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "调用",
                })).RootElement;
            var rows = macro.GetProperty("steps")[0].GetProperty("params")
                .GetProperty("arguments");
            Assert.Equal(1, rows.GetArrayLength());
            Assert.Equal("数量", rows[0].GetProperty("name").GetString());

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// Sending a macro with an empty trigger list takes the old ones out; sending one with no
    /// trigger key leaves them. The two used to mean the same thing, which meant neither worked.
    /// </summary>
    [Fact]
    public void An_empty_trigger_list_clears_the_triggers_and_a_missing_one_keeps_them()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = JsonNode.Parse($$"""
                    {"name":"触发器","steps":[],
                     "triggers":[{"kind":"key","macro":"触发器","enabled":true,
                                  "bindKey":"Ctrl+Alt+1","loopMode":"Toggle","triggerOnce":false}]}
                    """),
            });

            var kept = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "触发器",
                })).RootElement;
            Assert.True(kept.GetProperty("triggers").GetArrayLength() == 1, kept.GetRawText());

            // No trigger key: the project's own trigger stays.
            await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = JsonNode.Parse("""{"name":"触发器","steps":[]}"""),
            });
            var still = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "触发器",
                })).RootElement;
            Assert.Equal(1, still.GetProperty("triggers").GetArrayLength());

            // An empty list: this macro has none.
            await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = JsonNode.Parse("""{"name":"触发器","steps":[],"triggers":[]}"""),
            });
            var gone = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "触发器",
                })).RootElement;
            Assert.Equal(0, gone.GetProperty("triggers").GetArrayLength());

            await loop.StopAsync();
            return true;
        });
    }
}
