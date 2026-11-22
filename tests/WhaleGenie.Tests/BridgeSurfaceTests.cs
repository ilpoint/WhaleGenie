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
