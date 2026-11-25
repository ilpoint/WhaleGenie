using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// A close the page thought was safe but the workspace knows is not: it comes back as the dedicated
/// "ask the person" code, with the document still open, so the page can draw the save/discard/cancel
/// question instead of a red banner. Only an explicit discard actually throws the work away.
/// </summary>
public class EditorSessionCloseTests
{
    [Fact]
    public async Task Closing_a_dirty_document_without_discard_asks_the_person_and_keeps_it()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "target", Description = "before" });
        var server = new BridgeServer();
        var channel = new Channel();
        server.Attach(channel);
        new EditorSessionBridge(new WhaleGenie.Editor.EditorDocumentService(workspace), workspace).Register(server);

        var opened = await Call(server, channel,
            """{"id":1,"method":"editor.session","params":{"kind":"open","name":"target"}}""");
        var documentId = opened.GetProperty("result").GetProperty("activeDocumentId").GetString();
        await Call(server, channel, """{"id":2,"method":"editor.session","params":{"kind":"patch","description":"edited"}}""");

        var refused = await Call(server, channel,
            "{\"id\":3,\"method\":\"editor.session\",\"params\":{\"kind\":\"close\",\"documentId\":\""
            + documentId + "\"}}");

        Assert.False(refused.GetProperty("ok").GetBoolean());
        Assert.Equal(BridgeErrors.EditorUnsaved,
            refused.GetProperty("error").GetProperty("code").GetString());
        var unsaved = await Call(server, channel,
            """{"id":4,"method":"editor.unsavedDocuments"}""");
        Assert.Contains("target",
            unsaved.GetProperty("result").GetProperty("names").EnumerateArray()
                .Select(name => name.GetString()));
        Assert.Equal("before", workspace.Macros[0].Description);
    }

    [Fact]
    public async Task Discarding_the_document_closes_it_and_leaves_the_project_alone()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "target", Description = "before" });
        var server = new BridgeServer();
        var channel = new Channel();
        server.Attach(channel);
        new EditorSessionBridge(new WhaleGenie.Editor.EditorDocumentService(workspace), workspace).Register(server);

        var opened = await Call(server, channel,
            """{"id":1,"method":"editor.session","params":{"kind":"open","name":"target"}}""");
        var documentId = opened.GetProperty("result").GetProperty("activeDocumentId").GetString();
        await Call(server, channel, """{"id":2,"method":"editor.session","params":{"kind":"patch","description":"edited"}}""");

        var closed = await Call(server, channel,
            "{\"id\":3,\"method\":\"editor.session\",\"params\":{\"kind\":\"close\",\"documentId\":\""
            + documentId + "\",\"discard\":true}}");

        Assert.True(closed.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, closed.GetProperty("result").ValueKind);
        Assert.Equal("before", workspace.Macros[0].Description);
        var state = await Call(server, channel,
            """{"id":4,"method":"editor.sessionState"}""");
        Assert.Equal(JsonValueKind.Null, state.GetProperty("result").ValueKind);
    }

    private static async Task<JsonElement> Call(BridgeServer server, Channel channel, string request)
    {
        channel.Sent.Clear();
        await server.AcceptAsync(request);
        using var document = JsonDocument.Parse(Assert.Single(channel.Sent));
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
