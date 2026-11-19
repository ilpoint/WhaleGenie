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

// Patches that change the outline, the macro itself, the input door and the declarations.
public partial class McpServerTests
{
    /// <summary>A patch can leave the outline out, which is most of the answer for a long macro.</summary>
    [Fact]
    public void A_patch_can_leave_the_outline_out()
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
                    ["macro"] = JsonNode.Parse("""{"name":"短","steps":[{"type":"control.delay","params":{"ms":"1"}}]}"""),
                })).RootElement;
            var id = put.GetProperty("outline")[0].GetProperty("id").GetString()!;

            var small = JsonDocument.Parse(await Call(client, "whalegenie_patch_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "短",
                    ["operations"] = JsonNode.Parse(
                        $$"""[{"op":"set","stepId":"{{id}}","param":"ms","value":7}]"""),
                    ["includeOutline"] = false,
                })).RootElement;
            Assert.False(small.TryGetProperty("outline", out _));
            Assert.Equal(1, small.GetProperty("changed").GetArrayLength());

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A step's own settings are reachable without resending the macro: its note, whether it is
    /// skipped, its timeout and its retries.
    /// </summary>
    [Fact]
    public void A_patch_can_change_what_a_step_says_about_itself()
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
                    ["macro"] = JsonNode.Parse("""{"name":"设置","steps":[{"type":"control.delay","params":{"ms":"1"}}]}"""),
                })).RootElement;
            var id = put.GetProperty("outline")[0].GetProperty("id").GetString()!;

            var answer = JsonDocument.Parse(await Wrote(client, "whalegenie_patch_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "设置",
                    ["operations"] = JsonNode.Parse($$"""
                        [{"op":"meta","stepId":"{{id}}","comment":"等一下","enabled":false,
                          "timeoutMs":2500,"retryCount":2,"retryDelayMs":300,"onError":"continue"}]
                        """),
                })).RootElement;

            var touched = answer.GetProperty("changed")[0];
            Assert.Equal("meta", touched.GetProperty("op").GetString());
            Assert.Equal(id, touched.GetProperty("stepId").GetString());

            var step = JsonDocument.Parse(await Call(client, "whalegenie_get_step",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "设置",
                    ["stepId"] = id,
                })).RootElement.GetProperty("step").GetProperty("meta");
            Assert.Equal("等一下", step.GetProperty("comment").GetString());
            Assert.False(step.GetProperty("enabled").GetBoolean());
            Assert.Equal(2500, step.GetProperty("timeoutMs").GetInt32());
            Assert.Equal(2, step.GetProperty("retryCount").GetInt32());
            Assert.Equal("continue", step.GetProperty("onError").GetString());

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// The macro's own settings are reachable the same way, and changing one leaves the others
    /// alone — which resending the whole macro never did.
    /// </summary>
    [Fact]
    public void A_patch_can_change_the_macro_itself()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = JsonNode.Parse("""{"name":"宏设置","description":"原来的","steps":[]}"""),
            });

            var answer = JsonDocument.Parse(await Wrote(client, "whalegenie_patch_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "宏设置",
                    ["operations"] = JsonNode.Parse(
                        """[{"op":"macro","description":"改过的","delayScale":0.5,"toggleKey":"F7"}]"""),
                })).RootElement;
            Assert.Equal(3, answer.GetProperty("changed").GetArrayLength());
            Assert.Equal("description", answer.GetProperty("changed")[0].GetProperty("field").GetString());

            var macro = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "宏设置",
                })).RootElement;
            Assert.Equal("改过的", macro.GetProperty("description").GetString());
            Assert.Equal(0.5, macro.GetProperty("delayScale").GetDouble());
            Assert.Equal("F7", macro.GetProperty("toggleKey").GetString());

            // What starts a macro is not a field any more: the trigger table is the one source, so
            // a field patch that tries to set one is refused rather than silently ignored.
            var refused = await client.CallToolAsync("whalegenie_patch_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "宏设置",
                    ["operations"] = JsonNode.Parse("""[{"op":"macro","triggerMode":"Timer"}]"""),
                });
            Assert.True(refused.IsError);

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// 独占输入不是宏的字段：补丁里带上它会被拒绝，并说明该用「独占输入」块 —— 悄悄忽略比拒绝
    /// 更糟，调用方会以为自己改到了。
    /// </summary>
    [Fact]
    public void A_patch_cannot_set_the_input_door_on_one_macro()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = JsonNode.Parse("""{"name":"互斥","steps":[]}"""),
            });

            var refused = await client.CallToolAsync("whalegenie_patch_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "互斥",
                    ["operations"] = JsonNode.Parse("""[{"op":"macro","exclusiveInput":true}]"""),
                });
            Assert.True(refused.IsError == true);

            var said = refused.Content.OfType<TextContentBlock>().First().Text;
            Assert.Equal(McpErrors.BadArgument,
                JsonDocument.Parse(said).RootElement.GetProperty("error").GetProperty("code")
                    .GetString());
            Assert.Contains("exclusiveInput", said, StringComparison.Ordinal);
            Assert.Contains("control.holdInput", said, StringComparison.Ordinal);

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// 每宏限速与启用/停用键也走补丁：数字写进去、null 交回「跟随程序设置」，键写成触发键同一种
    /// 写法，读回来还是那两个值。
    /// </summary>
    [Fact]
    public void A_patch_can_set_the_input_limit_and_the_switch_key_of_one_macro()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = JsonNode.Parse("""{"name":"专属","steps":[]}"""),
            });

            await Wrote(client, "whalegenie_patch_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["name"] = "专属",
                ["operations"] = JsonNode.Parse(
                    """[{"op":"macro","inputRate":300,"toggleKey":"F9"}]"""),
            });

            var macro = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "专属",
                })).RootElement;
            Assert.Equal(300, macro.GetProperty("inputRate").GetInt32());
            Assert.Equal("F9", macro.GetProperty("toggleKey").GetString());

            await Wrote(client, "whalegenie_patch_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["name"] = "专属",
                ["operations"] = JsonNode.Parse("""[{"op":"macro","inputRate":null}]"""),
            });

            var back = JsonDocument.Parse(await Call(client, "whalegenie_get_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "专属",
                })).RootElement;
            // 交回「跟随程序设置」就是不再写这一格，文件形状与没设过一样。
            Assert.False(back.TryGetProperty("inputRate", out _));

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// What a macro asks a call to fill in, and what it hands back, are reachable without resending
    /// the macro either.
    /// </summary>
    [Fact]
    public void A_patch_can_change_the_declarations()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = JsonNode.Parse("""{"name":"声明","steps":[]}"""),
            });

            await Wrote(client, "whalegenie_patch_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["name"] = "声明",
                ["operations"] = JsonNode.Parse("""
                    [{"op":"declare","kind":"argument","action":"add","name":"数量","type":"Number"},
                     {"op":"declare","kind":"argument","action":"add","name":"标题","description":"要写什么"},
                     {"op":"declare","kind":"result","action":"add","name":"结果","type":"Text"},
                     {"op":"declare","kind":"argument","action":"move","name":"标题","index":0}]
                    """),
            });

            var signature = JsonDocument.Parse(await Call(client, "whalegenie_get_macro_signature",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "声明",
                })).RootElement;
            var arguments = signature.GetProperty("arguments").EnumerateArray().ToList();
            Assert.Equal(2, arguments.Count);
            Assert.Equal("标题", arguments[0].GetProperty("name").GetString());
            Assert.Equal("Number", arguments[1].GetProperty("type").GetString());
            Assert.Equal("结果", signature.GetProperty("results")[0].GetProperty("name").GetString());

            // A name nothing could read back is refused before it reaches the project.
            var refused = await client.CallToolAsync("whalegenie_patch_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "声明",
                    ["operations"] = JsonNode.Parse(
                        """[{"op":"declare","kind":"argument","action":"add","name":"1 bad"}]"""),
                });
            Assert.True(refused.IsError);

            await loop.StopAsync();
            return true;
        });
    }
}
