using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WhaleGenie.Mcp;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// Running a macro through MCP: the second switch, the one run at a time, and the answers that make
/// a run worth doing. These checks use a macro that touches no device, so what they measure is the
/// entrance itself rather than the machine behind it.
/// </summary>
public class McpRunTests : IDisposable
{
    /// <summary>The two switches, standing in for the MCP page's settings file.</summary>
    private readonly McpGate _gate = new();

    public void Dispose() => _gate.Dispose();

    /// <summary>With the switch off nothing runs, and the refusal says which switch to turn on.</summary>
    [Fact]
    public void A_run_needs_its_own_switch()
    {
        Ui.RunAsync(async () =>
        {
            _gate.Run = false;
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            await Put(client, draft, """{"name":"安静","steps":[]}""");

            var answer = await client.CallToolAsync("whalegenie_run_macro",
                new Dictionary<string, object?> { ["name"] = "安静", ["draftId"] = draft });
            Assert.True(answer.IsError);
            var error = JsonDocument.Parse(answer.Content.OfType<TextContentBlock>().First().Text)
                .RootElement.GetProperty("error");
            Assert.Equal("RunNotAllowed", error.GetProperty("code").GetString());
            Assert.Contains("Allow the AI to run macros", error.GetProperty("hint").GetString()!,
                StringComparison.Ordinal);

            // Direct control being on is not the same permission.
            _gate.Control = true;
            var stillOff = await client.CallToolAsync("whalegenie_run_macro",
                new Dictionary<string, object?> { ["name"] = "安静", ["draftId"] = draft });
            Assert.True(stillOff.IsError);
            _gate.Control = false;

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A draft can be run before it is applied, and the answer carries what each step worked out —
    /// which is the whole point: a client can compare it with what it meant.
    /// </summary>
    [Fact]
    public void A_draft_runs_and_answers_with_what_each_step_worked_out()
    {
        Ui.RunAsync(async () =>
        {
            _gate.Run = true;
            try
            {
                await using var loop = McpLoopback.Start(new MainViewModel());
                await using var client = await loop.ConnectAsync();

                var draft = await Draft(client);
                var put = await Put(client, draft, """
                    {"name":"算一下","steps":[
                      {"type":"data.base64Encode",
                       "params":{"dataText":"bench","returns":[{"key":"encoded","name":"编码"}]}}
                    ]}
                    """);
                var stepId = JsonDocument.Parse(put).RootElement
                    .GetProperty("outline")[0].GetProperty("id").GetString()!;

                var answer = JsonDocument.Parse(await Call(client, "whalegenie_run_macro",
                    new Dictionary<string, object?>
                    {
                        ["name"] = "算一下",
                        ["draftId"] = draft,
                    })).RootElement;

                Assert.Equal("Completed", answer.GetProperty("status").GetString());
                Assert.Equal(1, answer.GetProperty("steps").GetInt32());

                var outcome = answer.GetProperty("outcomes").EnumerateArray()
                    .Single(item => item.GetProperty("stepId").GetString() == stepId);
                Assert.Equal("ok", outcome.GetProperty("outcome").GetString());
                var result = outcome.GetProperty("results")[0];
                Assert.Equal("encoded", result.GetProperty("key").GetString());
                Assert.Equal("编码", result.GetProperty("label").GetString());
                Assert.Equal("YmVuY2g=", result.GetProperty("value").GetString());
                Assert.True(result.GetProperty("kept").GetBoolean());

                // The run is in the history the debug tools read, under its own source.
                var runs = JsonDocument.Parse(await Call(client, "whalegenie_list_runs",
                    new Dictionary<string, object?> { ["limit"] = 5 })).RootElement;
                Assert.Contains(runs.GetProperty("runs").EnumerateArray(),
                    run => run.GetProperty("source").GetString() == "mcp");

                await loop.StopAsync();
            }
            finally
            {
                _gate.Run = false;
            }

            return true;
        });
    }

    /// <summary>A second run is refused rather than queued: two macros on one machine cannot interleave.</summary>
    [Fact]
    public void A_second_run_while_one_is_going_is_refused()
    {
        Ui.RunAsync(async () =>
        {
            _gate.Run = true;
            try
            {
                await using var loop = McpLoopback.Start(new MainViewModel());
                await using var client = await loop.ConnectAsync();

                var draft = await Draft(client);
                await Put(client, draft, """
                    {"name":"慢","steps":[{"type":"control.delay","params":{"ms":"1200"}}]}
                    """);

                var first = client.CallToolAsync("whalegenie_run_macro",
                    new Dictionary<string, object?>
                    {
                        ["name"] = "慢",
                        ["draftId"] = draft,
                        ["timeoutMs"] = 5000,
                    });
                await Task.Delay(150);

                var second = await client.CallToolAsync("whalegenie_run_macro",
                    new Dictionary<string, object?>
                    {
                        ["name"] = "慢",
                        ["draftId"] = draft,
                        ["timeoutMs"] = 5000,
                    });
                Assert.True(second.IsError);
                var error = JsonDocument.Parse(second.Content.OfType<TextContentBlock>().First().Text)
                    .RootElement.GetProperty("error");
                Assert.Equal("RunBusy", error.GetProperty("code").GetString());

                var status = JsonDocument.Parse(await Call(client, "whalegenie_run_status",
                    new Dictionary<string, object?>())).RootElement;
                Assert.True(status.GetProperty("running").GetBoolean());

                // Stopping it is what the person would do, and it ends as Stopped.
                var stop = JsonDocument.Parse(await Call(client, "whalegenie_stop_macro",
                    new Dictionary<string, object?>())).RootElement;
                Assert.False(string.IsNullOrEmpty(stop.GetProperty("stopped").GetString()));

                var firstResult = await first;
                var finished = JsonDocument.Parse(
                    firstResult.Content.OfType<TextContentBlock>().First().Text).RootElement;
                Assert.Equal("Stopped", finished.GetProperty("status").GetString());

                await loop.StopAsync();
            }
            finally
            {
                _gate.Run = false;
            }

            return true;
        });
    }

    /// <summary>Stopping nothing is an answer of its own, not a silent success.</summary>
    [Fact]
    public void Stopping_a_run_that_is_not_going_says_so()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var answer = await client.CallToolAsync("whalegenie_stop_macro",
                new Dictionary<string, object?>());
            Assert.True(answer.IsError);

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A macro that calls one of its own submacros runs. The engine only knows names, and until the
    /// submacros were gathered into the run's library the call failed with Run.MacroNotFound however
    /// it was written — the editor could make the call, the run could not find it.
    /// </summary>
    [Fact]
    public void A_macro_can_call_its_own_submacro()
    {
        Ui.RunAsync(async () =>
        {
            _gate.Run = true;
            try
            {
                await using var loop = McpLoopback.Start(new MainViewModel());
                await using var client = await loop.ConnectAsync();

                var draft = await Draft(client);
                var put = await Put(client, draft, """
                    {"name":"外面","steps":[
                       {"type":"control.runMacro","params":{"macro":"里面","arguments":[],"returns":[]}}
                     ],
                     "subMacros":[
                       {"name":"里面","steps":[
                          {"type":"control.setVariable",
                           "params":{"name":"见过","scope":"local","value":"yes"}}
                        ]}
                     ]}
                    """);
                var stepId = JsonDocument.Parse(put).RootElement
                    .GetProperty("outline")[0].GetProperty("id").GetString()!;

                var byBareName = JsonDocument.Parse(await Call(client, "whalegenie_run_macro",
                    new Dictionary<string, object?> { ["name"] = "外面", ["draftId"] = draft }))
                    .RootElement;
                Assert.Equal("Completed", byBareName.GetProperty("status").GetString());

                // The qualified name works whatever else the project holds: two parents may each
                // have a submacro called the same thing.
                await Call(client, "whalegenie_patch_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "外面",
                    ["operations"] = JsonNode.Parse(
                        "[{\"op\":\"set\",\"stepId\":\"" + stepId + "\",\"param\":\"macro\","
                        + "\"value\":\"外面/里面\"}]"),
                });

                var byQualifiedName = JsonDocument.Parse(await Call(client, "whalegenie_run_macro",
                    new Dictionary<string, object?> { ["name"] = "外面", ["draftId"] = draft }))
                    .RootElement;
                Assert.Equal("Completed", byQualifiedName.GetProperty("status").GetString());
                Assert.Equal(2, byQualifiedName.GetProperty("steps").GetInt32());

                await loop.StopAsync();
            }
            finally
            {
                _gate.Run = false;
            }

            return true;
        });
    }

    private static async Task<string> Draft(McpClient client)
        => JsonDocument.Parse(await Call(client, "whalegenie_open_project",
            new Dictionary<string, object?>())).RootElement.GetProperty("draftId").GetString()!;

    private static async Task<string> Put(McpClient client, string draft, string macro)
        => await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
        {
            ["draftId"] = draft,
            ["macro"] = JsonNode.Parse(macro),
        });

    private static async Task<string> Call(McpClient client, string name,
        Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(name, arguments);
        var text = result.Content.OfType<TextContentBlock>().First().Text;
        Assert.True(result.IsError is not true, text);
        return text;
    }

}
