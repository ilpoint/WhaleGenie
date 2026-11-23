using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// One bridge, several pages: the shell keeps the project, an overlay keeps a menu or a wheel. A
/// push names its surface, and a surface that is not ready buffers only its own events — mixing
/// those up would send a menu payload to the shell or drop one overlay's state behind another's.
/// A fact every page draws with, like the word table or the theme, is broadcast instead; each page
/// still gets, or buffers, its own copy.
/// </summary>
public class BridgeSurfaceTests
{
    [Fact]
    public async Task A_push_reaches_only_the_surface_it_names()
    {
        var server = new BridgeServer();
        var main = new RecordingChannel();
        var menu = new RecordingChannel();
        server.Attach(BridgeSurface.Main, main);
        server.Attach(BridgeSurface.Menu, menu);
        await ReadyAsync(server, BridgeSurface.Main, main, 1);
        await ReadyAsync(server, BridgeSurface.Menu, menu, 2);

        await server.PushAsync("surface.menu", new { open = true }, BridgePushMode.Latest,
            BridgeSurface.Menu);

        Assert.Contains(menu.Sent, json => json.Contains("surface.menu", StringComparison.Ordinal));
        Assert.DoesNotContain(main.Sent, json => json.Contains("surface.menu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASurface_buffers_its_own_events_until_it_is_ready()
    {
        var server = new BridgeServer();
        var main = new RecordingChannel();
        var menu = new RecordingChannel();
        server.Attach(BridgeSurface.Main, main);
        server.Attach(BridgeSurface.Menu, menu);
        await ReadyAsync(server, BridgeSurface.Main, main, 1);

        // The shell is up and must not receive the overlay's buffered state, and the overlay must
        // not receive the shell's.
        await server.PushAsync("surface.menu", new { open = true }, BridgePushMode.Latest,
            BridgeSurface.Menu);
        await server.PushAsync("project.changed", new { macros = 0 });

        Assert.Empty(menu.Sent);

        await ReadyAsync(server, BridgeSurface.Menu, menu, 2);

        Assert.Contains(menu.Sent, json => json.Contains("surface.menu", StringComparison.Ordinal));
        Assert.DoesNotContain(menu.Sent, json => json.Contains("project.changed", StringComparison.Ordinal));
        Assert.Contains(main.Sent, json => json.Contains("project.changed", StringComparison.Ordinal));
        Assert.DoesNotContain(main.Sent, json => json.Contains("surface.menu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ready_is_answered_on_the_surface_that_asked()
    {
        var server = new BridgeServer();
        var main = new RecordingChannel();
        var wheel = new RecordingChannel();
        server.Attach(BridgeSurface.Main, main);
        server.Attach(BridgeSurface.Wheel, wheel);

        await server.AcceptAsync(BridgeSurface.Wheel, """{"id":7,"method":"bridge.ready"}""");

        Assert.Contains(wheel.Sent, json => json.Contains("\"id\":7", StringComparison.Ordinal));
        Assert.Empty(main.Sent);
        Assert.True(server.IsReadyOn(BridgeSurface.Wheel));
        Assert.False(server.IsReady);
    }

    [Fact]
    public async Task A_request_is_answered_on_the_surface_it_came_from()
    {
        var server = new BridgeServer();
        var main = new RecordingChannel();
        var menu = new RecordingChannel();
        server.Attach(BridgeSurface.Main, main);
        server.Attach(BridgeSurface.Menu, menu);
        server.Register("surface.menu", _ => Task.FromResult<object?>(new { open = true }));

        await server.AcceptAsync(BridgeSurface.Menu, """{"id":3,"method":"surface.menu"}""");

        var answer = Assert.Single(menu.Sent);
        Assert.Contains("\"id\":3", answer, StringComparison.Ordinal);
        Assert.Empty(main.Sent);
    }

    [Fact]
    public void PageReset_is_the_shell_going_away_not_an_overlay_reloading()
    {
        var server = new BridgeServer();
        var resets = 0;
        server.PageReset += () => resets++;

        server.Attach(BridgeSurface.Menu, new RecordingChannel());
        Assert.Equal(0, resets);

        server.Attach(BridgeSurface.Main, new RecordingChannel());
        Assert.Equal(1, resets);
    }

    [Fact]
    public async Task Latest_coalescing_is_per_surface()
    {
        var server = new BridgeServer();
        var main = new RecordingChannel();
        var menu = new RecordingChannel();
        server.Attach(BridgeSurface.Main, main);
        server.Attach(BridgeSurface.Menu, menu);

        // Neither page is ready, so both pushes are held. The older one is replaced on its own
        // surface only.
        await server.PushAsync("surface.state", new { step = 1 }, BridgePushMode.Latest,
            BridgeSurface.Menu);
        await server.PushAsync("surface.state", new { step = 2 }, BridgePushMode.Latest,
            BridgeSurface.Menu);
        await server.PushAsync("surface.state", new { step = 9 }, BridgePushMode.Latest);

        await ReadyAsync(server, BridgeSurface.Menu, menu, 1);
        await ReadyAsync(server, BridgeSurface.Main, main, 2);

        Assert.Contains(menu.Sent, json => json.Contains("\"step\":2", StringComparison.Ordinal));
        Assert.DoesNotContain(menu.Sent, json => json.Contains("\"step\":1", StringComparison.Ordinal));
        Assert.Contains(main.Sent, json => json.Contains("\"step\":9", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_broadcast_reaches_every_page_that_is_listening()
    {
        var server = new BridgeServer();
        var main = new RecordingChannel();
        var menu = new RecordingChannel();
        var wheel = new RecordingChannel();
        server.Attach(BridgeSurface.Main, main);
        server.Attach(BridgeSurface.Menu, menu);
        server.Attach(BridgeSurface.Wheel, wheel);
        await ReadyAsync(server, BridgeSurface.Main, main, 1);
        await ReadyAsync(server, BridgeSurface.Menu, menu, 2);
        await ReadyAsync(server, BridgeSurface.Wheel, wheel, 3);

        await server.PushAllAsync("strings.changed", new { language = "zh-Hans" });

        foreach (var channel in new[] { main, menu, wheel })
        {
            // The answer to ready, then the one broadcast: nothing else, and no duplicate.
            Assert.Equal(2, channel.Sent.Count);
            Assert.Single(channel.Sent,
                json => json.Contains("\"event\":\"strings.changed\"", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_broadcast_buffers_on_a_page_that_is_not_ready_and_replays_it()
    {
        var server = new BridgeServer();
        var main = new RecordingChannel();
        var menu = new RecordingChannel();
        server.Attach(BridgeSurface.Main, main);
        server.Attach(BridgeSurface.Menu, menu);
        await ReadyAsync(server, BridgeSurface.Main, main, 1);

        // The overlay page has not registered its receiver yet, so the broadcast waits in its
        // buffer while the shell gets it straight away. The overlay's own state event is held in
        // the same buffer.
        await server.PushAsync("surface.menu", new { open = true }, BridgePushMode.All,
            BridgeSurface.Menu);
        await server.PushAllAsync("theme.changed", new { mode = "dark" }, BridgePushMode.All);
        await server.PushAllAsync("strings.changed", new { language = "en" }, BridgePushMode.All);

        Assert.Contains(main.Sent,
            json => json.Contains("\"event\":\"theme.changed\"", StringComparison.Ordinal));
        Assert.Empty(menu.Sent);

        await ReadyAsync(server, BridgeSurface.Menu, menu, 2);

        // The answer to ready comes first, then every held-back event in the order it was pushed.
        Assert.Equal(4, menu.Sent.Count);
        Assert.Contains("\"id\":2", menu.Sent[0], StringComparison.Ordinal);
        Assert.Contains("\"event\":\"surface.menu\"", menu.Sent[1], StringComparison.Ordinal);
        Assert.Contains("\"event\":\"theme.changed\"", menu.Sent[2], StringComparison.Ordinal);
        Assert.Contains("\"event\":\"strings.changed\"", menu.Sent[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_broadcast_is_held_for_each_page_separately()
    {
        var server = new BridgeServer();
        var main = new RecordingChannel();
        var menu = new RecordingChannel();
        server.Attach(BridgeSurface.Main, main);
        server.Attach(BridgeSurface.Menu, menu);

        // Neither page is ready. The broadcast is held in both buffers; the shell becoming ready
        // must deliver its copy without counting the overlay's copy as already sent.
        await server.PushAllAsync("strings.changed", new { language = "zh-Hans" });

        await ReadyAsync(server, BridgeSurface.Main, main, 1);
        Assert.Equal(2, main.Sent.Count);
        Assert.Single(main.Sent,
            json => json.Contains("\"event\":\"strings.changed\"", StringComparison.Ordinal));
        Assert.Empty(menu.Sent);

        await ReadyAsync(server, BridgeSurface.Menu, menu, 2);
        Assert.Equal(2, menu.Sent.Count);
        Assert.Single(menu.Sent,
            json => json.Contains("\"event\":\"strings.changed\"", StringComparison.Ordinal));
    }

    private static async Task ReadyAsync(BridgeServer server, BridgeSurface surface,
        RecordingChannel channel, int id)
    {
        await server.AcceptAsync(surface, $$"""{"id":{{id}},"method":"bridge.ready"}""");
        Assert.Contains(channel.Sent, json => json.Contains($"\"id\":{id}", StringComparison.Ordinal));
    }

    private sealed class RecordingChannel : IBridgeChannel
    {
        public List<string> Sent { get; } = [];

        public event Action<string>? MessageFromWeb
        {
            add { }
            remove { }
        }

        public Task SendToWebAsync(string json)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }
    }
}
