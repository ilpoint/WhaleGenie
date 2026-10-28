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

            // What a call passes is a row of arguments, not a key per argument: the shape changed
            // with package version 20, and a prompt that still says otherwise teaches a step the
            // engine cannot read.
            Assert.DoesNotContain("arg.", instructions, StringComparison.Ordinal);
            Assert.DoesNotContain("arg.", debugInstructions, StringComparison.Ordinal);
            Assert.Contains("params.arguments", instructions, StringComparison.Ordinal);

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

    /// <summary>
    /// The project is one page a client can keep, and every macro on it names its own page. Reading
    /// that page gives the same outline the tools give, so a client holding both can place a step
    /// without reading the macro whole — which is the whole point of a page over an answer.
    /// </summary>
    [Fact]
    public void The_project_reads_as_one_page_and_every_macro_names_its_own()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

            var draft = await Draft(client);
            var written = JsonDocument.Parse(await Wrote(client, "whalegenie_put_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = JsonNode.Parse("""
                        {"name":"登录","steps":[{"type":"control.delay","params":{"ms":"10"}}]}
                        """),
                })).RootElement;
            await Wrote(client, "whalegenie_apply", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
            });

            var status = JsonDocument.Parse(await Call(client, "whalegenie_project_status",
                new Dictionary<string, object?>())).RootElement;
            var project = await ReadPage(client, "whalegenie://project");

            // The page is about the same revision the tool reports: that number is what makes
            // keeping the page safe, so it may not be one the project has already left behind.
            Assert.Equal(status.GetProperty("revision").GetInt32(),
                project.GetProperty("revision").GetInt32());

            var login = project.GetProperty("macros").EnumerateArray()
                .Single(macro => macro.GetProperty("name").GetString() == "登录");
            Assert.Equal(1, login.GetProperty("steps").GetInt32());

            // Where the macro's own page is, escaped so a Chinese name survives being a path.
            var uri = login.GetProperty("uri").GetString()!;
            Assert.Equal(McpResources.MacroUri("登录"), uri);

            var page = await ReadPage(client, uri);
            Assert.Equal("登录", page.GetProperty("name").GetString());
            var steps = page.GetProperty("outline").EnumerateArray().ToList();
            Assert.Single(steps);
            Assert.Equal(written.GetProperty("outline")[0].GetProperty("id").GetString(),
                steps[0].GetProperty("id").GetString());
            Assert.Equal("0", steps[0].GetProperty("path").GetString());

            await host.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A page for a name the project does not have answers with the nearest names rather than
    /// failing: the client asked about a macro it half-remembers, and a candidate list is the answer
    /// to that question. A name that is only longer than the real one still finds it.
    /// </summary>
    [Fact]
    public void A_macro_page_for_a_name_that_is_not_there_names_the_nearest_ones()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

            var draft = await Draft(client);
            await Wrote(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = JsonNode.Parse("""
                    {"name":"登录","steps":[{"type":"control.delay","params":{"ms":"10"}}]}
                    """),
            });
            await Wrote(client, "whalegenie_apply", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
            });

            var page = await ReadPage(client, McpResources.MacroUri("登录后台"));
            var error = page.GetProperty("error");
            Assert.Equal(McpErrors.NoSuchMacro, error.GetProperty("code").GetString());
            Assert.Contains(error.GetProperty("candidates").EnumerateArray(),
                candidate => candidate.GetString() == "登录");

            await host.StopAsync();
            return true;
        });
    }

    /// <summary>Reads one resource and parses it, which is what a client keeping a page would do.</summary>
    private static async Task<JsonElement> ReadPage(McpClient client, string uri)
    {
        var read = await client.ReadResourceAsync(uri);
        var text = string.Concat(read.Contents.OfType<TextResourceContents>()
            .Select(content => content.Text));
        return JsonDocument.Parse(text).RootElement;
    }

    private static async Task<McpClient> ConnectAsync(string endpoint)
        => await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
        }));

    /// <summary>
    /// What a step says it does to the machine, so a client can tell a click from a read before it
    /// writes the step: the flags are in the catalogue and used to be dropped on the way out.
    /// </summary>
    [Fact]
    public void An_action_says_whether_it_touches_the_machine()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

            var click = JsonDocument.Parse(await Call(client, "whalegenie_get_action",
                new Dictionary<string, object?> { ["key"] = "input.mouseClick" })).RootElement;
            Assert.True(click.GetProperty("sendsInput").GetBoolean());
            Assert.False(click.GetProperty("looksAtScreen").GetBoolean());
            Assert.Equal("None", click.GetProperty("timeoutSource").GetString());

            var shot = JsonDocument.Parse(await Call(client, "whalegenie_get_action",
                new Dictionary<string, object?> { ["key"] = "vision.capture" })).RootElement;
            Assert.False(shot.GetProperty("sendsInput").GetBoolean());
            Assert.True(shot.GetProperty("looksAtScreen").GetBoolean());

            var wait = JsonDocument.Parse(await Call(client, "whalegenie_get_action",
                new Dictionary<string, object?> { ["key"] = "window.waitFor" })).RootElement;
            Assert.True(wait.GetProperty("waits").GetBoolean());
            Assert.Equal("Step", wait.GetProperty("timeoutSource").GetString());

            await host.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A key one letter away from a real one gets its neighbours. Containment alone answered nothing
    /// for "control.delai", which is exactly when a client needs the hint.
    /// </summary>
    [Fact]
    public void A_misspelled_action_key_gets_its_neighbours()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

            var answer = await client.CallToolAsync("whalegenie_get_action",
                new Dictionary<string, object?> { ["key"] = "control.delai" });
            Assert.True(answer.IsError);
            var error = JsonDocument.Parse(answer.Content.OfType<TextContentBlock>().First().Text)
                .RootElement.GetProperty("error");
            Assert.Equal("UnknownAction", error.GetProperty("code").GetString());
            var candidates = error.GetProperty("details").GetProperty("candidates").EnumerateArray()
                .Select(candidate => candidate.GetString()).ToList();
            Assert.Contains("control.delay", candidates);

            await host.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A draft id nobody handed out is not a discard that worked: a client that mistyped it has to
    /// hear so, or it believes it cleaned up.
    /// </summary>
    [Fact]
    public void Discarding_a_draft_that_never_existed_says_so()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

            var answer = await client.CallToolAsync("whalegenie_discard",
                new Dictionary<string, object?> { ["draftId"] = "nope1234" });
            Assert.True(answer.IsError);
            var error = JsonDocument.Parse(answer.Content.OfType<TextContentBlock>().First().Text)
                .RootElement.GetProperty("error");
            Assert.Equal("DraftNotFound", error.GetProperty("code").GetString());

            await host.StopAsync();
            return true;
        });
    }

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

            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

            var log = JsonDocument.Parse(await Call(client, "whalegenie_get_run_log",
                new Dictionary<string, object?> { ["tail"] = 10 })).RootElement;
            Assert.Equal(600, log.GetProperty("totalLines").GetInt32());
            Assert.Equal(10, log.GetProperty("returned").GetInt32());
            Assert.Equal(590, log.GetProperty("fromLine").GetInt32());
            Assert.True(log.GetProperty("truncated").GetBoolean());
            var lines = log.GetProperty("lines").EnumerateArray().ToList();
            Assert.Equal("590", lines[0].GetProperty("text").GetString());
            Assert.Equal("599", lines[^1].GetProperty("text").GetString());

            await host.StopAsync();
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
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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

            await host.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// The global declarations can be read back in the shape they are written in, so a client does
    /// not have to replace a list it has never seen.
    /// </summary>
    [Fact]
    public void A_client_can_read_the_global_declarations()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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

            await host.StopAsync();
            return true;
        });
    }

    /// <summary>A long project is paged rather than answered whole, and says whether more is left.</summary>
    [Fact]
    public void The_macro_list_is_paged()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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

            await host.StopAsync();
            return true;
        });
    }

    /// <summary>A patch can leave the outline out, which is most of the answer for a long macro.</summary>
    [Fact]
    public void A_patch_can_leave_the_outline_out()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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

            await host.StopAsync();
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
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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

            await host.StopAsync();
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
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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
                        """[{"op":"macro","description":"改过的","delayScale":0.5,"triggerMode":"Timer"}]"""),
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
            Assert.Equal("Timer", macro.GetProperty("triggerMode").GetString());

            var refused = await client.CallToolAsync("whalegenie_patch_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["name"] = "宏设置",
                    ["operations"] = JsonNode.Parse("""[{"op":"macro","triggerMode":"Nonsense"}]"""),
                });
            Assert.True(refused.IsError);

            await host.StopAsync();
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
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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

            await host.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// The first row of a table that has never had one can be added. The old answer told a client
    /// to use addRow and then refused that very call, which was a loop with no exit.
    /// </summary>
    [Fact]
    public void The_first_row_of_a_table_can_be_added()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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

            await host.StopAsync();
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
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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

            await host.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A submacro's steps are in the outline and can be patched like any other: before this, a
    /// project that used one could only be changed by sending the whole macro again.
    /// </summary>
    [Fact]
    public void A_submacro_s_steps_can_be_read_and_patched()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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

            await host.StopAsync();
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

    /// <summary>
    /// Reading the log before anything has run is the state a session starts in, not a mistake: the
    /// answer is empty and says so, so a client does not treat its first look at the log as an error
    /// to retry or branch on. An id that names no run is still refused, and still says which.
    /// </summary>
    [Fact]
    public void The_run_log_read_before_anything_has_run_is_empty_rather_than_an_error()
    {
        Ui.RunAsync(async () =>
        {
            RunLogStore.Forget();
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

            var log = JsonDocument.Parse(await Call(client, "whalegenie_get_run_log",
                new Dictionary<string, object?>())).RootElement;

            Assert.Equal(0, log.GetProperty("totalLines").GetInt32());
            Assert.Equal(0, log.GetProperty("returned").GetInt32());
            Assert.False(log.GetProperty("truncated").GetBoolean());
            Assert.Empty(log.GetProperty("lines").EnumerateArray());
            Assert.Contains("No run", log.GetProperty("note").GetString(), StringComparison.Ordinal);

            var missing = await Call(client, "whalegenie_get_run_log",
                new Dictionary<string, object?> { ["runId"] = "nope" });
            Assert.Contains("NoSuchRun", missing, StringComparison.Ordinal);

            await host.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A parameter that has to be an object or a list arrives as text often enough to matter — a
    /// client quoting the macro, or wrapping the array in an object. The schema stays strict, and the
    /// answer names the argument, what arrived and what to send instead, rather than the runtime's
    /// "An error occurred invoking".
    /// </summary>
    [Fact]
    public void An_argument_of_the_wrong_shape_is_refused_by_name()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-shape-" + Guid.NewGuid().ToString("N"));
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

                // The macro as text, which is how a double-encoded macro arrives.
                var quoted = await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = "{\"name\":\"AI宏\",\"steps\":[]}",
                });
                Assert.Contains("BadJson", quoted, StringComparison.Ordinal);
                Assert.Contains("\"macro\"", quoted, StringComparison.Ordinal);
                Assert.Contains("text", quoted, StringComparison.Ordinal);

                // The declarations as one object instead of a list of them.
                var wrapped = await Call(client, "whalegenie_set_globals", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["globals"] = new { name = "总数" },
                });
                Assert.Contains("BadJson", wrapped, StringComparison.Ordinal);
                Assert.Contains("\"globals\"", wrapped, StringComparison.Ordinal);
                Assert.Contains("list", wrapped, StringComparison.Ordinal);

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
                            Value = "1000",
                        },
                    ],
                });
                viewModel.AddMacro(existing);
                viewModel.CurrentPath = path;
                viewModel.SavePackage(path);

                await using var host = new McpServerHost(viewModel);
                await host.StartAsync();
                await using var client = await ConnectAsync(host.Endpoint);

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

            await using var host = new McpServerHost(viewModel);
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

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

            await host.StopAsync();
            return true;
        });
    }

    private static async Task<string> Call(McpClient client, string name,
        Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(name, arguments);
        return result.Content.OfType<TextContentBlock>().First().Text;
    }
}
