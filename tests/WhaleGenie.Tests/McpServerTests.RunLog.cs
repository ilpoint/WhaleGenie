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

// The log of the last run, read from its end, and one step read by id.
public partial class McpServerTests
{
    /// <summary>
    /// A long log comes back from its end. The lines worth reading are the last ones; answering with
    /// the first five hundred shows a run that started fine and hides how it went wrong.
    /// </summary>
    [Fact]
    public void A_long_run_log_comes_back_from_its_end()
    {
        Ui.RunAsync(async () =>
        {
            RunLogStore.Clear();
            var capture = RunLogStore.Begin("长日志", "debug");
            for (var line = 0; line < 600; line++)
            {
                capture.Add(new WhaleGenie.Core.Execution.LogEntry(DateTimeOffset.Now,
                    WhaleGenie.Core.Execution.LogLevel.Info, 0, string.Empty,
                    WhaleGenie.Core.Execution.LogKeys.Finished, []), line.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
            }

            capture.Finish(new WhaleGenie.Core.Execution.RunResult(
                WhaleGenie.Core.Execution.RunStatus.Completed, string.Empty, string.Empty, 600), "完成");

            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var log = JsonDocument.Parse(await Call(client, "whalegenie_get_run_log",
                new Dictionary<string, object?> { ["tail"] = 10 })).RootElement;
            Assert.Equal(600, log.GetProperty("totalLines").GetInt32());
            Assert.Equal(10, log.GetProperty("returned").GetInt32());
            Assert.Equal(590, log.GetProperty("fromLine").GetInt32());
            Assert.True(log.GetProperty("truncated").GetBoolean());
            var lines = log.GetProperty("lines").EnumerateArray().ToList();
            Assert.Equal("590", lines[0].GetProperty("text").GetString());
            Assert.Equal("599", lines[^1].GetProperty("text").GetString());

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// One step can be read on its own: its JSON, where it sits, and what validation says about it.
    /// Before this, changing one parameter meant fetching the whole macro.
    /// </summary>
    [Fact]
    public void A_client_can_read_one_step_by_id()
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
                        {"name":"单步","steps":[
                          {"type":"control.repeat","params":{"times":"2","intervalMs":"0","body":[
                            {"type":"control.delay","params":{"ms":"5"}}
                          ]}}
                        ]}
                        """),
                })).RootElement;
            var inner = put.GetProperty("outline")[1].GetProperty("id").GetString()!;

            var step = JsonDocument.Parse(await Call(client, "whalegenie_get_step",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "单步",
                    ["stepId"] = inner,
                })).RootElement;
            Assert.Equal("control.delay", step.GetProperty("type").GetString());
            Assert.Equal("0.body.0", step.GetProperty("path").GetString());
            Assert.Equal("5", step.GetProperty("step").GetProperty("params")
                .GetProperty("ms").GetRawText());
            Assert.Equal(0, step.GetProperty("problems").GetArrayLength());

            var missing = await client.CallToolAsync("whalegenie_get_step",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "单步",
                    ["stepId"] = "zzzz",
                });
            Assert.True(missing.IsError);

            await loop.StopAsync();
            return true;
        });
    }
}
