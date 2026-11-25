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

// Pictures a client stores beside the package, and what happens to one that is not a picture.
public partial class McpServerTests
{
    /// <summary>
    /// A picture a client brings is a file beside the package, and the name it answers with is the
    /// one a step puts in its image parameter. Nothing is written into the draft: the asset travels
    /// with the package when it is saved, the same way a picture taken from the screen does.
    /// </summary>
    [Fact]
    public void An_image_is_stored_beside_the_package_and_named_for_the_step()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-image-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "proj.wgmacro");
                var viewModel = new MainViewModel();
                viewModel.AddMacro(new MacroItem { Name = "找图" });
                viewModel.CurrentPath = path;
                TestProjectFiles.Service.Save(viewModel.Workspace, path);

                await using var loop = McpLoopback.Start(viewModel);
                await using var client = await loop.ConnectAsync();

                var answer = JsonDocument.Parse(await Wrote(client, "whalegenie_put_image",
                    new Dictionary<string, object?>
                    {
                        ["base64"] = OnePixelPng,
                        ["name"] = "按钮",
                    })).RootElement;

                var file = answer.GetProperty("file").GetString()!;
                Assert.Equal("按钮.png", file);
                Assert.Equal(1, answer.GetProperty("width").GetInt32());
                Assert.Equal(1, answer.GetProperty("height").GetInt32());
                Assert.Equal(file, answer.GetProperty("parameter").GetProperty("image").GetString());

                // Under the folder the package owns, in the program's own data folder.
                var stored = Path.Combine(ImageAssets.FolderFor(path), file);
                Assert.True(File.Exists(stored), stored);

                // A second picture under the same name is kept, not written over: a step still
                // points at the first one.
                var again = JsonDocument.Parse(await Wrote(client, "whalegenie_put_image",
                    new Dictionary<string, object?> { ["base64"] = OnePixelPng, ["name"] = "按钮" }))
                    .RootElement;
                Assert.Equal("按钮-2.png", again.GetProperty("file").GetString());

                await loop.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    /// <summary>A refusal says which of the two things went wrong, not that "the image was bad".</summary>
    [Fact]
    public void An_image_that_is_not_a_picture_is_refused()
    {
        Ui.RunAsync(async () =>
        {
            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            var answered = await client.CallToolAsync("whalegenie_put_image",
                new Dictionary<string, object?>
                {
                    ["base64"] = Convert.ToBase64String("not a picture"u8.ToArray()),
                });

            Assert.True(answered.IsError == true);
            Assert.Equal(McpErrors.BadArgument, Code(answered));
            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>The error code a refusal carries, which is what a client branches on.</summary>
    private static string? Code(CallToolResult result)
        => JsonDocument.Parse(result.Content.OfType<TextContentBlock>().First().Text)
            .RootElement.GetProperty("error").GetProperty("code").GetString();

    /// <summary>A one pixel PNG: enough to be a real picture, small enough to write in a test.</summary>
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
}
