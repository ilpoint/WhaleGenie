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

// A sub-macro below the first level, read and patched through its path.
public partial class McpServerTests
{
    /// <summary>
    /// A submacro's steps are in the outline and can be patched like any other: before this, a
    /// project that used one could only be changed by sending the whole macro again.
    /// </summary>
    [Fact]
    public void A_submacro_s_steps_can_be_read_and_patched()
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
                        {"name":"外面","steps":[{"type":"control.delay","params":{"ms":"1"}}],
                         "subMacros":[{"name":"里面","steps":[
                           {"type":"control.delay","params":{"ms":"2"}}]}]}
                        """),
                })).RootElement;

            var rows = put.GetProperty("outline").EnumerateArray().ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal("main", rows[0].GetProperty("macro").GetString());
            Assert.Equal("sub:里面", rows[1].GetProperty("macro").GetString());

            var inner = rows[1].GetProperty("id").GetString()!;
            var step = JsonDocument.Parse(await Call(client, "whalegenie_get_step",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "外面",
                    ["stepId"] = inner,
                })).RootElement;
            Assert.Equal("sub:里面", step.GetProperty("macro").GetString());

            // A parameter inside the submacro, changed where it lives.
            var patched = await Wrote(client, "whalegenie_patch_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["name"] = "外面",
                ["operations"] = JsonNode.Parse(
                    "[{\"op\":\"set\",\"macro\":\"sub:里面\",\"stepId\":\"" + inner
                    + "\",\"param\":\"ms\",\"value\":900}]"),
            });
            Assert.Contains("set", patched, StringComparison.Ordinal);

            var macro = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "外面",
                })).RootElement;
            Assert.Equal("900", macro.GetProperty("subMacros")[0].GetProperty("steps")[0]
                .GetProperty("params").GetProperty("ms").GetRawText());
            Assert.Equal("1", macro.GetProperty("steps")[0].GetProperty("params")
                .GetProperty("ms").GetRawText());

            var missing = await client.CallToolAsync("whalegenie_patch_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "外面",
                    ["operations"] = JsonNode.Parse(
                        "[{\"op\":\"set\",\"macro\":\"sub:没有\",\"stepId\":\"abcd\",\"param\":\"ms\",\"value\":1}]"),
                });
            Assert.True(missing.IsError);
            Assert.Contains("submacro", missing.Content.OfType<TextContentBlock>().First().Text,
                StringComparison.OrdinalIgnoreCase);

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A submacro inside a submacro is named by the path that reaches it, the same label the
    /// outline prints. Reading and patching both take that label, so the second level is not a
    /// place a client can see but not touch.
    /// </summary>
    [Fact]
    public void A_submacro_below_the_first_level_is_reached_by_its_path()
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
                        {"name":"外面","steps":[{"type":"control.delay","params":{"ms":"1"}}],
                         "subMacros":[{"name":"里面","steps":[{"type":"control.delay","params":{"ms":"2"}}],
                           "subMacros":[{"name":"更里面","steps":[
                             {"type":"control.delay","params":{"ms":"3"}}]}]}]}
                        """),
                })).RootElement;

            var rows = put.GetProperty("outline").EnumerateArray().ToList();
            Assert.Equal(3, rows.Count);
            Assert.Equal("sub:里面/sub:更里面", rows[2].GetProperty("macro").GetString());

            var deepest = rows[2].GetProperty("id").GetString()!;
            var step = JsonDocument.Parse(await Call(client, "whalegenie_get_step",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "外面",
                    ["stepId"] = deepest,
                })).RootElement;
            Assert.Equal("sub:里面/sub:更里面", step.GetProperty("macro").GetString());

            await Wrote(client, "whalegenie_patch_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["name"] = "外面",
                ["operations"] = JsonNode.Parse(
                    "[{\"op\":\"set\",\"macro\":\"sub:里面/sub:更里面\",\"stepId\":\"" + deepest
                    + "\",\"param\":\"ms\",\"value\":4}]"),
            });

            var macro = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "外面",
                })).RootElement;
            var nested = macro.GetProperty("subMacros")[0].GetProperty("subMacros")[0]
                .GetProperty("steps")[0].GetProperty("params").GetProperty("ms").GetRawText();
            Assert.Equal("4", nested);

            var missing = await client.CallToolAsync("whalegenie_patch_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "外面",
                    ["operations"] = JsonNode.Parse(
                        "[{\"op\":\"set\",\"macro\":\"sub:里面/sub:没有\",\"stepId\":\"abcd\",\"param\":\"ms\",\"value\":1}]"),
                });
            Assert.True(missing.IsError);

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>Calls a tool that is supposed to work, and shows the refusal when it does not.</summary>
    private static async Task<string> Wrote(McpClient client, string name,
        Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(name, arguments);
        var text = result.Content.OfType<TextContentBlock>().First().Text;
        // The protocol leaves isError unset on success, so it is null rather than false.
        Assert.True(result.IsError is not true, $"isError={result.IsError}; {text}");
        return text;
    }

    /// <summary>Opens a draft and hands back its id.</summary>
    private static async Task<string> Draft(McpClient client)
        => JsonDocument.Parse(await Call(client, "whalegenie_open_project",
            new Dictionary<string, object?>())).RootElement.GetProperty("draftId").GetString()!;
}
