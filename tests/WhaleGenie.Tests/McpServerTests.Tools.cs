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

// What the server offers: the tool list, the client config, the switches and the door in front of them.
public partial class McpServerTests
{
    [Fact]
    public void The_server_lists_its_tools_and_reports_the_project()
    {
        Ui.RunAsync(async () =>
        {
            var viewModel = new MainViewModel();
            await using var loop = McpLoopback.Start(viewModel);

            await using var client = await loop.ConnectAsync();
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
            await loop.StopAsync();
            Assert.False(loop.Host.IsRunning);
            return true;
        });
    }

    [Fact]
    public void The_action_tool_describes_record_fields_and_number_ranges()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());

            await using var client = await loop.ConnectAsync();
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

            await loop.StopAsync();
            return true;
        });
    }


    /// <summary>
    /// The two switches belong to the settings, not to the block. A served run reads them while it
    /// runs, so a client that was set up once keeps working when one is flipped — and the block a
    /// person copies never has to be copied a second time.
    /// </summary>
    [Fact]
    public void The_switches_are_stored_and_leave_the_block_alone()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-grants-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var stored = LocalSettings.FilePath;
        try
        {
            LocalSettings.FilePath = Path.Combine(folder, "settings.json");

            var mcp = new McpServerViewModel();
            mcp.AllowControl = false;
            mcp.AllowRun = false;
            var block = mcp.Config;
            Assert.Contains("\"--mcp\"", block, StringComparison.Ordinal);
            Assert.DoesNotContain("--allow-control", block, StringComparison.Ordinal);
            Assert.DoesNotContain("--allow-run", block, StringComparison.Ordinal);

            mcp.AllowControl = true;
            mcp.AllowRun = true;

            // The block is what the client was told once; the switches are not in it.
            Assert.Equal(block, mcp.Config);

            // 记住选择：下次打开这一页，开关还是上次那两个，块还是同一块。
            var again = new McpServerViewModel();
            Assert.True(again.AllowControl);
            Assert.True(again.AllowRun);
            Assert.Equal(block, again.Config);
        }
        finally
        {
            LocalSettings.FilePath = stored;
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// Writing the project is the same favour as driving the machine: a client that may not press a
    /// key may not rewrite the file either. Reading and editing a draft stays free, which is what
    /// lets a client prepare a change it cannot put anywhere.
    /// </summary>
    [Fact]
    public void Writing_the_project_needs_the_control_switch()
    {
        Ui.RunAsync(async () =>
        {
            _gate.Control = false;
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            await Wrote(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = JsonNode.Parse("""{"name":"写不进去","steps":[]}"""),
            });

            // The draft was writable; the project is not.
            var applied = await client.CallToolAsync("whalegenie_apply",
                new Dictionary<string, object?> { ["draftId"] = draft });
            Assert.True(applied.IsError == true);
            Assert.Equal(McpErrors.ProjectWriteOff, Code(applied));

            var saved = await client.CallToolAsync("whalegenie_save_project",
                new Dictionary<string, object?>());
            Assert.True(saved.IsError == true);
            Assert.Equal(McpErrors.ProjectWriteOff, Code(saved));

            // Opening the switch is all it takes, and nothing has to be restarted.
            _gate.Control = true;

            var through = await client.CallToolAsync("whalegenie_apply",
                new Dictionary<string, object?> { ["draftId"] = draft });
            Assert.True(through.IsError is not true);
            await loop.StopAsync();
            return true;
        });
    }
}
