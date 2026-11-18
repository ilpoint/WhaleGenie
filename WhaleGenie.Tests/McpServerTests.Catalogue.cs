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

// The catalogue and the project read as resources, and what an action says about the machine.
public partial class McpServerTests
{
    /// <summary>
    /// The catalogue is also a resource, so a client that can cache one reads it once instead of
    /// asking for it again at every turn. Both readings come from the same catalogue, so what is
    /// checked here is that the resource is there and holds every action the tool would list.
    /// </summary>
    [Fact]
    public async Task The_catalogue_is_also_a_resource_a_client_can_keep()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var uris = (await client.ListResourcesAsync()).Select(resource => resource.Uri).ToList();
            Assert.Contains(uris, uri => uri.Contains("actions", StringComparison.Ordinal));

            var templates = (await client.ListResourceTemplatesAsync())
                .Select(template => template.UriTemplate).ToList();
            Assert.Contains("whalegenie://actions/{key}", templates);

            var whole = await client.ReadResourceAsync("whalegenie://actions");
            var text = string.Concat(whole.Contents.OfType<TextResourceContents>()
                .Select(content => content.Text));
            Assert.Contains($"\"total\":{ActionSemanticsCatalog.All.Count}", text,
                StringComparison.Ordinal);

            // The template answers for one action, which is the same description the tool gives.
            var one = await client.ReadResourceAsync("whalegenie://actions/control.delay");
            var details = string.Concat(one.Contents.OfType<TextResourceContents>()
                .Select(content => content.Text));
            Assert.Contains("control.delay", details, StringComparison.Ordinal);

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// The project is one page a client can keep, and every macro on it names its own page. Reading
    /// that page gives the same outline the tools give, so a client holding both can place a step
    /// without reading the macro whole — which is the whole point of a page over an answer.
    /// </summary>
    [Fact]
    public void The_project_reads_as_one_page_and_every_macro_names_its_own()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            var written = JsonDocument.Parse(await Wrote(client, "whalegenie_put_macro",
                new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = JsonNode.Parse("""
                        {"name":"登录","steps":[{"type":"control.delay","params":{"ms":"10"}}]}
                        """),
                })).RootElement;
            await Wrote(client, "whalegenie_apply", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
            });

            var status = JsonDocument.Parse(await Call(client, "whalegenie_project_status",
                new Dictionary<string, object?>())).RootElement;
            var project = await ReadPage(client, "whalegenie://project");

            // The page is about the same revision the tool reports: that number is what makes
            // keeping the page safe, so it may not be one the project has already left behind.
            Assert.Equal(status.GetProperty("revision").GetInt32(),
                project.GetProperty("revision").GetInt32());

            var login = project.GetProperty("macros").EnumerateArray()
                .Single(macro => macro.GetProperty("name").GetString() == "登录");
            Assert.Equal(1, login.GetProperty("steps").GetInt32());

            // Where the macro's own page is, escaped so a Chinese name survives being a path.
            var uri = login.GetProperty("uri").GetString()!;
            Assert.Equal(McpResources.MacroUri("登录"), uri);

            var page = await ReadPage(client, uri);
            Assert.Equal("登录", page.GetProperty("name").GetString());
            var steps = page.GetProperty("outline").EnumerateArray().ToList();
            Assert.Single(steps);
            Assert.Equal(written.GetProperty("outline")[0].GetProperty("id").GetString(),
                steps[0].GetProperty("id").GetString());
            Assert.Equal("0", steps[0].GetProperty("path").GetString());

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A page for a name the project does not have answers with the nearest names rather than
    /// failing: the client asked about a macro it half-remembers, and a candidate list is the answer
    /// to that question. A name that is only longer than the real one still finds it.
    /// </summary>
    [Fact]
    public void A_macro_page_for_a_name_that_is_not_there_names_the_nearest_ones()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var draft = await Draft(client);
            await Wrote(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = JsonNode.Parse("""
                    {"name":"登录","steps":[{"type":"control.delay","params":{"ms":"10"}}]}
                    """),
            });
            await Wrote(client, "whalegenie_apply", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
            });

            var page = await ReadPage(client, McpResources.MacroUri("登录后台"));
            var error = page.GetProperty("error");
            Assert.Equal(McpErrors.NoSuchMacro, error.GetProperty("code").GetString());
            Assert.Contains(error.GetProperty("candidates").EnumerateArray(),
                candidate => candidate.GetString() == "登录");

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>Reads one resource and parses it, which is what a client keeping a page would do.</summary>
    private static async Task<JsonElement> ReadPage(McpClient client, string uri)
    {
        var read = await client.ReadResourceAsync(uri);
        var text = string.Concat(read.Contents.OfType<TextResourceContents>()
            .Select(content => content.Text));
        return JsonDocument.Parse(text).RootElement;
    }


    /// <summary>
    /// What a step says it does to the machine, so a client can tell a click from a read before it
    /// writes the step: the flags are in the catalogue and used to be dropped on the way out.
    /// </summary>
    [Fact]
    public void An_action_says_whether_it_touches_the_machine()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var click = JsonDocument.Parse(await Call(client, "whalegenie_get_action",
                new Dictionary<string, object?> { ["key"] = "input.mouseClick" })).RootElement;
            Assert.True(click.GetProperty("sendsInput").GetBoolean());
            Assert.False(click.GetProperty("looksAtScreen").GetBoolean());
            Assert.Equal("None", click.GetProperty("timeoutSource").GetString());

            var shot = JsonDocument.Parse(await Call(client, "whalegenie_get_action",
                new Dictionary<string, object?> { ["key"] = "vision.capture" })).RootElement;
            Assert.False(shot.GetProperty("sendsInput").GetBoolean());
            Assert.True(shot.GetProperty("looksAtScreen").GetBoolean());

            var wait = JsonDocument.Parse(await Call(client, "whalegenie_get_action",
                new Dictionary<string, object?> { ["key"] = "window.waitFor" })).RootElement;
            Assert.True(wait.GetProperty("waits").GetBoolean());
            Assert.Equal("Step", wait.GetProperty("timeoutSource").GetString());

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A key one letter away from a real one gets its neighbours. Containment alone answered nothing
    /// for "control.delai", which is exactly when a client needs the hint.
    /// </summary>
    [Fact]
    public void A_misspelled_action_key_gets_its_neighbours()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var answer = await client.CallToolAsync("whalegenie_get_action",
                new Dictionary<string, object?> { ["key"] = "control.delai" });
            Assert.True(answer.IsError);
            var error = JsonDocument.Parse(answer.Content.OfType<TextContentBlock>().First().Text)
                .RootElement.GetProperty("error");
            Assert.Equal("UnknownAction", error.GetProperty("code").GetString());
            var candidates = error.GetProperty("details").GetProperty("candidates").EnumerateArray()
                .Select(candidate => candidate.GetString()).ToList();
            Assert.Contains("control.delay", candidates);

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A draft id nobody handed out is not a discard that worked: a client that mistyped it has to
    /// hear so, or it believes it cleaned up.
    /// </summary>
    [Fact]
    public void Discarding_a_draft_that_never_existed_says_so()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var answer = await client.CallToolAsync("whalegenie_discard",
                new Dictionary<string, object?> { ["draftId"] = "nope1234" });
            Assert.True(answer.IsError);
            var error = JsonDocument.Parse(answer.Content.OfType<TextContentBlock>().First().Text)
                .RootElement.GetProperty("error");
            Assert.Equal("DraftNotFound", error.GetProperty("code").GetString());

            await loop.StopAsync();
            return true;
        });
    }
}
