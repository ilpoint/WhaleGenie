using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

/// <summary>
/// The bridge routing: what a page's envelope turns into, what comes back, and when events are
/// allowed through. A channel that records what it was handed is the whole stand-in for the web
/// control, because the server must not know there is one.
/// </summary>
public class BridgeServerTests
{
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

    /// <summary>A channel that holds the next send open so a second push can queue behind it.</summary>
    private sealed class GatedChannel : IBridgeChannel
    {
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Sent { get; } = [];

        public event Action<string>? MessageFromWeb;

        public bool Block { get; set; }

        public Task Started => _started.Task;

        public async Task SendToWebAsync(string json)
        {
            Sent.Add(json);
            if (Block)
            {
                _started.TrySetResult();
                await _gate.Task;
            }
        }

        public void Release() => _gate.TrySetResult();

        public void Receive(string json) => MessageFromWeb?.Invoke(json);
    }

    [Fact]
    public async Task A_registered_method_answers_with_its_result()
    {
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        server.Attach(channel);
        server.Register("app.info", _ => Task.FromResult<object?>(new { version = "1.2.3" }));

        await server.AcceptAsync("""{"id":7,"method":"app.info"}""");

        var answer = JsonDocument.Parse(channel.Sent.Single()).RootElement;
        Assert.Equal(7, answer.GetProperty("id").GetInt32());
        Assert.True(answer.GetProperty("ok").GetBoolean());
        Assert.Equal("1.2.3", answer.GetProperty("result").GetProperty("version").GetString());
    }

    [Fact]
    public async Task An_unknown_method_answers_NoSuchMethod()
    {
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        server.Attach(channel);

        await server.AcceptAsync("""{"id":1,"method":"not.a.method"}""");

        var answer = JsonDocument.Parse(channel.Sent.Single()).RootElement;
        Assert.False(answer.GetProperty("ok").GetBoolean());
        Assert.Equal(BridgeErrors.NoSuchMethod, answer.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_rejection_a_handler_meant_is_answered_with_its_code()
    {
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        server.Attach(channel);
        server.Register("theme.set", _ => throw new BridgeFailureException(BridgeErrors.BadArgument, "bad mode"));

        await server.AcceptAsync("""{"id":3,"method":"theme.set","params":{"mode":"meh"}}""");

        var answer = JsonDocument.Parse(channel.Sent.Single()).RootElement;
        Assert.Equal(BridgeErrors.BadArgument, answer.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("bad mode", answer.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task An_unexpected_failure_answers_Internal_and_is_recorded()
    {
        var log = Path.Combine(Path.GetTempPath(), $"bridge-test-{Guid.NewGuid():N}.log");
        AppDiagnostics.FilePathOverride = log;
        try
        {
            var channel = new RecordingChannel();
            var server = new BridgeServer();
            server.Attach(channel);
            server.Register("app.quit", _ => throw new InvalidOperationException("boom"));

            await server.AcceptAsync("""{"id":4,"method":"app.quit"}""");

            var answer = JsonDocument.Parse(channel.Sent.Single()).RootElement;
            Assert.Equal(BridgeErrors.Internal, answer.GetProperty("error").GetProperty("code").GetString());
            Assert.True(File.Exists(log));
        }
        finally
        {
            AppDiagnostics.FilePathOverride = null;
            File.Delete(log);
        }
    }

    [Fact]
    public async Task Events_buffer_until_the_page_is_ready()
    {
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        server.Attach(channel);

        await server.PushAsync("theme.changed", new { mode = "dark" });
        Assert.Empty(channel.Sent);

        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");

        Assert.Equal(2, channel.Sent.Count);
        var response = JsonDocument.Parse(channel.Sent[0]).RootElement;
        Assert.Equal(1, response.GetProperty("id").GetInt32());
        var pushed = JsonDocument.Parse(channel.Sent[1]).RootElement;
        Assert.Equal("theme.changed", pushed.GetProperty("event").GetString());
    }

    [Fact]
    public async Task Events_are_coalesced_to_the_latest_of_each_name()
    {
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        server.Attach(channel);

        await server.PushAsync("theme.changed", new { mode = "dark" });
        await server.PushAsync("theme.changed", new { mode = "light" });
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");

        Assert.Equal(2, channel.Sent.Count);
        var pushed = JsonDocument.Parse(channel.Sent[1]).RootElement;
        Assert.Equal("light", pushed.GetProperty("payload").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Ready_pushes_coalesce_to_the_latest_while_one_is_in_flight()
    {
        var channel = new GatedChannel();
        var server = new BridgeServer();
        server.Attach(channel);
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        channel.Block = true;

        var first = server.PushAsync("project.changed", new { revision = 1 });
        await channel.Started;
        var second = server.PushAsync("project.changed", new { revision = 2 });
        var third = server.PushAsync("project.changed", new { revision = 3 });
        channel.Release();

        await Task.WhenAll(first, second, third);

        var events = channel.Sent
            .Where(json => json.Contains("\"event\":\"project.changed\"", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, events.Count);
        Assert.Contains("\"revision\":1", events[0], StringComparison.Ordinal);
        Assert.Contains("\"revision\":3", events[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Streaming_pushes_keep_every_payload_while_one_is_in_flight()
    {
        var channel = new GatedChannel();
        var server = new BridgeServer();
        server.Attach(channel);
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        channel.Block = true;

        var first = server.PushAsync("run.log", new { n = 1 }, BridgePushMode.All);
        await channel.Started;
        var second = server.PushAsync("run.log", new { n = 2 }, BridgePushMode.All);
        var third = server.PushAsync("run.log", new { n = 3 }, BridgePushMode.All);
        channel.Release();

        await Task.WhenAll(first, second, third);

        var events = channel.Sent
            .Where(json => json.Contains("\"event\":\"run.log\"", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, events.Count);
        Assert.Contains("\"n\":2", events[1], StringComparison.Ordinal);
        Assert.Contains("\"n\":3", events[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pending_event_can_be_dropped_by_the_fetch_that_replaces_it()
    {
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        server.Attach(channel);

        await server.PushAsync("project.changed", new { revision = 1 });
        server.DropPending("project.changed");
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");

        Assert.Single(channel.Sent);
    }

    [Fact]
    public async Task Attaching_again_buffers_until_the_next_ready()
    {
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        server.Attach(channel);
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        channel.Sent.Clear();

        server.Attach(channel);
        await server.PushAsync("window.changed", new { state = "normal" });
        Assert.Empty(channel.Sent);

        await server.AcceptAsync("""{"id":2,"method":"bridge.ready"}""");
        Assert.Equal(2, channel.Sent.Count);
        Assert.Equal(2, JsonDocument.Parse(channel.Sent[0]).RootElement.GetProperty("id").GetInt32());
        Assert.Equal("window.changed", JsonDocument.Parse(channel.Sent[1]).RootElement.GetProperty("event").GetString());
    }

    [Fact]
    public async Task Text_that_is_not_a_request_is_dropped()
    {
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        server.Attach(channel);

        await server.AcceptAsync("this is not json");
        await server.AcceptAsync("""{"event":"someone forwarded a push into the request door"}""");

        Assert.Empty(channel.Sent);
    }

    [Fact]
    public void A_method_registered_twice_is_a_bug()
    {
        var server = new BridgeServer();
        server.Register("app.info", _ => Task.FromResult<object?>(null));

        Assert.Throws<InvalidOperationException>(() =>
            server.Register("app.info", _ => Task.FromResult<object?>(null)));
    }
}
