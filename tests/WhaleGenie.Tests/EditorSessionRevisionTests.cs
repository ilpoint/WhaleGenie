using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// The number the page orders editor snapshots by belongs to the whole strip, not to one
/// document. A macro opened after another one has moved on must still be allowed to replace what
/// the page is showing, or its name, tab and dirty dot would stay those of the previous document.
/// </summary>
public class EditorSessionRevisionTests
{
    [Fact]
    public async Task A_second_document_gets_a_higher_revision_than_the_first()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "first" });
        workspace.AddMacro(new MacroItem { Name = "second" });
        var server = new BridgeServer();
        var channel = new Channel();
        server.Attach(channel);
        new EditorSessionBridge(new WhaleGenie.Editor.EditorDocumentService(workspace), workspace).Register(server);

        var first = (await Call(server, channel, """{"id":1,"method":"editor.session","params":{"kind":"open","name":"first"}}"""))
            .GetProperty("result");
        var firstRevision = first.GetProperty("revision").GetInt32();

        // Push the first document's own counter well past a fresh one, the way editing it would.
        var latest = firstRevision;
        for (var index = 0; index < 5; index++)
        {
            var again = (await Call(server, channel,
                """{"id":2,"method":"editor.sessionState"}""")).GetProperty("result");
            latest = again.GetProperty("revision").GetInt32();
        }

        var second = (await Call(server, channel, """{"id":3,"method":"editor.session","params":{"kind":"open","name":"second"}}"""))
            .GetProperty("result");
        var secondRevision = second.GetProperty("revision").GetInt32();

        Assert.True(secondRevision > latest,
            $"the second document answered with revision {secondRevision}, behind {latest}");
        Assert.Equal("second", second.GetProperty("name").GetString());
        Assert.Equal(second.GetProperty("activeDocumentId").GetString(),
            second.GetProperty("documentId").GetString());
        Assert.Equal(2, second.GetProperty("tabs").GetArrayLength());
    }

    [Fact]
    public async Task A_new_macro_has_its_real_default_name_in_the_tabs()
    {
        var workspace = new ProjectWorkspace();
        var server = new BridgeServer();
        var channel = new Channel();
        server.Attach(channel);
        new EditorSessionBridge(new WhaleGenie.Editor.EditorDocumentService(workspace), workspace).Register(server);

        var result = (await Call(server, channel,
            """{"id":1,"method":"editor.session","params":{"kind":"new"}}""")).GetProperty("result");

        Assert.Equal("Macro 1", result.GetProperty("name").GetString());
        var tab = Assert.Single(result.GetProperty("tabs").EnumerateArray());
        Assert.Equal("Macro 1", tab.GetProperty("name").GetString());
        Assert.Equal(result.GetProperty("activeDocumentId").GetString(),
            tab.GetProperty("documentId").GetString());
    }

    [Fact]
    public async Task Confirming_an_action_answers_with_the_whole_editor_snapshot()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "note" });
        var server = new BridgeServer();
        var channel = new Channel();
        server.Attach(channel);
        new EditorSessionBridge(new WhaleGenie.Editor.EditorDocumentService(workspace), workspace)
            .Register(server);
        await Call(server, channel, """{"id":1,"method":"bridge.ready"}""");
        var opened = (await Call(server, channel,
            """{"id":2,"method":"editor.session","params":{"kind":"open","name":"note"}}"""))
            .GetProperty("result");
        var documentId = opened.GetProperty("activeDocumentId").GetString();

        await Call(server, channel,
            """{"id":3,"method":"editor.actionOpen","params":{"type":"control.note"}}""");
        var confirmed = (await Call(server, channel,
            """{"id":4,"method":"editor.actionConfirm"}""")).GetProperty("result");

        Assert.Equal(1, confirmed.GetProperty("stepCount").GetInt32());
        Assert.Equal("control.note", confirmed.GetProperty("steps")[0].GetProperty("type").GetString());
        Assert.Equal(string.Empty, confirmed.GetProperty("steps")[0].GetProperty("detail").GetString());
        Assert.Equal(JsonValueKind.Object, confirmed.GetProperty("actionForm").ValueKind);
        Assert.False(confirmed.GetProperty("actionForm").GetProperty("open").GetBoolean());
        var tab = Assert.Single(confirmed.GetProperty("tabs").EnumerateArray());
        Assert.Equal(documentId, tab.GetProperty("documentId").GetString());
        Assert.Equal(documentId, confirmed.GetProperty("activeDocumentId").GetString());
        Assert.True(confirmed.GetProperty("revision").GetInt32()
            > opened.GetProperty("revision").GetInt32());
    }

    private static async Task<JsonElement> Call(BridgeServer server, Channel channel, string request)
    {
        channel.Sent.Clear();
        var id = JsonDocument.Parse(request).RootElement.GetProperty("id").GetInt32();
        await server.AcceptAsync(request);
        var answer = channel.Sent.Last(text =>
            text.Contains($"\"id\":{id}", StringComparison.Ordinal)
            && text.Contains("\"ok\"", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(answer);
        return document.RootElement.Clone();
    }

    private sealed class Channel : IBridgeChannel
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
