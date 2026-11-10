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

// What kind of script a step writes, where it runs, and how its file is named and tidied.
public sealed partial class MacroRunner
{
    /// <summary>The kind of script a step asks for, said in the step's <c>kind</c> field.</summary>
    private static ScriptKind ScriptKindOf(ExecutableStep step)
        => step.Text("kind").Trim().ToLowerInvariant() switch
        {
            "" or "ps1" => ScriptKind.PowerShell,
            "cmdkeep" => ScriptKind.CommandKeepWindow,
            "cmdclose" => ScriptKind.CommandCloseWindow,
            "cmdhidden" => ScriptKind.CommandHidden,
            "bat" => ScriptKind.BatchFile,
            "cmdfile" => ScriptKind.CommandFile,
            "ahk" => ScriptKind.AutoHotkey,
            "custom" => ScriptKind.Custom,
            // A word the catalogue does not offer is refused rather than quietly read as one of the
            // kinds: a step that says something unknown is a step whose author meant something
            // else, and running it as PowerShell would hide that.
            _ => throw new StepFailure(RunKeys.UnknownScriptKind, step.Text("kind")),
        };

    /// <summary>
    /// A name for the kind of script, for the line the run log keeps. It is the name of the thing
    /// that ran rather than the value the field holds, because the log is read by a person.
    /// </summary>
    private static string ScriptKindName(ScriptKind kind) => kind switch
    {
        ScriptKind.CommandKeepWindow or ScriptKind.CommandCloseWindow or ScriptKind.CommandHidden
            or ScriptKind.CommandFile => "CMD",
        ScriptKind.BatchFile => "BAT",
        ScriptKind.AutoHotkey => "AutoHotkey",
        ScriptKind.Custom => "custom",
        _ => "PowerShell",
    };

    /// <summary>
    /// The command line that runs a script of this kind. PowerShell is given the flags that stop it
    /// from loading a profile or asking anything, so a script that runs inside a macro behaves the
    /// same way every time it runs. A program of the macro's own choosing gets the script's path
    /// after whatever the macro wrote, which is where an interpreter expects to find it and where
    /// a person typing the same command line would put it.
    /// </summary>
    private (string Program, string Arguments) ScriptCommandOf(ExecutableStep step,
        ScriptKind kind, string path, string extra)
    {
        var quoted = $"\"{path}\"";
        var tail = extra.Length == 0 ? string.Empty : $" {extra}";

        if (kind is ScriptKind.Custom)
        {
            var named = ReadText(step, "interpreter").Trim();
            if (named.Length == 0)
            {
                throw new StepFailure(RunKeys.MissingInterpreter);
            }

            var (program, flags) = CommandLine.Split(named);
            var start = flags.Length == 0 ? string.Empty : $"{flags} ";
            return (program, $"{start}{quoted}{tail}");
        }

        if (kind is ScriptKind.AutoHotkey)
        {
            return (AutoHotkey.Program() ?? throw new StepFailure(RunKeys.NoAutoHotkey),
                $"{quoted}{tail}");
        }

        return kind switch
        {
            ScriptKind.CommandKeepWindow => ("cmd.exe", $"/k {quoted}{tail}"),
            ScriptKind.CommandCloseWindow or ScriptKind.CommandHidden
                or ScriptKind.BatchFile or ScriptKind.CommandFile => ("cmd.exe", $"/c {quoted}{tail}"),
            _ => ("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File {quoted}{tail}"),
        };
    }

    /// <summary>
    /// The file name ending the program recognises, which is how it knows what it is reading. The
    /// kinds that name a program by itself are each one ending; a program of the macro's own
    /// choosing has to be told.
    /// </summary>
    private string ScriptExtensionOf(ExecutableStep step, ScriptKind kind)
    {
        switch (kind)
        {
            case ScriptKind.CommandKeepWindow:
            case ScriptKind.CommandCloseWindow:
            case ScriptKind.CommandHidden:
            case ScriptKind.CommandFile:
                return ".cmd";
            case ScriptKind.BatchFile:
                return ".bat";
            case ScriptKind.PowerShell:
                return ".ps1";
            case ScriptKind.AutoHotkey:
                return ".ahk";
        }

        var wanted = ReadText(step, "extension").Trim();
        if (wanted.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingScriptExtension);
        }

        return wanted.StartsWith('.') ? wanted : $".{wanted}";
    }

    /// <summary>
    /// How the copy of the script handed over is written. Windows PowerShell and AutoHotkey read a
    /// script as the system code page unless the file starts with a byte-order mark, so a script
    /// with Chinese text in it needs one; cmd.exe reads in this machine's own code page and a batch
    /// file with a mark on it does not start at all, so the command kinds get that one. A program
    /// of the macro's own choosing gets plain UTF-8, which is what a program from this decade
    /// reads. A macro that knows better can say so, and the reason it can is cscript: that one
    /// reads the system code page by design, so Chinese text in a .vbs has to be GBK.
    /// </summary>
    private static string ScriptEncodingOf(ExecutableStep step, ScriptKind kind)
    {
        var chosen = step.Text("encoding").Trim();
        if (chosen.Length > 0 && !string.Equals(chosen, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return chosen;
        }

        return kind switch
        {
            ScriptKind.PowerShell or ScriptKind.AutoHotkey => "utf8bom",
            ScriptKind.Custom => TextEncoding.Default,
            _ => TextEncoding.System,
        };
    }

    /// <summary>
    /// The folder a script runs in. A script that reads or writes files next to the macro should
    /// not have to be told where that is, so the macros folder is what an empty working folder
    /// means — but only when that folder is really there: a program cannot be started in a folder
    /// that does not exist, and an empty folder field is better than a script that never runs.
    /// </summary>
    private string ScriptWorkingFolder(ExecutableStep step)
    {
        var folder = ReadValue(step, "folder").AsText().Trim();
        return folder.Length == 0 && _devices.Files.Exists(_devices.Files.BaseFolder)
            ? _devices.Files.BaseFolder
            : folder;
    }

    /// <summary>
    /// Removes the copies of the script the run left on disk: the script itself, and the mark the
    /// one kind that needs one writes. The script lives in the macro, so what is out there was only
    /// ever there for as long as the program needed to read it, and a tidy-up that fails is not
    /// worth failing the step over.
    /// </summary>
    private void TidyScript(string path, string sentinel, int depth, string type)
    {
        foreach (var file in new[] { path, sentinel })
        {
            if (file.Length == 0)
            {
                continue;
            }

            try
            {
                _devices.Files.Delete(file, false);
            }
            catch (Exception)
            {
                Log(LogLevel.Debug, depth, type, LogKeys.ScriptNotTidied, file);
            }
        }
    }

    /// <summary>Where the copies of a script are written while it runs.</summary>
    private static string ScriptFolder() => Path.Combine(Path.GetTempPath(), "WhaleGenie");

    /// <summary>
    /// The time a command is given, turned into one the process API understands: a step that asked
    /// for no limit is given one long enough that nobody reading the number is waiting for it.
    /// </summary>
    private static int ScriptLimitOf(int timeoutMs) => timeoutMs <= 0 ? int.MaxValue : timeoutMs;

    /// <summary>
    /// Fills the <c>{{name}}</c> placeholders with what those variables hold. A script is written in
    /// a language of its own — PowerShell, JavaScript, a batch file — and every one of them gives
    /// its own meaning to <c>$</c> and <c>%</c>, so a macro value is spelled differently in here on
    /// purpose. A name the macro does not know is left as it was written, where it is easy to see
    /// rather than quietly replaced by nothing.
    /// </summary>
    private string Template(string text)
        => Regex.Replace(text, @"\{\{(.+?)\}\}", match =>
        {
            var name = match.Groups[1].Value.Trim();
            return Variables.TryGet(name, out var value) ? value.AsText() : match.Value;
        });

    /// <summary>What a script said on its error output, kept short enough to read in a message.</summary>
    private static string Careful(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400] + "…";
    }
}
