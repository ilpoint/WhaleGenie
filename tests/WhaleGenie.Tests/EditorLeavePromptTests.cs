using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;

namespace WhaleGenie.Tests;

/// <summary>
/// The host asks the page about open macros. The prompt has to survive a page that is not up (the
/// caller falls back to the native question), a page that answers, a page that never answers, and
/// a page that reloads under the question. Two questions at once must not be collapsed into one.
/// </summary>
public class EditorLeavePromptTests
{
    [Fact]
    public async Task Without_a_ready_page_the_host_is_told_to_fall_back()
    {
        var server = new BridgeServer();
        server.Attach(new Channel());
        using var prompt = new EditorLeavePrompt(TimeSpan.FromMilliseconds(50));
        prompt.Register(server);

        Assert.Null(await prompt.AskAsync(["Macro 1"]));
    }

    [Fact]
    public async Task The_page_answer_wakes_the_waiting_host()
    {
        var server = new BridgeServer();
        var channel = new Channel();
        server.Attach(channel);
        using var prompt = new EditorLeavePrompt(TimeSpan.FromSeconds(2));
        prompt.Register(server);
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");

        var asking = prompt.AskAsync(["Macro 1"]);
        var asked = await Until(channel, "editor.leaveAsked");
        await Answer(server, asked.GetProperty("requestId").GetInt32(), leave: true);

        Assert.True(await asking);
    }

    [Fact]
    public async Task A_page_that_does_not_answer_keeps_the_window_open()
    {
        var server = new BridgeServer();
        var channel = new Channel();
        server.Attach(channel);
        using var prompt = new EditorLeavePrompt(TimeSpan.FromMilliseconds(30));
        prompt.Register(server);
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");

        var asking = prompt.AskAsync(["Macro 1"]);
        Assert.False(await asking);
        var asked = await Until(channel, "editor.leaveAsked");
        var cancelled = await Until(channel, "editor.leaveCancelled");
        Assert.Equal(asked.GetProperty("requestId").GetInt32(),
            cancelled.GetProperty("requestId").GetInt32());
    }

    [Fact]
    public async Task A_page_reset_hands_the_question_back_to_the_native_fallback()
    {
        var server = new BridgeServer();
        var channel = new Channel();
        server.Attach(channel);
        using var prompt = new EditorLeavePrompt(TimeSpan.FromSeconds(5));
        prompt.Register(server);
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");

        var asking = prompt.AskAsync(["Macro 1"]);
        await Until(channel, "editor.leaveAsked");
        server.Attach(channel);

        Assert.Null(await asking);
    }

    [Fact]
    public async Task Two_questions_are_pushed_separately()
    {
        var server = new BridgeServer();
        var channel = new Channel();
        server.Attach(channel);
        using var prompt = new EditorLeavePrompt(TimeSpan.FromSeconds(2));
        prompt.Register(server);
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");

        var first = prompt.AskAsync(["first"]);
        var second = prompt.AskAsync(["second"]);
        var asked = await UntilCount(channel, "editor.leaveAsked", 2);
        Assert.NotEqual(asked[0].GetProperty("requestId").GetInt32(),
            asked[1].GetProperty("requestId").GetInt32());

        await Answer(server, asked[0].GetProperty("requestId").GetInt32(), leave: true);
        await Answer(server, asked[1].GetProperty("requestId").GetInt32(), leave: false);

        Assert.True(await first);
        Assert.False(await second);
    }

    private static Task Answer(BridgeServer server, int requestId, bool leave)
        => server.AcceptAsync(JsonSerializer.Serialize(new
        {
            id = 100 + requestId,
            method = "editor.leaveAnswered",
            @params = new { requestId, leave },
        }));

    private static async Task<JsonElement> Until(Channel channel, string @event)
        => Assert.Single(await UntilCount(channel, @event, 1));

    private static async Task<List<JsonElement>> UntilCount(Channel channel, string @event, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var found = channel.Events(@event);
            if (found.Count >= count)
            {
                return found;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("the prompt did not push " + @event);
    }

    private sealed class Channel : IBridgeChannel
    {
        private readonly List<string> _sent = [];

        public event Action<string>? MessageFromWeb;

        public Task SendToWebAsync(string json)
        {
            lock (_sent)
            {
                _sent.Add(json);
            }

            return Task.CompletedTask;
        }

        public List<JsonElement> Events(string @event)
        {
            List<string> sent;
            lock (_sent)
            {
                sent = [.. _sent];
            }

            var found = new List<JsonElement>();
            foreach (var text in sent)
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("event", out var name)
                    && name.GetString() == @event)
                {
                    found.Add(document.RootElement.GetProperty("payload").Clone());
                }
            }

            return found;
        }

        public void Receive(string json) => MessageFromWeb?.Invoke(json);
    }
}
