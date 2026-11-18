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

// The edges of a run log and an argument of the wrong shape.
public partial class McpServerTests
{
    /// <summary>
    /// Reading the log before anything has run is the state a session starts in, not a mistake: the
    /// answer is empty and says so, so a client does not treat its first look at the log as an error
    /// to retry or branch on. An id that names no run is still refused, and still says which.
    /// </summary>
    [Fact]
    public void The_run_log_read_before_anything_has_run_is_empty_rather_than_an_error()
    {
        Ui.RunAsync(async () =>
        {
            RunLogStore.Forget();
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var log = JsonDocument.Parse(await Call(client, "whalegenie_get_run_log",
                new Dictionary<string, object?>())).RootElement;

            Assert.Equal(0, log.GetProperty("totalLines").GetInt32());
            Assert.Equal(0, log.GetProperty("returned").GetInt32());
            Assert.False(log.GetProperty("truncated").GetBoolean());
            Assert.Empty(log.GetProperty("lines").EnumerateArray());
            Assert.Contains("No run", log.GetProperty("note").GetString(), StringComparison.Ordinal);

            var missing = await Call(client, "whalegenie_get_run_log",
                new Dictionary<string, object?> { ["runId"] = "nope" });
            Assert.Contains("NoSuchRun", missing, StringComparison.Ordinal);

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// A parameter that has to be an object or a list arrives as text often enough to matter — a
    /// client quoting the macro, or wrapping the array in an object. The schema stays strict, and the
    /// answer names the argument, what arrived and what to send instead, rather than the runtime's
    /// "An error occurred invoking".
    /// </summary>
    [Fact]
    public void An_argument_of_the_wrong_shape_is_refused_by_name()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-shape-" + Guid.NewGuid().ToString("N"));
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

                // The macro as text, which is how a double-encoded macro arrives.
                var quoted = await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["macro"] = "{\"name\":\"AI宏\",\"steps\":[]}",
                });
                Assert.Contains("BadJson", quoted, StringComparison.Ordinal);
                Assert.Contains("\"macro\"", quoted, StringComparison.Ordinal);
                Assert.Contains("text", quoted, StringComparison.Ordinal);

                // The declarations as one object instead of a list of them.
                var wrapped = await Call(client, "whalegenie_set_globals", new Dictionary<string, object?>
                {
                    ["draftId"] = draft,
                    ["globals"] = new { name = "总数" },
                });
                Assert.Contains("BadJson", wrapped, StringComparison.Ordinal);
                Assert.Contains("\"globals\"", wrapped, StringComparison.Ordinal);
                Assert.Contains("list", wrapped, StringComparison.Ordinal);

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
