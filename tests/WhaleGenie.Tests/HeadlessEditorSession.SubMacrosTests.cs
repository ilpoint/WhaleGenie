using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// A macro's callable children through the document workspace the page talks to, and the wire the
/// page uses to put a step inside a block's child list: a new child reaches the action library and
/// can be called, a project macro can be copied in, a child made while a child document is on
/// screen still lands on the macro that owns the children, a child a call still reaches cannot be
/// removed, and a move that names a list and a place lands there.
/// </summary>
public class HeadlessEditorSessionSubMacroTests
{
    [Fact]
    public void A_new_child_is_a_draft_tab_until_it_is_saved()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "host" });
        var documents = new EditorDocumentSet(workspace);
        var session = documents.Open("host");
        var hostId = Assert.IsType<string>(documents.ActiveId);

        var child = documents.CreateSubmacro(hostId, "helper");

        Assert.Equal("helper", child.Name);
        Assert.DoesNotContain("helper", session.Snapshot().SubMacros);
        Assert.Contains(hostId, documents.Tabs.Select(tab => tab.DocumentId));

        var saved = child.Save();
        Assert.True(saved.Succeeded);
        Assert.Contains("helper", session.Snapshot().SubMacros);

        session.OpenActionDraft("control.runMacro");
        var macros = session.ActionForm.Form.Parameters
            .Single(parameter => parameter.Definition.Name == "macro")
            .Macros;
        Assert.Contains("helper", macros);
        session.CancelActionDraft();

        session.AddStep("control.runMacro", null, null,
            new Dictionary<string, string> { ["macro"] = "helper" });

        var call = Assert.Single(session.Snapshot().Steps);
        Assert.Equal("control.runMacro", call.Type);
        Assert.Equal("helper",
            Assert.Single(call.Parameters, parameter => parameter.Name == "macro").Value);
    }

    [Fact]
    public async Task Importing_a_project_macro_opens_it_as_a_child_without_a_call_step()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "host" });
        var source = new MacroItem { Name = "shared" };
        source.Steps.Add(new MacroStep { Type = "control.log" });
        workspace.AddMacro(source);

        var channel = new RecordingChannel();
        var server = new BridgeServer();
        server.Attach(channel);
        new EditorSessionBridge(new WhaleGenie.Editor.EditorDocumentService(workspace), workspace).Register(server);

        await Call(server, """{"id":1,"method":"editor.session","params":{"kind":"open","name":"host"}}""");
        await Call(server, """
            {"id":2,"method":"editor.session","params":
              {"kind":"subMacroImport","source":"project","reference":"shared"}}
            """);

        var answer = channel.Sent[^1].GetProperty("result");
        Assert.Equal("shared 2", answer.GetProperty("name").GetString());
        Assert.True(answer.GetProperty("isSubMacro").GetBoolean());
        Assert.Contains(answer.GetProperty("rootSubMacros").EnumerateArray(),
            name => name.GetString() == "shared 2");
        Assert.DoesNotContain(answer.GetProperty("steps").EnumerateArray(),
            step => step.GetProperty("type").GetString() == "control.runMacro");
        Assert.Single(answer.GetProperty("steps").EnumerateArray());
    }

    [Fact]
    public void A_project_macro_can_be_copied_in_and_saved_with_its_steps()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "host" });
        var library = new MacroItem { Name = "library" };
        library.Steps.Add(new MacroStep { Type = "control.log" });
        workspace.AddMacro(library);
        var documents = new EditorDocumentSet(workspace);
        var session = documents.Open("host");

        var copied = session.CopySubMacro("library", "renamed");

        Assert.Contains("renamed", copied.SubMacros);
        Assert.Equal("renamed", Assert.Single(copied.Steps).Parameters
            .Single(parameter => parameter.Name == "macro")
            .Value);

        Assert.True(session.Save().Succeeded);
        var saved = workspace.Macros.Single(macro => macro.Name == "host");
        Assert.Equal("control.log",
            Assert.Single(saved.SubMacros.Single(macro => macro.Name == "renamed").Steps).Type);
        Assert.Equal("control.log", Assert.Single(library.Steps).Type);
    }

    [Fact]
    public void A_child_command_inside_a_child_document_lands_on_the_root()
    {
        var workspace = new ProjectWorkspace();
        var root = new MacroItem { Name = "host" };
        root.SubMacros.Add(new MacroItem { Name = "helper" });
        workspace.AddMacro(root);
        var documents = new EditorDocumentSet(workspace);
        documents.Open("host");
        var hostId = Assert.IsType<string>(documents.ActiveId);
        var child = documents.OpenSubmacro(hostId, "helper");

        var deeper = documents.CreateSubmacro(documents.ActiveId, "deeper");
        Assert.DoesNotContain("deeper", child.Snapshot().RootSubMacros!);
        Assert.True(deeper.Save().Succeeded);
        Assert.Contains("deeper", documents.Activate(hostId).Snapshot().SubMacros);

        child.DeleteSubMacro("deeper");

        Assert.DoesNotContain("deeper", documents.Activate(hostId).Snapshot().SubMacros);
    }

    [Fact]
    public void A_child_still_called_is_refused_and_a_free_one_is_removed()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "host" });
        var documents = new EditorDocumentSet(workspace);
        var session = documents.Open("host");
        var hostId = Assert.IsType<string>(documents.ActiveId);
        var child = documents.CreateSubmacro(hostId, "helper");
        child.Save();
        session.AddStep("control.runMacro", null, null,
            new Dictionary<string, string> { ["macro"] = "helper" });

        Assert.Throws<InvalidOperationException>(() => session.DeleteSubMacro("helper"));
        Assert.Contains("helper", session.Snapshot().SubMacros);

        session.RemoveStep(Assert.Single(session.Snapshot().Steps).Id);
        session.DeleteSubMacro("helper");

        Assert.DoesNotContain("helper", session.Snapshot().SubMacros);
        Assert.Throws<KeyNotFoundException>(() => session.DeleteSubMacro("helper"));
    }

    /// <summary>
    /// The same three commands as the page sends them, so the codes the web reads are checked on
    /// the wire rather than only in the session: a name already taken is answered with the next
    /// one, an unknown source with NoSuchMacro, and a child still called with BadArgument and the
    /// word the page shows.
    /// </summary>
    [Fact]
    public async Task The_bridge_answers_the_child_commands_with_the_contract_codes()
    {
        var workspace = new ProjectWorkspace();
        var root = new MacroItem { Name = "host" };
        root.SubMacros.Add(new MacroItem { Name = "helper" });
        root.Steps.Add(SubMacroEditor.CallStep(root.SubMacros[0]));
        workspace.AddMacro(root);

        var channel = new RecordingChannel();
        var server = new BridgeServer();
        server.Attach(channel);
        new EditorSessionBridge(new WhaleGenie.Editor.EditorDocumentService(workspace), workspace).Register(server);

        await Call(server, """{"id":1,"method":"editor.session","params":{"kind":"open","name":"host"}}""");
        await Call(server,
            """{"id":2,"method":"editor.session","params":{"kind":"subMacroCreate","name":"helper"}}""");
        var created = channel.Sent[^1];
        Assert.True(created.GetProperty("ok").GetBoolean());
        var createdSnapshot = created.GetProperty("result");
        Assert.Equal("helper 2", createdSnapshot.GetProperty("name").GetString());
        Assert.Empty(createdSnapshot.GetProperty("subMacros").EnumerateArray());
        Assert.Contains(createdSnapshot.GetProperty("rootSubMacros").EnumerateArray(),
            name => name.GetString() == "helper");
        Assert.Equal(2, createdSnapshot.GetProperty("tabs").GetArrayLength());

        var documentId = createdSnapshot.GetProperty("documentId").GetString();
        await Call(server, JsonSerializer.Serialize(new
        {
            id = 3,
            method = "editor.save",
            @params = new { documentId },
        }));
        var saved = channel.Sent[^1].GetProperty("result").GetProperty("snapshot");
        Assert.Contains(saved.GetProperty("rootSubMacros").EnumerateArray(),
            name => name.GetString() == "helper 2");

        await Call(server,
            """{"id":4,"method":"editor.session","params":{"kind":"subMacroCopy","from":"nowhere"}}""");
        var missing = channel.Sent[^1];
        Assert.Equal(BridgeErrors.NoSuchMacro,
            missing.GetProperty("error").GetProperty("code").GetString());

        await Call(server,
            """{"id":5,"method":"editor.session","params":{"kind":"subMacroDelete","name":"helper"}}""");
        var refused = channel.Sent[^1];
        Assert.Equal(BridgeErrors.BadArgument,
            refused.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(Strings.Format("Bridge.SubMacroInUse", "helper"),
            refused.GetProperty("error").GetProperty("message").GetString());
    }

    /// <summary>
    /// A move that names the block, its list and the place in it is the drop a page makes into a
    /// branch with no row of its own; leaving the list and the place out lands it at the end of the
    /// block's first list.
    /// </summary>
    [Fact]
    public async Task The_bridge_moves_a_step_into_a_named_child_list()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "moves" });
        var channel = new RecordingChannel();
        var server = new BridgeServer();
        server.Attach(channel);
        new EditorSessionBridge(new WhaleGenie.Editor.EditorDocumentService(workspace), workspace).Register(server);

        await Call(server, """{"id":1,"method":"editor.session","params":{"kind":"open","name":"moves"}}""");
        await Call(server, """{"id":2,"method":"editor.step","params":{"kind":"add","type":"control.try"}}""");
        var blockId = channel.Sent[1].GetProperty("result").GetProperty("steps")[0]
            .GetProperty("id").GetString()!;
        await Call(server,
            """{"id":3,"method":"editor.step","params":{"kind":"add","type":"control.delay"}}""");
        var delayId = channel.Sent[2].GetProperty("result").GetProperty("steps")[1]
            .GetProperty("id").GetString()!;

        await Call(server, MoveRequest(4, blockId, delayId, "catch", 0));

        var block = channel.Sent[3].GetProperty("result").GetProperty("steps")[0];
        Assert.Equal("control.delay", Assert.Single(Section(block, "catch")).GetProperty("type")
            .GetString());
        Assert.Empty(Section(block, "body"));

        await Call(server,
            """{"id":5,"method":"editor.step","params":{"kind":"add","type":"control.log"}}""");
        var logId = channel.Sent[4].GetProperty("result").GetProperty("steps")[1]
            .GetProperty("id").GetString()!;

        await Call(server, MoveRequest(6, blockId, logId));

        var moved = channel.Sent[5].GetProperty("result").GetProperty("steps")[0];
        Assert.Equal("control.log", Assert.Single(Section(moved, "body")).GetProperty("type")
            .GetString());
    }

    /// <summary>One editor.steps move as the page sends it, built rather than hand-escaped.</summary>
    private static string MoveRequest(int id, string parentId, string stepId, string? list = null,
        int? index = null)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = "editor.steps",
            ["params"] = new Dictionary<string, object?>
            {
                ["kind"] = "move",
                ["steps"] = new[] { stepId },
                ["parentId"] = parentId,
                ["list"] = list,
                ["index"] = index,
            },
        });

    private static IEnumerable<JsonElement> Section(JsonElement block, string name)
        => block.GetProperty("sections").EnumerateArray()
            .Single(section => section.GetProperty("name").GetString() == name)
            .GetProperty("steps")
            .EnumerateArray();

    private static Task Call(BridgeServer server, string json) => server.AcceptAsync(json);

    private sealed class RecordingChannel : IBridgeChannel
    {
        public List<JsonElement> Sent { get; } = [];

        public event Action<string>? MessageFromWeb;

        public Task SendToWebAsync(string json)
        {
            using var document = JsonDocument.Parse(json);
            Sent.Add(document.RootElement.Clone());
            return Task.CompletedTask;
        }

        public void Receive(string json) => MessageFromWeb?.Invoke(json);
    }
}
