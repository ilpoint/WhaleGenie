using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
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
