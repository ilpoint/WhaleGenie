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
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The in-process MCP endpoint. It is started on a loopback port, listed and called through the
/// official client; no window, no device and no external service is involved.
/// </summary>
public class McpServerTests
{
    [Fact]
    public void The_server_lists_its_tools_and_reports_the_project()
    {
        Ui.RunAsync(async () =>
        {
            var viewModel = new MainViewModel();
            await using var host = new McpServerHost(viewModel);
            await host.StartAsync();

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(host.Endpoint),
                TransportMode = HttpTransportMode.StreamableHttp,
            });
            await using var client = await McpClient.CreateAsync(transport);
            var tools = await client.ListToolsAsync();

            Assert.Contains(tools, tool => tool.Name == "whalegenie_open_project");
            Assert.Contains(tools, tool => tool.Name == "whalegenie_get_run_log");
            Assert.Contains(tools, tool => tool.Name == "whalegenie_save_project");

            // The looking tools answer whenever the server is on; the driving ones only while the
            // flyout's own switch is on.
            foreach (var name in new[]
                     {
                         "whalegenie_capture_screen", "whalegenie_pixel_at", "whalegenie_cursor",
                         "whalegenie_list_windows", "whalegenie_element_at",
                         "whalegenie_mouse_move", "whalegenie_mouse_click", "whalegenie_mouse_scroll",
                         "whalegenie_mouse_drag", "whalegenie_key", "whalegenie_type_text",
                         "whalegenie_window_activate", "whalegenie_window_move", "whalegenie_window_close",
                     })
            {
                Assert.Contains(tools, tool => tool.Name == name);
            }

            var prompts = await client.ListPromptsAsync();
            Assert.Contains(prompts, prompt => prompt.Name == "whalegenie_write_macro");
            Assert.Contains(prompts, prompt => prompt.Name == "whalegenie_debug_macro");

            var written = await client.GetPromptAsync("whalegenie_write_macro",
                new Dictionary<string, object?>());
            var message = Assert.Single(written.Messages);
            var instructions = Assert.IsType<TextContentBlock>(message.Content).Text;
            Assert.Contains("whalegenie_open_project", instructions, StringComparison.Ordinal);
            Assert.Contains("whalegenie_put_macro", instructions, StringComparison.Ordinal);
            Assert.Contains("childKeys", instructions, StringComparison.Ordinal);

            var debug = await client.GetPromptAsync("whalegenie_debug_macro",
                new Dictionary<string, object?>());
            var debugInstructions = Assert.IsType<TextContentBlock>(
                Assert.Single(debug.Messages).Content).Text;
            Assert.Contains("whalegenie_get_run_log", debugInstructions, StringComparison.Ordinal);
            Assert.Contains("childKeys", debugInstructions, StringComparison.Ordinal);

            // The debug loop now closes on the machine itself: look at the screen, and drive it
            // only while the person's own switch is on.
            Assert.Contains("whalegenie_capture_screen", debugInstructions, StringComparison.Ordinal);
            Assert.Contains("Allow direct control", debugInstructions, StringComparison.Ordinal);

            var status = await client.CallToolAsync("whalegenie_project_status",
                new Dictionary<string, object?>());
            var text = status.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()
                .First().Text;

            Assert.Contains("macroCount", text, StringComparison.Ordinal);
            await host.StopAsync();
            Assert.False(host.IsRunning);
            return true;
        });
    }

    [Fact]
    public void The_action_tool_describes_record_fields_and_number_ranges()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(host.Endpoint),
                TransportMode = HttpTransportMode.StreamableHttp,
            });
            await using var client = await McpClient.CreateAsync(transport);
            var action = JsonDocument.Parse(await Call(client, "whalegenie_get_action",
                new Dictionary<string, object?> { ["key"] = "window.move" }));
            var parameters = action.RootElement.GetProperty("parameters").EnumerateArray().ToArray();

            var window = Assert.Single(parameters,
                parameter => parameter.GetProperty("name").GetString() == "window");
            Assert.Equal("WindowRule", window.GetProperty("shape").GetString());
            Assert.Contains("multiline", window.GetProperty("fields").EnumerateArray()
                .Select(field => field.GetString()));

            var x = Assert.Single(parameters,
                parameter => parameter.GetProperty("name").GetString() == "x");
            Assert.Equal("Pixels", x.GetProperty("unit").GetString());
            Assert.Equal("Keep", x.GetProperty("emptyMeans").GetString());
            Assert.Equal(-100_000m, x.GetProperty("minimum").GetDecimal());
            Assert.Equal(100_000m, x.GetProperty("maximum").GetDecimal());

            var choice = JsonDocument.Parse(await Call(client, "whalegenie_get_action",
                new Dictionary<string, object?> { ["key"] = "control.switch" }));
            Assert.False(choice.RootElement.GetProperty("hidden").GetBoolean());
            var cases = Assert.Single(choice.RootElement.GetProperty("parameters").EnumerateArray(),
                parameter => parameter.GetProperty("name").GetString() == "cases");
            Assert.Equal(["control.case"], cases.GetProperty("childKeys").EnumerateArray()
                .Select(key => key.GetString()!));

            var branch = JsonDocument.Parse(await Call(client, "whalegenie_get_action",
                new Dictionary<string, object?> { ["key"] = "control.case" }));
            Assert.True(branch.RootElement.GetProperty("hidden").GetBoolean());

            await host.StopAsync();
            return true;
        });
    }

    [Fact]
    public void The_main_window_has_an_mcp_switch_and_a_flyout()
    {
        Ui.Run(() =>
        {
            var window = new MainWindow { DataContext = new MainViewModel() };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var button = window.FindControl<Button>("McpButton");
            Assert.NotNull(button);
            Assert.Null(button!.Command);
            var flyout = Assert.IsType<Flyout>(button.Flyout);
            Assert.NotNull(flyout.Content);
            Assert.NotNull(window.FindControl<TextBlock>("VersionText"));

            // The server's own switch, and the direct-control switch beside it.
            Assert.NotNull(window.FindControl<ToggleSwitch>("McpToggle"));
            Assert.NotNull(window.FindControl<ToggleSwitch>("McpControlToggle"));
            window.Close();
        });
    }

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
                viewModel.SavePackage(path);

                await using var host = new McpServerHost(viewModel);
                await host.StartAsync();
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(host.Endpoint),
                    TransportMode = HttpTransportMode.StreamableHttp,
                });
                await using var client = await McpClient.CreateAsync(transport);

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
                Assert.True(File.Exists(path + ".bak"));
                await host.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    [Fact]
    public void The_switch_starts_and_stops_the_server()
    {
        Ui.RunAsync(async () =>
        {
            var viewModel = new MainViewModel();
            await viewModel.Mcp.ToggleCommand.ExecuteAsync(null);

            Assert.True(viewModel.Mcp.IsRunning);
            Assert.False(string.IsNullOrWhiteSpace(viewModel.Mcp.Endpoint));

            await viewModel.Mcp.ToggleCommand.ExecuteAsync(null);
            Assert.False(viewModel.Mcp.IsRunning);
            Assert.Equal(string.Empty, viewModel.Mcp.Endpoint);
            return true;
        });
    }

    [Fact]
    public void The_mcp_port_is_kept_between_runs()
    {
        var original = LocalSettings.FilePath;
        var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-port-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            LocalSettings.FilePath = Path.Combine(folder, "settings.json");
            var first = new MainViewModel();
            first.Mcp.Port = 4123;

            var second = new MainViewModel();
            Assert.Equal(4123, second.Mcp.Port);
        }
        finally
        {
            LocalSettings.FilePath = original;
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void The_switch_waits_for_the_save_question()
    {
        Ui.RunAsync(async () =>
        {
            var viewModel = new MainViewModel();
            viewModel.Mcp.EnsureSaved = () => Task.FromResult(false);

            await viewModel.Mcp.ToggleCommand.ExecuteAsync(null);

            Assert.False(viewModel.Mcp.IsRunning);
            Assert.Equal(Strings.Get("Mcp.NeedSave"), viewModel.Mcp.Status);
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

            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(host.Endpoint),
                TransportMode = HttpTransportMode.StreamableHttp,
            });
            await using var client = await McpClient.CreateAsync(transport);

            var runs = JsonDocument.Parse(await Call(client, "whalegenie_list_runs",
                new Dictionary<string, object?> { ["limit"] = 5 }));
            Assert.Equal("调试宏", runs.RootElement.GetProperty("runs")[0]
                .GetProperty("macro").GetString());

            var log = JsonDocument.Parse(await Call(client, "whalegenie_get_run_log",
                new Dictionary<string, object?>()));
            Assert.Equal("完成", log.RootElement.GetProperty("lines")[0]
                .GetProperty("text").GetString());
            await host.StopAsync();
            return true;
        });
    }

    [Fact]
    public void The_tools_declare_their_allowed_values_and_their_titles()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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

            await host.StopAsync();
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
                viewModel.SavePackage(path);

                await using var host = new McpServerHost(viewModel);
                await host.StartAsync();
                await using var client = await ConnectAsync(host.Endpoint);

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

                await host.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

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

                await using var host = new McpServerHost(viewModel);
                await host.StartAsync();
                await using var client = await ConnectAsync(host.Endpoint);
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

                // The macro comes back when it is asked for, which is the one time it is wanted.
                var echoed = JsonDocument.Parse(await Call(client, "whalegenie_put_macro",
                    new Dictionary<string, object?>
                    {
                        ["draftId"] = draft,
                        ["macro"] = JsonNode.Parse("""{"name":"补丁宏","steps":[]}"""),
                        ["includeMacro"] = true,
                    })).RootElement;
                Assert.True(echoed.TryGetProperty("macro", out _));

                await host.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    [Fact]
    public void A_client_can_read_what_a_macro_takes_and_write_a_call_from_it()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-signature-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "project.wgmacro");
                var viewModel = new MainViewModel();
                viewModel.AddMacro(new MacroItem { Name = "原宏" });
                viewModel.CurrentPath = path;
                viewModel.SavePackage(path);

                await using var host = new McpServerHost(viewModel);
                await host.StartAsync();
                await using var client = await ConnectAsync(host.Endpoint);
                var draft = JsonDocument.Parse(await Call(client, "whalegenie_open_project",
                    new Dictionary<string, object?>())).RootElement.GetProperty("draftId").GetString()!;

                await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = JsonNode.Parse("""
                        {"name":"子","arguments":[
                           {"name":"数量","type":"Number","required":true,"description":"要加多少。"},
                           {"name":"标签","type":"Text","required":false,"default":"无"}],
                         "returns":[{"name":"答案","type":"Number","description":"加完的结果。"}],
                         "steps":[{"type":"control.delay","params":{"ms":"1"}}]}
                        """),
                });

                var signature = JsonDocument.Parse(await Call(client, "whalegenie_get_macro_signature",
                    new Dictionary<string, object?> { ["draftId"] = draft, ["name"] = "子" }))
                    .RootElement;

                // A call names its arguments one row each; a client cannot get those names from the
                // action catalogue, which is why this answer carries the signatures themselves.
                var arguments = signature.GetProperty("arguments").EnumerateArray().ToArray();
                Assert.Equal(2, arguments.Length);

                // Chinese comes back as itself: an escaped answer is twice the size and unreadable
                // in a log, and nothing here is HTML.
                Assert.Contains("答案", signature.GetRawText(), StringComparison.Ordinal);

                Assert.Equal("数量", arguments[0].GetProperty("name").GetString());
                Assert.True(arguments[0].GetProperty("required").GetBoolean());
                Assert.False(arguments[1].GetProperty("required").GetBoolean());
                Assert.Equal(["name", "value", "given"],
                    signature.GetProperty("argumentColumns").EnumerateArray()
                        .Select(column => column.GetString() ?? string.Empty).ToArray());

                // The skeleton carries what a call has to fill in, leaves the optional ones out so
                // their declared default applies, and asks for every result by key and name.
                var template = signature.GetProperty("callTemplate");
                var skeleton = Assert.Single(template.GetProperty("arguments").EnumerateArray().ToArray());
                Assert.Equal("数量", skeleton.GetProperty("name").GetString());
                Assert.Equal(string.Empty, skeleton.GetProperty("value").GetString());
                var row = template.GetProperty("returns")[0];
                Assert.Equal("答案", row.GetProperty("key").GetString());
                Assert.Equal("答案", row.GetProperty("name").GetString());

                // A step written from that skeleton is a step the validator accepts, once the value
                // the template leaves empty is filled in.
                var call = JsonNode.Parse(template.GetRawText())!.AsObject();
                call["arguments"]![0]!["value"] = "3";
                await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = new JsonObject
                    {
                        ["name"] = "主",
                        ["steps"] = new JsonArray(new JsonObject
                        {
                            ["type"] = "control.runMacro",
                            ["params"] = call,
                        }),
                    },
                });

                var validated = JsonDocument.Parse(await Call(client, "whalegenie_validate",
                    new Dictionary<string, object?> { ["draftId"] = draft })).RootElement;
                Assert.True(validated.GetProperty("ok").GetBoolean(), validated.GetRawText());

                await host.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    /// <summary>
    /// The catalogue is also a resource, so a client that can cache one reads it once instead of
    /// asking for it again at every turn. Both readings come from the same catalogue, so what is
    /// checked here is that the resource is there and holds every action the tool would list.
    /// </summary>
    [Fact]
    public async Task The_catalogue_is_also_a_resource_a_client_can_keep()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

            var uris = (await client.ListResourcesAsync()).Select(resource => resource.Uri).ToList();
            Assert.Contains(uris, uri => uri.Contains("actions", StringComparison.Ordinal));

            var templates = (await client.ListResourceTemplatesAsync())
                .Select(template => template.UriTemplate).ToList();
            Assert.Contains("whalegenie://actions/{key}", templates);

            var whole = await client.ReadResourceAsync("whalegenie://actions");
            var text = string.Concat(whole.Contents.OfType<TextResourceContents>()
                .Select(content => content.Text));
            Assert.Contains($"\"total\":{ActionSemanticsCatalog.All.Count}", text,
                StringComparison.Ordinal);

            // The template answers for one action, which is the same description the tool gives.
            var one = await client.ReadResourceAsync("whalegenie://actions/control.delay");
            var details = string.Concat(one.Contents.OfType<TextResourceContents>()
                .Select(content => content.Text));
            Assert.Contains("control.delay", details, StringComparison.Ordinal);

            await host.StopAsync();
            return true;
        });
    }

    private static async Task<McpClient> ConnectAsync(string endpoint)
        => await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
        }));

    private static async Task<string> Call(McpClient client, string name,
        Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(name, arguments);
        return result.Content.OfType<TextContentBlock>().First().Text;
    }
}
