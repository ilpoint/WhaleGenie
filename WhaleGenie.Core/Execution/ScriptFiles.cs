using System;
using System.IO;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// What kind of script a step writes, where it runs, how its file is named and how the copy is
/// tidied away. Everything about a script that is a fact about files rather than about running
/// one: the command line an interpreter needs, the ending its file must have, the code page it is
/// written in, and the folder it runs in.
/// </summary>
internal static class ScriptFiles
{
    /// <summary>The kind of script a step asks for, said in the step's <c>kind</c> field.</summary>
    public static ScriptKind KindOf(ExecutableStep step)
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
    public static string KindName(ScriptKind kind) => kind switch
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
    public static (string Program, string Arguments) CommandOf(IRunContext run,
        ExecutableStep step, ScriptKind kind, string path, string extra)
    {
        var quoted = $"\"{path}\"";
        var tail = extra.Length == 0 ? string.Empty : $" {extra}";

        if (kind is ScriptKind.Custom)
        {
            var named = run.Args(step).Text("interpreter").Trim();
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
            return (run.Devices.Scripts.AutoHotkeyProgram
                    ?? throw new StepFailure(RunKeys.NoAutoHotkey),
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
    public static string ExtensionOf(IRunContext run, ExecutableStep step, ScriptKind kind)
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

        var wanted = run.Args(step).Text("extension").Trim();
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
    public static string EncodingOf(ExecutableStep step, ScriptKind kind)
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
    public static string WorkingFolder(IRunContext run, ExecutableStep step)
    {
        var folder = run.Args(step).ValueOf("folder").AsText().Trim();
        return folder.Length == 0 && run.Devices.Files.Exists(run.Devices.Files.BaseFolder)
            ? run.Devices.Files.BaseFolder
            : folder;
    }

    /// <summary>
    /// Removes the copies of the script the run left on disk: the script itself, and the mark the
    /// one kind that needs one writes. The script lives in the macro, so what is out there was only
    /// ever there for as long as the program needed to read it, and a tidy-up that fails is not
    /// worth failing the step over.
    /// </summary>
    public static void Tidy(IRunContext run, string path, string sentinel, int depth, string type)
    {
        foreach (var file in new[] { path, sentinel })
        {
            if (file.Length == 0)
            {
                continue;
            }

            try
            {
                run.Devices.Files.Delete(file, false);
            }
            catch (Exception)
            {
                run.Log(LogLevel.Debug, depth, type, LogKeys.ScriptNotTidied, file);
            }
        }
    }

    /// <summary>Where the copies of a script are written while it runs.</summary>
    public static string Folder() => Path.Combine(Path.GetTempPath(), "WhaleGenie");

    /// <summary>
    /// The time a command is given, turned into one the process API understands: a step that asked
    /// for no limit is given one long enough that nobody reading the number is waiting for it.
    /// </summary>
    public static int LimitOf(int timeoutMs) => timeoutMs <= 0 ? int.MaxValue : timeoutMs;

    /// <summary>What a script said on its error output, kept short enough to read in a message.</summary>
    public static string Careful(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400] + "…";
    }
}
