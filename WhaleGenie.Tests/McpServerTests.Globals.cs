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

// The global declarations a project carries, and the paged macro list.
public partial class McpServerTests
{
    /// <summary>
    /// The global declarations can be read back in the shape they are written in, so a client does
    /// not have to replace a list it has never seen.
    /// </summary>
    [Fact]
    public void A_client_can_read_the_global_declarations()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            await Call(client, "whalegenie_set_globals", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["globals"] = JsonNode.Parse("""
                    [{"name":"计数","type":"number","default":"1","description":"多少次"}]
                    """),
            });

            var answer = JsonDocument.Parse(await Call(client, "whalegenie_get_globals",
                new Dictionary<string, object?> { ["draftId"] = draft })).RootElement;
            Assert.Equal(1, answer.GetProperty("count").GetInt32());
            var one = answer.GetProperty("globals")[0];
            Assert.Equal("计数", one.GetProperty("name").GetString());
            Assert.Equal("number", one.GetProperty("type").GetString());
            Assert.Equal("1", one.GetProperty("default").GetString());

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>A long project is paged rather than answered whole, and says whether more is left.</summary>
    [Fact]
    public void The_macro_list_is_paged()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            foreach (var name in new[] { "甲", "乙", "丙" })
            {
                await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = JsonNode.Parse($$"""{"name":"{{name}}","steps":[]}"""),
                });
            }

            var first = JsonDocument.Parse(await Call(client, "whalegenie_list_macros",
                new Dictionary<string, object?> { ["draftId"] = draft, ["limit"] = 2 })).RootElement;
            Assert.Equal(3, first.GetProperty("total").GetInt32());
            Assert.Equal(2, first.GetProperty("macros").GetArrayLength());
            Assert.True(first.GetProperty("hasMore").GetBoolean());

            var last = JsonDocument.Parse(await Call(client, "whalegenie_list_macros",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["offset"] = 2,
                    ["limit"] = 2,
                })).RootElement;
            Assert.Equal(1, last.GetProperty("macros").GetArrayLength());
            Assert.False(last.GetProperty("hasMore").GetBoolean());

            await loop.StopAsync();
            return true;
        });
    }
}
