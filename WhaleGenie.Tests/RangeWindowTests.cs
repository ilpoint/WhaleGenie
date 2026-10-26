using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WhaleGenie.Core.Devices;
using WhaleGenie.Mcp;
using WhaleGenie.Range;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The range window: a place with controls at fixed places and a colour that does not change, so a
/// run can be checked against what it did rather than against what the desktop happened to look
/// like. These checks are about the window and the tools that read it; what a macro does inside it
/// is somebody else's check.
/// </summary>
public class RangeWindowTests
{
    /// <summary>The window says where everything is, and putting it back takes the presses away.</summary>
    [Fact]
    public void The_range_window_reports_what_it_shows_and_resets()
        => Ui.Run(() =>
    {
        var window = new RangeWindow();
        window.Show();

        var before = JsonDocument.Parse(JsonSerializer.Serialize(window.Facts())).RootElement;
        Assert.True(before.GetProperty("available").GetBoolean());
        Assert.Equal(0, before.GetProperty("presses").GetInt32());
        Assert.Equal("#3366CC", before.GetProperty("colour").GetString());

        var targets = before.GetProperty("targets").EnumerateArray()
            .Select(target => target.GetProperty("id").GetString()).ToList();
        Assert.Equal(
            ["rangeCount", "rangeInput", "rangeFlag", "rangeColour", "rangeDanger"],
            targets);

        Press(window, "rangeCount");
        Press(window, "rangeCount");
        Press(window, "rangeDanger");
        Assert.Equal(2, window.Presses);
        Assert.Equal(1, window.DangerPresses);

        window.Reset();
        Assert.Equal(0, window.Presses);
        Assert.Equal(0, window.DangerPresses);
        Assert.Equal(string.Empty, window.Field);
        Assert.False(window.Flag);

        window.Close();
        Assert.Null(RangeWindow.Current);
    });

    /// <summary>Two controls sit where the window says they do, which is what a click aims at.</summary>
    [Fact]
    public void Every_control_sits_where_the_window_says()
        => Ui.Run(() =>
    {
        var window = new RangeWindow();
        window.Show();

        var facts = JsonDocument.Parse(JsonSerializer.Serialize(window.Facts())).RootElement;
        var count = facts.GetProperty("targets").EnumerateArray()
            .Single(target => target.GetProperty("id").GetString() == "rangeCount");
        Assert.Equal(RangeWindow.CountX, count.GetProperty("x").GetInt32());
        Assert.Equal(RangeWindow.CountY, count.GetProperty("y").GetInt32());
        Assert.Equal(RangeWindow.CountWidth, count.GetProperty("width").GetInt32());

        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(candidate => AutomationProperties.GetAutomationId(candidate) == "rangeCount");
        Assert.Equal(RangeWindow.CountWidth, (int)button.Bounds.Width);
        Assert.Equal(RangeWindow.CountY, (int)button.Bounds.Top);

        window.Close();
    });

    /// <summary>
    /// Without a range run the tools say so: an ordinary window is not a place a client may try
    /// things on, and "there is no range" is a different answer from "the range is empty".
    /// </summary>
    [Fact]
    public void The_range_tools_refuse_when_this_run_has_no_range()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

            var state = await client.CallToolAsync("whalegenie_range_state",
                new Dictionary<string, object?>());
            Assert.True(state.IsError);
            Assert.Equal("RangeNotAvailable", Code(state));

            var reset = await client.CallToolAsync("whalegenie_range_reset",
                new Dictionary<string, object?>());
            Assert.True(reset.IsError);

            await host.StopAsync();
            return true;
        });
    }

    /// <summary>With a range open, the same tools answer with the window and can put it back.</summary>
    [Fact]
    public void The_range_tools_read_and_reset_the_window()
    {
        Ui.RunAsync(async () =>
        {
            var window = new RangeWindow();
            window.Show();
            Press(window, "rangeCount");

            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);

            var state = JsonDocument.Parse(await Call(client, "whalegenie_range_state",
                new Dictionary<string, object?>())).RootElement;
            Assert.True(state.GetProperty("available").GetBoolean());
            Assert.Equal(1, state.GetProperty("presses").GetInt32());

            var reset = JsonDocument.Parse(await Call(client, "whalegenie_range_reset",
                new Dictionary<string, object?>())).RootElement;
            Assert.Equal(0, reset.GetProperty("presses").GetInt32());
            Assert.Equal(0, window.Presses);

            await host.StopAsync();
            window.Close();
            return true;
        });
    }

    /// <summary>
    /// A point outside the window is refused, and the refusal says where the window is. Without the
    /// edge the window would only be a target, not a range.
    /// </summary>
    [Fact]
    public void A_point_outside_the_range_is_refused()
    {
        Ui.Run(() =>
        {
            var window = new RangeWindow();
            window.Show();
            var where = RangeWindow.Where!;

            var devices = new ChecksDevices { Inputs = new SingleInputRouter(new RecordingInput()) };
            var tools = new McpControlTools(() => devices);
            McpControl.Allowed = true;
            try
            {
                var inside = new[] { tools.MouseMove(where.X + 60, where.Y + 58) };
                Assert.True(inside[0].Result.IsError is not true);

                var outside = tools.MouseMove(where.X - 40, where.Y - 40).Result;
                Assert.True(outside.IsError == true);
                Assert.Equal("Range.OutsideTarget", Code(outside));

                var far = tools.MouseClick(where.X + where.Width + 5, where.Y + 10).Result;
                Assert.Equal("Range.OutsideTarget", Code(far));
            }
            finally
            {
                McpControl.Allowed = false;
                window.Close();
            }
        });
    }

    /// <summary>A window that is not the range's is refused, so the rest of the desktop stays out of reach.</summary>
    [Fact]
    public void A_window_that_is_not_the_range_is_refused()
    {
        Ui.Run(() =>
        {
            var window = new RangeWindow();
            window.Show();
            var mine = RangeWindow.Where!.Handle;

            var windows = new WindowsForChecks();
            windows.Add(mine, "WhaleGenie Range", "WhaleGenie", "AvaloniaWindow");
            windows.Add(4242, "别的窗口", "other", "Other");
            var devices = new ChecksDevices { Windows = windows };
            var tools = new McpControlTools(() => devices);
            McpControl.Allowed = true;
            try
            {
                Assert.True(tools.WindowActivate(mine).Result.IsError is not true);

                var other = tools.WindowActivate(4242).Result;
                Assert.True(other.IsError == true);
                Assert.Equal("Range.ForeignWindow", Code(other));

                Assert.Equal("Range.ForeignWindow", Code(tools.WindowClose(4242).Result));
            }
            finally
            {
                McpControl.Allowed = false;
                window.Close();
            }
        });
    }

    /// <summary>
    /// Keys go to whatever has the focus, so in a range run the window in front has to be the range
    /// itself: otherwise typing lands somewhere nobody asked for.
    /// </summary>
    [Fact]
    public void Keys_only_go_into_the_range_while_it_is_in_front()
    {
        Ui.Run(() =>
        {
            var window = new RangeWindow();
            window.Show();
            var mine = RangeWindow.Where!.Handle;
            McpControl.Allowed = true;
            try
            {
                // Something else in front: refused, and the hint says what to do about it.
                var behind = new WindowsForChecks();
                behind.Add(4242, "别的窗口", "other", "Other");
                behind.Add(mine, "WhaleGenie Range", "WhaleGenie", "AvaloniaWindow");
                var away = new McpControlTools(() => new ChecksDevices
                {
                    Windows = behind,
                    Inputs = new SingleInputRouter(new RecordingInput()),
                });

                var refused = away.TypeText("hi").Result;
                Assert.True(refused.IsError == true);
                Assert.Equal("Range.NotFocused", Code(refused));
                Assert.Equal("Range.NotFocused", Code(away.Key("F5").Result));

                // The range in front: allowed.
                var front = new WindowsForChecks();
                front.Add(mine, "WhaleGenie Range", "WhaleGenie", "AvaloniaWindow");
                var input = new RecordingInput();
                var here = new McpControlTools(() => new ChecksDevices
                {
                    Windows = front,
                    Inputs = new SingleInputRouter(input),
                });
                Assert.True(here.TypeText("hi").Result.IsError is not true);

                // What was typed is not recorded, only how much: the log says the length, not the text.
                Assert.Contains("type 2 0", input.Done);
            }
            finally
            {
                McpControl.Allowed = false;
                window.Close();
            }
        });
    }

    /// <summary>
    /// A range run refuses what a range cannot promise — a script of its own, or a place outside its
    /// own folder — before anything is touched, and says which edge was crossed.
    /// </summary>
    [Fact]
    public void A_range_run_refuses_a_script_and_a_path_outside()
    {
        Ui.RunAsync(async () =>
        {
            var window = new RangeWindow();
            window.Show();
            McpControl.AllowsRun = true;
            try
            {
                await using var host = new McpServerHost(new MainViewModel());
                await host.StartAsync();
                await using var client = await ConnectAsync(host.Endpoint);

                var draft = await Draft(client);
                await Put(client, draft, """
                    {"name":"脚本","steps":[
                      {"type":"script.run","params":{"kind":"ps1","scriptSource":"text","script":"Write-Output 1"}}
                    ]}
                    """);
                var script = await client.CallToolAsync("whalegenie_run_macro",
                    new Dictionary<string, object?> { ["name"] = "脚本", ["draftId"] = draft });
                Assert.True(script.IsError);
                Assert.Equal("Range.ActionRefused", ErrorCode(script));

                await Put(client, draft, """
                    {"name":"写外面","steps":[
                      {"type":"file.writeText","params":{"path":"C:////Windows////nope.txt","fileContent":"x"}}
                    ]}
                    """);
                var outside = await client.CallToolAsync("whalegenie_run_macro",
                    new Dictionary<string, object?> { ["name"] = "写外面", ["draftId"] = draft });
                Assert.True(outside.IsError);
                Assert.Equal("Range.PathOutside", ErrorCode(outside));

                // A place inside the range folder is allowed, and the run really does it.
                var inside = System.IO.Path.Combine(RangePolicy.Folder, "note.txt");
                await Put(client, draft,
                    "{\"name\":\"写里面\",\"steps\":[{\"type\":\"file.writeText\",\"params\":"
                    + "{\"path\":\"" + inside.Replace("\\", "\\\\") + "\",\"fileContent\":\"x\"}}]}");
                await Call(client, "whalegenie_run_macro",
                    new Dictionary<string, object?> { ["name"] = "写里面", ["draftId"] = draft });
                Assert.True(System.IO.File.Exists(inside));
                System.IO.File.Delete(inside);

                await host.StopAsync();
            }
            finally
            {
                McpControl.AllowsRun = false;
                window.Close();
            }

            return true;
        });
    }

    private static string ErrorCode(CallToolResult result)
        => JsonDocument.Parse(result.Content.OfType<TextContentBlock>().First().Text)
            .RootElement.GetProperty("error").GetProperty("code").GetString()!;

    private static async Task<string> Draft(McpClient client)
        => JsonDocument.Parse(await Call(client, "whalegenie_open_project",
            new Dictionary<string, object?>())).RootElement.GetProperty("draftId").GetString()!;

    private static async Task Put(McpClient client, string draft, string macro)
        => await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
        {
            ["draftId"] = draft,
            ["macro"] = JsonNode.Parse(macro),
        });

    private static void Press(RangeWindow window, string id)
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(candidate => AutomationProperties.GetAutomationId(candidate) == id);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static string Code(CallToolResult result)
        => JsonDocument.Parse(result.Content.OfType<TextContentBlock>().First().Text)
            .RootElement.GetProperty("error").GetProperty("code").GetString()!;

    private static async Task<string> Call(McpClient client, string name,
        Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(name, arguments);
        var text = result.Content.OfType<TextContentBlock>().First().Text;
        Assert.True(result.IsError is not true, text);
        return text;
    }

    private static async Task<McpClient> ConnectAsync(string endpoint)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
        });
        return await McpClient.CreateAsync(transport);
    }
}
