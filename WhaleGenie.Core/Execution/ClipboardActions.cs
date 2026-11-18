using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The clipboard steps: what a person would copy, paste or wait for, driven by the macro. Reading a
/// picture from the clipboard is also the one clipboard step that shows what it took.
/// </summary>
internal static class ClipboardActions
{
    /// <summary>
    /// The clipboard actions, each answered by the method written for it. A step of another family
    /// is not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["clipboard.writeText"] = static (run, step, depth, _) =>
            {
                WriteText(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["clipboard.readText"] = static (run, step, depth, _) =>
            {
                ReadText(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["clipboard.readImage"] = static (run, step, depth, _) =>
            {
                // Watched so the picture is handed to the window once, the same way a capture is.
                run.Watching(() => ReadImage(run, step, depth));
                return Task.FromResult(Signal.Normal);
            },

            ["clipboard.writeImage"] = static (run, step, depth, _) =>
            {
                WriteImage(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["clipboard.readFiles"] = static (run, step, depth, _) =>
            {
                ReadFiles(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["clipboard.writeFiles"] = static (run, step, depth, _) =>
            {
                WriteFiles(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["clipboard.clear"] = static (run, step, depth, _) =>
            {
                run.Devices.Clipboard.Clear();
                run.Log(LogLevel.Info, depth, step.Type, LogKeys.ClearedClipboard);
                return Task.FromResult(Signal.Normal);
            },

            ["clipboard.waitChange"] = static async (run, step, depth, token) =>
            {
                await WaitChange(run, step, depth, token);
                return Signal.Normal;
            },

            ["clipboard.copy"] = static async (run, step, depth, token) =>
            {
                await Copy(run, step, depth, token);
                return Signal.Normal;
            },

            ["clipboard.paste"] = static async (run, step, depth, token) =>
            {
                await Paste(run, step, depth, token);
                return Signal.Normal;
            },
        };

    /// <summary>Puts text on the clipboard, the same as copying it.</summary>
    private static void WriteText(IRunContext run, ExecutableStep step, int depth)
    {
        var text = run.Args(step).ValueOf("clipboardText").AsText();
        run.Devices.Clipboard.WriteText(text);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.CopiedToClipboard, text.Length);
    }

    /// <summary>Reads the clipboard into a variable.</summary>
    private static void ReadText(IRunContext run, ExecutableStep step, int depth)
    {
        var text = run.Devices.Clipboard.ReadText();
        run.Store(step, "text", Value.FromText(text), depth);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ReadClipboard, text.Length);
    }

    /// <summary>
    /// Takes the picture on the clipboard into an image variable, so a later step can look for it
    /// on screen. There is no rectangle to write down with it: a picture on the clipboard came from
    /// somewhere else, and nothing says where on this screen it might be.
    /// </summary>
    private static void ReadImage(IRunContext run, ExecutableStep step, int depth)
    {
        var picture = run.Devices.Clipboard.ReadImage();
        var name = step.Return("image");

        if (picture is null || picture.IsEmpty)
        {
            // A miss clears the variable outright, the same habit the find-image steps have: a
            // step inside a loop must never read the picture the previous pass left behind.
            if (name.Length > 0)
            {
                run.Images.Remove(name);
            }

            run.Give(step, "image", Value.FromImage(string.Empty));
            run.Log(LogLevel.Warn, depth, step.Type, LogKeys.NoClipboardImage);
            return;
        }

        if (name.Length > 0)
        {
            run.Images[name] = picture;
        }

        // The picture is the whole answer, so it is what the run window shows for this step: there
        // is nothing on the screen to mark and nothing to score.
        run.Saw(step, LookKind.Clipboard, picture);

        run.Give(step, "image", Value.FromImage($"<image {picture.Width}x{picture.Height}>"));
        run.Give(step, FieldNames.Width, Value.FromNumber(picture.Width));
        run.Give(step, FieldNames.Height, Value.FromNumber(picture.Height));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ReadClipboardImage,
            $"{picture.Width}x{picture.Height}");
    }

    /// <summary>Puts a picture on the clipboard, from a file or from an image variable.</summary>
    private static void WriteImage(IRunContext run, ExecutableStep step, int depth)
    {
        var picture = run.Reference(step);
        if (picture.IsEmpty)
        {
            throw new StepFailure(RunKeys.MissingImage, run.Args(step).Text("imageSource"));
        }

        run.Devices.Clipboard.WriteImage(picture);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.CopiedImageToClipboard,
            $"{picture.Width}x{picture.Height}");
    }

    /// <summary>Takes the paths of the files on the clipboard into a list.</summary>
    private static void ReadFiles(IRunContext run, ExecutableStep step, int depth)
    {
        var files = run.Devices.Clipboard.ReadFiles();

        run.Store(step, "files", Value.FromList(files.Select(Value.FromText)), depth);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ReadClipboardFiles, files.Count);
    }

    /// <summary>
    /// Puts files on the clipboard. A relative path is taken from the macros folder, and every
    /// file is checked to be there: the paste happens in another program, where a path that points
    /// nowhere is refused in a way the macro never gets to see.
    /// </summary>
    private static void WriteFiles(IRunContext run, ExecutableStep step, int depth)
    {
        var value = run.Args(step).ValueOf("files");
        var paths = new List<string>();
        IReadOnlyList<Value> items = value.IsList
            ? value.Items
            : [.. TextLines.Split(value.AsText()).Select(Value.FromText)];
        foreach (var item in items)
        {
            var written = item.AsText().Trim();
            if (written.Length == 0)
            {
                continue;
            }

            var full = run.Devices.Files.Resolve(written);
            if (!run.Devices.Files.Exists(full))
            {
                throw new StepFailure(RunKeys.FileNotFound, written);
            }

            paths.Add(full);
        }

        if (paths.Count == 0)
        {
            throw new StepFailure(RunKeys.MissingPath);
        }

        run.Devices.Clipboard.WriteFiles(paths);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.CopiedFilesToClipboard, paths.Count);
    }

    /// <summary>Waits for the clipboard to change, then keeps whatever landed on it.</summary>
    private static async Task WaitChange(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        await AwaitClipboard(run, step, run.Devices.Clipboard.ChangeCount, token);

        var text = run.Devices.Clipboard.ReadText();
        run.Give(step, "text", Value.FromText(text));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ClipboardChanged, text.Length);
    }

    /// <summary>Presses the copy shortcut, then waits for what it put on the clipboard.</summary>
    private static async Task Copy(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var before = run.Devices.Clipboard.ChangeCount;
        run.Input(step).Hotkey(run.Shortcut(step), 50);
        await AwaitClipboard(run, step, before, token);

        var text = run.Devices.Clipboard.ReadText();
        run.Give(step, "text", Value.FromText(text));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.CopiedFromWindow, text.Length);
    }

    /// <summary>Puts text on the clipboard when one is given, then presses the paste shortcut.</summary>
    private static async Task Paste(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var given = run.Args(step).Text("pasteText").Trim().Length > 0;
        if (given)
        {
            run.Devices.Clipboard.WriteText(run.Args(step).ValueOf("pasteText").AsText());
            await Pacing.Pause(40, token);
        }

        run.Input(step).Hotkey(run.Shortcut(step), 50);
        run.Log(LogLevel.Info, depth, step.Type, given ? LogKeys.PastedText : LogKeys.Pasted);
    }

    /// <summary>
    /// Waits for the clipboard's change counter to move past <paramref name="before"/>, which is
    /// how a macro notices a copy without reading the same text over and over.
    /// </summary>
    private static async Task AwaitClipboard(IRunContext run, ExecutableStep step, int before,
        CancellationToken token)
    {
        var timeout = step.Meta.TimeoutMs;

        if (!await Pacing.WaitForFlagAsync(() => run.Devices.Clipboard.ChangeCount != before,
                timeout, 30, token))
        {
            throw new StepFailure(RunKeys.ClipboardTimeout);
        }
    }
}
