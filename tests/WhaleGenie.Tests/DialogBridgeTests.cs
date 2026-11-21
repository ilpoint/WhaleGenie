using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;

namespace WhaleGenie.Tests;

/// <summary>
/// The page question has to fail closed: no page or a page that never answers must not leave a
/// delete, a close or an exit waiting for a dialog that will never come back.
/// </summary>
public class DialogBridgeTests
{
    [Fact]
    public async Task Without_a_ready_page_the_question_is_answered_no()
    {
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        var dialogs = new DialogBridge();
        server.Attach(channel);
        server.Register(dialogs);

        var answer = await dialogs.ConfirmAsync("title", "message", "yes", "no");

        Assert.False(answer);
        Assert.Empty(channel.Sent);
    }

    [Fact]
    public async Task A_page_that_never_answers_times_out_no()
    {
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        var dialogs = new DialogBridge { AnswerTimeout = TimeSpan.FromMilliseconds(20) };
        server.Attach(channel);
        server.Register(dialogs);
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");

        var answer = await dialogs.ConfirmAsync("title", "message", "yes", "no");

        Assert.False(answer);
        Assert.Contains(channel.Sent, json => json.Contains("\"event\":\"dialog.request\""));
    }

    [Fact]
    public async Task An_answer_comes_back_to_the_caller()
    {
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        var dialogs = new DialogBridge { AnswerTimeout = TimeSpan.FromSeconds(2) };
        server.Attach(channel);
        server.Register(dialogs);
        await server.AcceptAsync("""{"id":1,"method":"bridge.ready"}""");
        channel.Sent.Clear();

        var asking = dialogs.ConfirmAsync("title", "message", "yes", "no");
        var request = JsonDocument.Parse(channel.Sent.Single()).RootElement;
        var id = request.GetProperty("payload").GetProperty("requestId").GetInt32();

        await server.AcceptAsync(JsonSerializer.Serialize(new
        {
            id = 2,
            method = "dialog.respond",
            @params = new { requestId = id, accepted = true },
        }));

        Assert.True(await asking);
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
