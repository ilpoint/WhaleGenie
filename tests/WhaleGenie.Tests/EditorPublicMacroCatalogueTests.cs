using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// The editor's public-macro catalogue keeps the two shelves separate. The same id at two versions
/// is two rows, each with its source, so the page can never quietly choose a newer copy for the
/// person; the source setting filters which shelves are listed at all.
/// </summary>
public class EditorPublicMacroCatalogueTests
{
    [Fact]
    public async Task The_same_id_at_two_versions_is_two_labelled_rows()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-catalogue-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var localPath = Path.Combine(folder, "public-macros" + MacroPackage.Extension);
            PublicMacroLibrary.Save(localPath,
                [new PublicMacroEntry("same-id", 7, new MacroItem { Name = "shared" })], "test");
            var workspace = new ProjectWorkspace();
            workspace.MergeLibrary(
                [new PublicMacroEntry("same-id", 3, new MacroItem { Name = "shared" })]);
            var settings = new AppSettings(Path.Combine(folder, "settings.json"));
            var server = new BridgeServer();
            var channel = new Channel();
            server.Attach(channel);
            new EditorSessionBridge(new WhaleGenie.Editor.EditorDocumentService(workspace), workspace,
                publicLibrary: new FilePublicLibraryStore(localPath),
                appSettings: settings).Register(server);

            var catalogue = await Call(server, channel,
                """{"id":1,"method":"editor.sessionActions"}""");
            var rows = catalogue.GetProperty("result").GetProperty("publicMacros")
                .EnumerateArray().ToList();

            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, row => row.GetProperty("version").GetInt32() == 3
                && row.GetProperty("source").GetString() == "project");
            Assert.Contains(rows, row => row.GetProperty("version").GetInt32() == 7
                && row.GetProperty("source").GetString() == "local");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task The_source_setting_hides_the_other_shelf()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-catalogue-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var localPath = Path.Combine(folder, "public-macros" + MacroPackage.Extension);
            PublicMacroLibrary.Save(localPath,
                [new PublicMacroEntry("local-id", 1, new MacroItem { Name = "local" })], "test");
            var workspace = new ProjectWorkspace();
            workspace.MergeLibrary(
                [new PublicMacroEntry("project-id", 2, new MacroItem { Name = "project" })]);
            var settings = new AppSettings(Path.Combine(folder, "settings.json"));
            settings.StorePublicMacroEditorSources("project");
            var server = new BridgeServer();
            var channel = new Channel();
            server.Attach(channel);
            new EditorSessionBridge(new WhaleGenie.Editor.EditorDocumentService(workspace), workspace,
                publicLibrary: new FilePublicLibraryStore(localPath),
                appSettings: settings).Register(server);

            var rows = (await Call(server, channel,
                """{"id":1,"method":"editor.sessionActions"}"""))
                .GetProperty("result").GetProperty("publicMacros").EnumerateArray().ToList();

            var row = Assert.Single(rows);
            Assert.Equal("project", row.GetProperty("source").GetString());
            Assert.Equal("project", row.GetProperty("name").GetString());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
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
        public System.Collections.Generic.List<string> Sent { get; } = [];

        public event Action<string>? MessageFromWeb;

        public Task SendToWebAsync(string json)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public void Receive(string json) => MessageFromWeb?.Invoke(json);
    }
}
