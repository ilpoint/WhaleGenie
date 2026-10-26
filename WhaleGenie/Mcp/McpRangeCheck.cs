using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace WhaleGenie.Mcp;

/// <summary>
/// The whole loop tried on a range: open a draft, write a macro that clicks the counting button,
/// run it, read the count back, reset, and then try the three edges. It is the check that says the
/// pieces work together — the tools answer, the run happens, the window changes, and the edges hold.
/// </summary>
internal static class McpRangeCheck
{
    /// <summary>Runs every step of the loop and returns the report, or null when there is no range.</summary>
    public static async Task<McpSelfCheckReport?> RunAsync(McpClient client, string endpoint,
        CancellationToken token)
    {
        var report = new McpSelfCheckReport { Endpoint = endpoint, Title = "range check" };

        // 1. The window is there and says where it is.
        var state = await Call(client, "whalegenie_range_state", new Dictionary<string, object?>(), token);
        if (!state.Ok)
        {
            report.Checks.Add(new McpCheck("range.state", McpCheckResult.Fail, state.Text));
            return report;
        }

        var room = JsonDocument.Parse(state.Text).RootElement;
        if (!room.GetProperty("available").GetBoolean())
        {
            report.Checks.Add(new McpCheck("range.state", McpCheckResult.Missing,
                "this server has no range window (start it with --range)"));
            return report;
        }

        var targets = room.GetProperty("targets").EnumerateArray()
            .ToDictionary(target => target.GetProperty("id").GetString()!, target => target);
        report.Checks.Add(new McpCheck("range.state", targets.Count == 5
            ? McpCheckResult.Pass
            : McpCheckResult.Fail, $"handle {room.GetProperty("handle").GetInt64()}, "
                + $"{targets.Count} controls, at ({room.GetProperty("x").GetInt32()}, "
                + $"{room.GetProperty("y").GetInt32()}) {room.GetProperty("width").GetInt32()} by "
                + $"{room.GetProperty("height").GetInt32()}"));

        // 2. In front of whatever else is on the desktop: a click lands on whatever is at that
        // point, and a range nobody brought forward is a range a click misses.
        var raised = await Call(client, "whalegenie_window_activate", new Dictionary<string, object?>
        {
            ["handle"] = room.GetProperty("handle").GetInt64(),
        }, token);
        report.Checks.Add(new McpCheck("range.front",
            raised.Ok ? McpCheckResult.Pass : McpCheckResult.Fail,
            raised.Ok ? Trim(raised.Text) : raised.Text));

        // 3. A macro that aims at the counting button, at the place the window said it is.
        var opened = await Call(client, "whalegenie_open_project", new Dictionary<string, object?>(), token);
        var draft = opened.Ok
            ? JsonDocument.Parse(opened.Text).RootElement.GetProperty("draftId").GetString()!
            : string.Empty;

        var count = targets["rangeCount"];
        var x = room.GetProperty("x").GetInt32() + count.GetProperty("x").GetInt32()
            + (count.GetProperty("width").GetInt32() / 2);
        var y = room.GetProperty("y").GetInt32() + count.GetProperty("y").GetInt32()
            + (count.GetProperty("height").GetInt32() / 2);

        var macro = new JsonObject
        {
            ["name"] = "按一下",
            ["steps"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "input.mouseClick",
                    ["params"] = new JsonObject
                    {
                        ["inputMode"] = "foreground",
                        ["button"] = "left",
                        ["x"] = x,
                        ["y"] = y,
                        ["clicks"] = 1,
                    },
                },
            },
        };

        var put = await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
        {
            ["draftId"] = draft,
            ["macro"] = macro,
        }, token);
        report.Checks.Add(new McpCheck("range.draft", put.Ok ? McpCheckResult.Pass : McpCheckResult.Fail,
            put.Ok ? "one step written into a draft" : put.Text));

        // 4. Run it, then read the count back — the step's own answer and the window's.
        var run = await Call(client, "whalegenie_run_macro", new Dictionary<string, object?>
        {
            ["name"] = "按一下",
            ["draftId"] = draft,
            ["timeoutMs"] = 30_000,
        }, token);
        var ran = run.Ok && JsonDocument.Parse(run.Text).RootElement
            .GetProperty("status").GetString() == "Completed";
        report.Checks.Add(new McpCheck("range.run", ran ? McpCheckResult.Pass : McpCheckResult.Fail,
            run.Ok ? Trim(run.Text) : run.Text));

        // A picture of the window as it is now, kept beside the report: what a run did to a window
        // is easier to see than to read, and the doctor is already the thing that knows where it is.
        await Picture(client, report, room.GetProperty("handle").GetInt64(), token);

        // A click is posted to the window, not done to it: the step has finished by the time the
        // answer comes back, while the window's own thread may not have handled it yet. Looking
        // again for a moment is what a person does, and what makes the check about the click rather
        // than about who was faster.
        var presses = await PressesAsync(client, token);
        report.Checks.Add(new McpCheck("range.effect", presses == 1
            ? McpCheckResult.Pass
            : McpCheckResult.Fail, $"presses after the run: {presses}"));

        // 5. Put it back, which is what makes the next run comparable.
        var reset = await Call(client, "whalegenie_range_reset", new Dictionary<string, object?>(), token);
        var cleared = reset.Ok
            && JsonDocument.Parse(reset.Text).RootElement.GetProperty("presses").GetInt32() == 0;
        report.Checks.Add(new McpCheck("range.reset", cleared
            ? McpCheckResult.Pass
            : McpCheckResult.Fail, cleared ? "back to none" : Trim(reset.Text)));

        // 6. The three edges, each refused with its own code.
        await Edge(client, report, "range.edge.point", "Range.OutsideTarget", token, async () =>
            await Call(client, "whalegenie_mouse_click", new Dictionary<string, object?>
            {
                ["x"] = room.GetProperty("x").GetInt32() - 30,
                ["y"] = room.GetProperty("y").GetInt32() - 30,
            }, token));

        await Edge(client, report, "range.edge.script", "Range.ActionRefused", token, async () =>
        {
            await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = new JsonObject
                {
                    ["name"] = "脚本",
                    ["steps"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "script.run",
                            ["params"] = new JsonObject
                            {
                                ["kind"] = "ps1",
                                ["scriptSource"] = "text",
                                ["script"] = "Write-Output 1",
                            },
                        },
                    },
                },
            }, token);
            return await Call(client, "whalegenie_run_macro", new Dictionary<string, object?>
            {
                ["name"] = "脚本",
                ["draftId"] = draft,
            }, token);
        });

        await Edge(client, report, "range.edge.path", "Range.PathOutside", token, async () =>
        {
            await Call(client, "whalegenie_put_macro", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
                ["macro"] = new JsonObject
                {
                    ["name"] = "写外面",
                    ["steps"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "file.writeText",
                            ["params"] = new JsonObject
                            {
                                ["path"] = @"C:/Windows/whalegenie-range-check.txt",
                                ["fileContent"] = "x",
                            },
                        },
                    },
                },
            }, token);
            return await Call(client, "whalegenie_run_macro", new Dictionary<string, object?>
            {
                ["name"] = "写外面",
                ["draftId"] = draft,
            }, token);
        });

        if (draft.Length > 0)
        {
            await Call(client, "whalegenie_discard", new Dictionary<string, object?>
            {
                ["draftId"] = draft,
            }, token);
        }

        return report;
    }

    /// <summary>How many presses the window reports, looked at until it stops changing or time is up.</summary>
    private static async Task<int> PressesAsync(McpClient client, CancellationToken token)
    {
        var seen = -1;
        for (var look = 0; look < 20; look++)
        {
            var state = await Call(client, "whalegenie_range_state",
                new Dictionary<string, object?>(), token);
            if (!state.Ok)
            {
                return -1;
            }

            var presses = JsonDocument.Parse(state.Text).RootElement.GetProperty("presses").GetInt32();
            if (presses > 0)
            {
                return presses;
            }

            seen = presses;
            await Task.Delay(100, token);
        }

        return seen;
    }

    /// <summary>Saves a picture of the range window beside the report, when a report was asked for.</summary>
    private static async Task Picture(McpClient client, McpSelfCheckReport report, long handle,
        CancellationToken token)
    {
        try
        {
            var result = await client.CallToolAsync("whalegenie_capture_screen",
                new Dictionary<string, object?> { ["windowHandle"] = handle }, cancellationToken: token)
                .ConfigureAwait(false);
            var image = result.Content.OfType<ImageContentBlock>().FirstOrDefault();
            if (image is null)
            {
                return;
            }

            PicturePath = Path.Combine(Path.GetTempPath(), "whalegenie-range-check.png");
            await File.WriteAllBytesAsync(PicturePath, image.Data.ToArray(), token).ConfigureAwait(false);
            report.Facts["picture"] = PicturePath;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A picture is a help, not a check: a machine that will not draw one still got the run.
        }
    }

    /// <summary>Where the last picture went, so a person can look at what the run did.</summary>
    public static string PicturePath { get; private set; } = string.Empty;

    /// <summary>One edge: the call has to be refused, and with the code that names the edge.</summary>
    private static async Task Edge(McpClient client, McpSelfCheckReport report, string name,
        string code, CancellationToken token, Func<Task<Answer>> call)
    {
        var answer = await call();
        var refused = !answer.Ok && Code(answer.Text) == code;
        report.Checks.Add(new McpCheck(name, refused ? McpCheckResult.Pass : McpCheckResult.Fail,
            refused ? $"refused with {code}" : Trim(answer.Text)));
    }

    /// <summary>The code of a refusal, or empty when the answer was not one.</summary>
    private static string Code(string text)
    {
        try
        {
            return JsonDocument.Parse(text).RootElement.GetProperty("error").GetProperty("code")
                .GetString() ?? string.Empty;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException
            or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    /// <summary>One tool call and what came back, with whether it was refused.</summary>
    private static async Task<Answer> Call(McpClient client, string name,
        Dictionary<string, object?> arguments, CancellationToken token)
    {
        try
        {
            var result = await client.CallToolAsync(name, arguments,
                cancellationToken: token).ConfigureAwait(false);
            var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
                ?? string.Empty;
            return new Answer(result.IsError is not true, text);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new Answer(false, $"{error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>An answer long enough to read in a table.</summary>
    private static string Trim(string text)
        => text.Length <= 240 ? text : text[..240] + "…";

    private sealed record Answer(bool Ok, string Text);
}
