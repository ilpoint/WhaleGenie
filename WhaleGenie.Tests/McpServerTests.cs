using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
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

            var prompts = await client.ListPromptsAsync();
            Assert.Contains(prompts, prompt => prompt.Name == "whalegenie_write_macro");
            Assert.Contains(prompts, prompt => prompt.Name == "whalegenie_debug_macro");

            var written = await client.GetPromptAsync("whalegenie_write_macro",
                new Dictionary<string, object?>());
            var message = Assert.Single(written.Messages);
            var instructions = Assert.IsType<TextContentBlock>(message.Content).Text;
            Assert.Contains("whalegenie_open_project", instructions, StringComparison.Ordinal);
            Assert.Contains("whalegenie_put_macro", instructions, StringComparison.Ordinal);

            var debug = await client.GetPromptAsync("whalegenie_debug_macro",
                new Dictionary<string, object?>());
            var debugInstructions = Assert.IsType<TextContentBlock>(
                Assert.Single(debug.Messages).Content).Text;
            Assert.Contains("whalegenie_get_run_log", debugInstructions, StringComparison.Ordinal);

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
                    ["macroJson"] = """{"name":"AI宏","steps":[]}""",
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
            Assert.Equal("调试宏", runs.RootElement[0].GetProperty("macro").GetString());

            var log = JsonDocument.Parse(await Call(client, "whalegenie_get_run_log",
                new Dictionary<string, object?>()));
            Assert.Equal("完成", log.RootElement.GetProperty("lines")[0]
                .GetProperty("text").GetString());
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
