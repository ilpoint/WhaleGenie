using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Core.Devices;
using WhaleGenie.Tests.Support;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The one window list both the settings rule form and the editor read. The projection is the
/// contract: what a row carries, the order the desktop gave them in, and the fact that this
/// program never offers itself as a target.
/// </summary>
public class WindowListBridgeTests
{
    [Fact]
    public async Task The_list_carries_the_fields_a_rule_is_built_from()
    {
        var windows = new WindowsForChecks()
            .Add(2, "Untitled - Notepad", "notepad", "Notepad")
            .Place(2, 0, 0, 800, 600)
            .Add(4, "Calculator", "calc", "CalcFrame")
            .Place(4, 100, 100, 320, 240);
        var (server, channel) = Server(windows, new NoPick());

        var state = await Call(server, channel, "window.list");
        var rows = state.GetProperty("windows").EnumerateArray().ToList();

        var first = rows[0];
        Assert.Equal(2, first.GetProperty("handle").GetInt64());
        Assert.Equal("Untitled - Notepad", first.GetProperty("title").GetString());
        Assert.Equal("notepad", first.GetProperty("process").GetString());
        Assert.Equal("Notepad", first.GetProperty("className").GetString());
        Assert.Contains("800", first.GetProperty("detail").GetString());
        Assert.Contains("×", first.GetProperty("detail").GetString());
        Assert.Equal("notepad · Notepad · " + first.GetProperty("detail").GetString(),
            first.GetProperty("summary").GetString());

        Assert.Equal("Calculator", rows[1].GetProperty("title").GetString());
        Assert.Equal(4, rows[1].GetProperty("handle").GetInt64());
    }

    [Fact]
    public async Task The_list_keeps_the_desktop_order_and_leaves_this_program_out()
    {
        var own = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        var windows = new WindowsForChecks()
            .Add(2, "First", "notepad", "Notepad")
            .Add(3, "WhaleGenie", own, "WhaleGenieWindow")
            .Add(4, "Last", "calc", "CalcFrame");
        var (server, channel) = Server(windows, new NoPick());

        var state = await Call(server, channel, "window.list");

        Assert.Equal(new long[] { 2, 4 }, state.GetProperty("windows").EnumerateArray()
            .Select(row => row.GetProperty("handle").GetInt64()).ToArray());
    }

    [Fact]
    public async Task The_list_offers_the_same_matching_ways_a_rule_can_use()
    {
        var (server, channel) = Server(new WindowsForChecks(), new NoPick());

        var state = await Call(server, channel, "window.list");
        var hows = state.GetProperty("hows").EnumerateArray()
            .Select(row => row.GetProperty("value").GetString() ?? string.Empty).ToList();

        Assert.Equal(WindowHow.Values.ToArray(), hows.ToArray());
        Assert.All(state.GetProperty("hows").EnumerateArray(), row =>
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("label").GetString())));
    }

    [Fact]
    public async Task The_pick_delegates_to_the_host_and_projects_its_answer()
    {
        var windows = new WindowsForChecks()
            .Add(2, "Untitled - Notepad", "notepad", "Notepad")
            .Place(2, 0, 0, 800, 600);
        var host = new PicksOnce(new WindowInfo(2, "Untitled - Notepad", new ScreenPoint(0, 0),
            new ScreenSize(800, 600), false, false));
        var (server, channel) = Server(windows, host);

        var answer = await Call(server, channel, "window.pick");

        Assert.Equal(1, host.Calls);
        Assert.Equal(2, answer.GetProperty("handle").GetInt64());
        Assert.Equal("notepad", answer.GetProperty("process").GetString());
        Assert.Equal("Notepad", answer.GetProperty("className").GetString());
    }

    [Fact]
    public async Task A_dismissed_pick_answers_null()
    {
        var (server, channel) = Server(new WindowsForChecks(), new NoPick());

        var answer = await Call(server, channel, "window.pick");

        Assert.Equal(JsonValueKind.Null, answer.ValueKind);
    }

    private static (BridgeServer Server, RecordingChannel Channel) Server(IWindowDevice windows,
        IWindowPickHost picks)
    {
        var server = new BridgeServer();
        server.Register(new WindowListBridge(windows, picks));
        var channel = new RecordingChannel();
        server.Attach(channel);
        return (server, channel);
    }

    private static async Task<JsonElement> Call(BridgeServer server, RecordingChannel channel,
        string method)
    {
        channel.Sent.Clear();
        await server.AcceptAsync(JsonSerializer.Serialize(new { id = 1, method, @params = new { } }));
        var answer = JsonDocument.Parse(Assert.Single(channel.Sent)).RootElement.Clone();
        Assert.True(answer.GetProperty("ok").GetBoolean());
        return answer.GetProperty("result");
    }

    private sealed class NoPick : IWindowPickHost
    {
        public Task<WindowInfo?> PickWindowAsync() => Task.FromResult<WindowInfo?>(null);
    }

    private sealed class PicksOnce(WindowInfo window) : IWindowPickHost
    {
        public int Calls { get; private set; }

        public Task<WindowInfo?> PickWindowAsync()
        {
            Calls++;
            return Task.FromResult<WindowInfo?>(window);
        }
    }

    private sealed class RecordingChannel : IBridgeChannel
    {
        public List<string> Sent { get; } = [];

        public event Action<string>? MessageFromWeb;

        public Task SendToWebAsync(string json)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public void Receive(string json) => MessageFromWeb?.Invoke(json);
    }
}
