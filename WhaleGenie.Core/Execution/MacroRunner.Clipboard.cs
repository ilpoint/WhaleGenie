using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

public sealed partial class MacroRunner
{
    // ------------------------------------------------------------------ clipboard

    /// <summary>Puts text on the clipboard, the same as copying it.</summary>
    private void ClipboardWrite(ExecutableStep step, int depth)
    {
        var text = ReadValue(step, "clipboardText").AsText();
        _devices.Clipboard.WriteText(text);
        Log(LogLevel.Info, depth, step.Type, LogKeys.CopiedToClipboard, text.Length);
    }

    /// <summary>Reads the clipboard into a variable.</summary>
    private void ClipboardRead(ExecutableStep step, int depth)
    {
        var text = _devices.Clipboard.ReadText();
        Store(step, "text", Value.FromText(text), depth);
        Log(LogLevel.Info, depth, step.Type, LogKeys.ReadClipboard, text.Length);
    }

    /// <summary>
    /// Takes the picture on the clipboard into an image variable, so a later step can look for it
    /// on screen. There is no rectangle to write down with it: a picture on the clipboard came from
    /// somewhere else, and nothing says where on this screen it might be.
    /// </summary>
    private void ClipboardImageRead(ExecutableStep step, int depth)
    {
        var picture = _devices.Clipboard.ReadImage();
        var name = step.Return("image");

        if (picture is null || picture.IsEmpty)
        {
            // A miss clears the variable outright, the same habit the find-image steps have: a
            // step inside a loop must never read the picture the previous pass left behind.
            if (name.Length > 0)
            {
                _images.Remove(name);
            }

            Give(step, "image", Value.FromText(string.Empty));
            Log(LogLevel.Warn, depth, step.Type, LogKeys.NoClipboardImage);
            return;
        }

        if (name.Length > 0)
        {
            _images[name] = picture;
        }

        Give(step, "image", Value.FromText($"<image {picture.Width}x{picture.Height}>"));
        Give(step, FieldNames.Width, Value.FromNumber(picture.Width));
        Give(step, FieldNames.Height, Value.FromNumber(picture.Height));
        Log(LogLevel.Info, depth, step.Type, LogKeys.ReadClipboardImage,
            $"{picture.Width}x{picture.Height}");
    }

    /// <summary>Puts a picture on the clipboard, from a file or from an image variable.</summary>
    private void ClipboardImageWrite(ExecutableStep step, int depth)
    {
        var picture = Reference(step);
        if (picture.IsEmpty)
        {
            throw new StepFailure(RunKeys.MissingImage, ReadText(step, "imageSource"));
        }

        _devices.Clipboard.WriteImage(picture);
        Log(LogLevel.Info, depth, step.Type, LogKeys.CopiedImageToClipboard,
            $"{picture.Width}x{picture.Height}");
    }

    /// <summary>Takes the paths of the files on the clipboard into a list.</summary>
    private void ClipboardFilesRead(ExecutableStep step, int depth)
    {
        var files = _devices.Clipboard.ReadFiles();

        Store(step, "files", Value.FromList(files.Select(Value.FromText)), depth);
        Log(LogLevel.Info, depth, step.Type, LogKeys.ReadClipboardFiles, files.Count);
    }

    /// <summary>
    /// Puts files on the clipboard. A relative path is taken from the macros folder, and every
    /// file is checked to be there: the paste happens in another program, where a path that points
    /// nowhere is refused in a way the macro never gets to see.
    /// </summary>
    private void ClipboardFilesWrite(ExecutableStep step, int depth)
    {
        var value = ReadValue(step, "files");
        var paths = new List<string>();
        foreach (var item in value.IsList ? value.Items : [value])
        {
            var written = item.AsText().Trim();
            if (written.Length == 0)
            {
                continue;
            }

            var full = _devices.Files.Resolve(written);
            if (!_devices.Files.Exists(full))
            {
                throw new StepFailure(RunKeys.FileNotFound, written);
            }

            paths.Add(full);
        }

        if (paths.Count == 0)
        {
            throw new StepFailure(RunKeys.MissingPath);
        }

        _devices.Clipboard.WriteFiles(paths);
        Log(LogLevel.Info, depth, step.Type, LogKeys.CopiedFilesToClipboard, paths.Count);
    }

    /// <summary>Waits for the clipboard to change, then keeps whatever landed on it.</summary>
    private async Task ClipboardWait(ExecutableStep step, int depth, CancellationToken token)
    {
        await AwaitClipboard(step, _devices.Clipboard.ChangeCount, token);

        var text = _devices.Clipboard.ReadText();
        Give(step, "text", Value.FromText(text));
        Log(LogLevel.Info, depth, step.Type, LogKeys.ClipboardChanged, text.Length);
    }

    /// <summary>Presses the copy shortcut, then waits for what it put on the clipboard.</summary>
    private async Task ClipboardCopy(ExecutableStep step, int depth, CancellationToken token)
    {
        var before = _devices.Clipboard.ChangeCount;
        Input(step).Hotkey(Shortcut(step), 50);
        await AwaitClipboard(step, before, token);

        var text = _devices.Clipboard.ReadText();
        Give(step, "text", Value.FromText(text));
        Log(LogLevel.Info, depth, step.Type, LogKeys.CopiedFromWindow, text.Length);
    }

    /// <summary>Puts text on the clipboard when one is given, then presses the paste shortcut.</summary>
    private async Task ClipboardPaste(ExecutableStep step, int depth, CancellationToken token)
    {
        var given = ReadText(step, "pasteText").Trim().Length > 0;
        if (given)
        {
            _devices.Clipboard.WriteText(ReadValue(step, "pasteText").AsText());
            await Pause(40, token);
        }

        Input(step).Hotkey(Shortcut(step), 50);
        Log(LogLevel.Info, depth, step.Type, given ? LogKeys.PastedText : LogKeys.Pasted);
    }

    /// <summary>
    /// Waits for the clipboard's change counter to move past <paramref name="before"/>, which is
    /// how a macro notices a copy without reading the same text over and over.
    /// </summary>
    private async Task AwaitClipboard(ExecutableStep step, int before, CancellationToken token)
    {
        var timeout = step.Meta.TimeoutMs;

        if (!await WaitForFlagAsync(() => _devices.Clipboard.ChangeCount != before, timeout, 30, token))
        {
            throw new StepFailure(RunKeys.ClipboardTimeout);
        }
    }


    /// <summary>
    /// The clipboard actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchClipboard(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "clipboard.writeText":
                ClipboardWrite(step, depth);
                return Signal.Normal;
            case "clipboard.readText":
                ClipboardRead(step, depth);
                return Signal.Normal;
            case "clipboard.readImage":
                ClipboardImageRead(step, depth);
                return Signal.Normal;
            case "clipboard.writeImage":
                ClipboardImageWrite(step, depth);
                return Signal.Normal;
            case "clipboard.readFiles":
                ClipboardFilesRead(step, depth);
                return Signal.Normal;
            case "clipboard.writeFiles":
                ClipboardFilesWrite(step, depth);
                return Signal.Normal;
            case "clipboard.clear":
                _devices.Clipboard.Clear();
                Log(LogLevel.Info, depth, step.Type, LogKeys.ClearedClipboard);
                return Signal.Normal;
            case "clipboard.waitChange":
                await ClipboardWait(step, depth, token);
                return Signal.Normal;
            case "clipboard.copy":
                await ClipboardCopy(step, depth, token);
                return Signal.Normal;
            case "clipboard.paste":
                await ClipboardPaste(step, depth, token);
                return Signal.Normal;

            // ---------------------------------------------------------------- process
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }
}
