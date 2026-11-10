using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

// What a macro takes and hands back, read as one page.
public partial class McpServerTests
{
    [Fact]
    public void A_client_can_read_what_a_macro_takes_and_write_a_call_from_it()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-signature-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "project.wgmacro");
                var viewModel = new MainViewModel();
                viewModel.AddMacro(new MacroItem { Name = "原宏" });
                viewModel.CurrentPath = path;
                viewModel.SavePackage(path);

                await using var loop = McpLoopback.Start(viewModel);
                await using var client = await loop.ConnectAsync();
                var draft = JsonDocument.Parse(await Call(client, "whalegenie_open_project",
                    new Dictionary<string, object?>())).RootElement.GetProperty("draftId").GetString()!;

                await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = JsonNode.Parse("""
                        {"name":"子","arguments":[
                           {"name":"数量","type":"Number","required":true,"description":"要加多少。"},
                           {"name":"标签","type":"Text","required":false,"default":"无"}],
                         "returns":[{"name":"答案","type":"Number","description":"加完的结果。"}],
                         "steps":[{"type":"control.delay","params":{"ms":"1"}}]}
                        """),
                });

                var signature = JsonDocument.Parse(await Call(client, "whalegenie_get_macro_signature",
                    new Dictionary<string, object?> { ["draftId"] = draft, ["name"] = "子" }))
                    .RootElement;

                // A call names its arguments one row each; a client cannot get those names from the
                // action catalogue, which is why this answer carries the signatures themselves.
                var arguments = signature.GetProperty("arguments").EnumerateArray().ToArray();
                Assert.Equal(2, arguments.Length);

                // Chinese comes back as itself: an escaped answer is twice the size and unreadable
                // in a log, and nothing here is HTML.
                Assert.Contains("答案", signature.GetRawText(), StringComparison.Ordinal);

                Assert.Equal("数量", arguments[0].GetProperty("name").GetString());
                Assert.True(arguments[0].GetProperty("required").GetBoolean());
                Assert.False(arguments[1].GetProperty("required").GetBoolean());
                Assert.Equal(["name", "value", "given"],
                    signature.GetProperty("argumentColumns").EnumerateArray()
                        .Select(column => column.GetString() ?? string.Empty).ToArray());

                // The skeleton carries what a call has to fill in, leaves the optional ones out so
                // their declared default applies, and asks for every result by key and name.
                var template = signature.GetProperty("callTemplate");
                var skeleton = Assert.Single(template.GetProperty("arguments").EnumerateArray().ToArray());
                Assert.Equal("数量", skeleton.GetProperty("name").GetString());
                Assert.Equal(string.Empty, skeleton.GetProperty("value").GetString());
                var row = template.GetProperty("returns")[0];
                Assert.Equal("答案", row.GetProperty("key").GetString());
                Assert.Equal("答案", row.GetProperty("name").GetString());

                // A step written from that skeleton is a step the validator accepts, once the value
                // the template leaves empty is filled in.
                var call = JsonNode.Parse(template.GetRawText())!.AsObject();
                call["arguments"]![0]!["value"] = "3";
                await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = new JsonObject
                    {
                        ["name"] = "主",
                        ["steps"] = new JsonArray(new JsonObject
                        {
                            ["type"] = "control.runMacro",
                            ["params"] = call,
                        }),
                    },
                });

                var validated = JsonDocument.Parse(await Call(client, "whalegenie_validate",
                    new Dictionary<string, object?> { ["draftId"] = draft })).RootElement;
                Assert.True(validated.GetProperty("ok").GetBoolean(), validated.GetRawText());

                await loop.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }
}
