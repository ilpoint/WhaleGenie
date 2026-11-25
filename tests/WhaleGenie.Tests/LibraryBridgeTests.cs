using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// Publishing a macro into the public library. The thing that matters is the shape: a public entry
/// is a callable document with no trigger, recorder or interface of its own, because those belong
/// to the project that starts it. Publishing the same name again replaces the entry and raises its
/// version only when the definition changed.
/// </summary>
public class LibraryBridgeTests
{
    [Fact]
    public async Task A_published_macro_keeps_the_callable_shape_and_no_trigger()
    {
        var folder = Folder();
        try
        {
            var libraryPath = Path.Combine(folder, "public-macros.wglib");
            var workspace = new ProjectWorkspace();
            var macro = new MacroItem
            {
                Name = "shared",
                Description = "Shared work",
                ToggleKey = "F8",
                RecordMouseMovement = false,
                RecordMouseButtons = false,
            };
            macro.Steps.Add(new MacroStep { Type = "control.log" });
            workspace.AddMacro(macro);

            var server = new BridgeServer();
            var channel = new Channel();
            server.Attach(channel);
            server.Register(new LibraryBridge(workspace, null, libraryPath));

            await server.AcceptAsync("""{"id":1,"method":"library.publish","params":{"name":"shared"}}""");

            var result = JsonDocument.Parse(channel.Sent.Single()).RootElement.GetProperty("result");
            var entry = Assert.Single(result.GetProperty("entries").EnumerateArray());
            Assert.Equal("shared", entry.GetProperty("name").GetString());
            Assert.Equal(1, entry.GetProperty("version").GetInt32());

            var stored = Assert.Single(PublicMacroLibrary.Load(libraryPath));
            Assert.Equal("shared", stored.Macro.Name);
            Assert.Empty(stored.Macro.ToggleKey);
            Assert.True(stored.Macro.RecordMouseMovement);
            Assert.Equal("control.log", Assert.Single(stored.Macro.Steps).Type);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Publishing_the_same_name_again_versions_only_when_it_changed()
    {
        var folder = Folder();
        try
        {
            var libraryPath = Path.Combine(folder, "public-macros.wglib");
            var workspace = new ProjectWorkspace();
            var macro = new MacroItem { Name = "shared", Description = "first" };
            workspace.AddMacro(macro);
            var server = new BridgeServer();
            var channel = new Channel();
            server.Attach(channel);
            server.Register(new LibraryBridge(workspace, null, libraryPath));

            await Publish(server, 1);
            await Publish(server, 2);
            Assert.Equal(1, Version(channel, 2));

            workspace.Macros[0].Description = "second";
            await Publish(server, 3);
            Assert.Equal(2, Version(channel, 3));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Deleting_removes_the_entry_without_touching_the_project()
    {
        var folder = Folder();
        try
        {
            var libraryPath = Path.Combine(folder, "public-macros.wglib");
            var workspace = new ProjectWorkspace();
            workspace.AddMacro(new MacroItem { Name = "shared" });
            var server = new BridgeServer();
            var channel = new Channel();
            server.Attach(channel);
            server.Register(new LibraryBridge(workspace, null, libraryPath));

            await Publish(server, 1);
            var id = JsonDocument.Parse(channel.Sent.Single()).RootElement.GetProperty("result")
                .GetProperty("entries")[0].GetProperty("id").GetString();
            await server.AcceptAsync(JsonSerializer.Serialize(new
            {
                id = 2,
                method = "library.delete",
                @params = new { id },
            }));

            var answer = JsonDocument.Parse(channel.Sent.Last(text => text.Contains("\"id\":2")))
                .RootElement;
            Assert.True(answer.GetProperty("ok").GetBoolean(), answer.ToString());
            var result = answer.GetProperty("result");
            Assert.True(result.TryGetProperty("entries", out var entries), answer.ToString());
            Assert.Empty(entries.EnumerateArray());
            Assert.Single(workspace.Macros);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task The_library_can_start_a_blank_entry_and_import_a_package()
    {
        var folder = Folder();
        try
        {
            var libraryPath = Path.Combine(folder, "public-macros.wglib");
            var workspace = new ProjectWorkspace();
            var server = new BridgeServer();
            var channel = new Channel();
            server.Attach(channel);
            server.Register(new LibraryBridge(workspace, null, libraryPath));

            await server.AcceptAsync("""{"id":1,"method":"library.create","params":{}}""");
            await server.AcceptAsync("""{"id":2,"method":"library.create","params":{}}""");
            var created = Answer(channel, 2).GetProperty("result")
                .GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("name").GetString())
                .ToList();
            Assert.Equal(["公共宏", "公共宏 2"], created);

            var package = Path.Combine(folder, "incoming.wgmacro");
            MacroPackage.Save(package, [new MacroItem { Name = "外部宏" }],
                new ProjectTriggers(), [], "test", null);
            await server.AcceptAsync(JsonSerializer.Serialize(new
            {
                id = 3,
                method = "library.import",
                @params = new { path = package },
            }));

            var imported = Answer(channel, 3).GetProperty("result")
                .GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("name").GetString())
                .ToList();
            Assert.Contains("外部宏", imported);
            Assert.Contains(PublicMacroLibrary.Load(libraryPath),
                entry => entry.Macro.Name == "外部宏");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static JsonElement Answer(Channel channel, int id)
    {
        var text = channel.Sent.Last(candidate => candidate.Contains("\"id\":" + id,
            StringComparison.Ordinal));
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static Task Publish(BridgeServer server, int id)
        => server.AcceptAsync(JsonSerializer.Serialize(new
        {
            id,
            method = "library.publish",
            @params = new { name = "shared" },
        }));

    private static int Version(Channel channel, int id)
    {
        foreach (var text in channel.Sent)
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("id", out var answer)
                && answer.GetInt32() == id)
            {
                return document.RootElement.GetProperty("result").GetProperty("entries")[0]
                    .GetProperty("version").GetInt32();
            }
        }

        throw new InvalidOperationException("no answer for request " + id);
    }

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-library-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
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
