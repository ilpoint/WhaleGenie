using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Storage;

/// <summary>
/// The macro the editor was holding when the program stopped, and which macro in the list it
/// stood for. The editor is a window of its own and its work is not in the list yet, so it has to
/// be kept apart from the project the snapshot also carries.
/// </summary>
public sealed class RecoveryEditor
{
    public required MacroItem Macro { get; init; }

    /// <summary>Name of the macro the draft was editing, or empty when it was a new macro.</summary>
    public required string Replaces { get; init; }
}

/// <summary>Everything the program was holding that had not been written to a package.</summary>
public sealed class RecoveryContents
{
    public required IReadOnlyList<MacroItem> Macros { get; init; }

    public required IReadOnlyList<VariableDefinition> Globals { get; init; }

    /// <summary>Package the macro list came from, or null when it had never been saved.</summary>
    public string? PackagePath { get; init; }

    /// <summary>The macro the editor was holding, or null when no editor was open.</summary>
    public RecoveryEditor? Editor { get; init; }

    /// <summary>When this snapshot was last written.</summary>
    public DateTime? SavedUtc { get; init; }

    /// <summary>True when there is nothing in it worth offering back.</summary>
    public bool IsEmpty => Macros.Count == 0 && PackagePath is null && Editor is null;
}

/// <summary>
/// The safety net under the two places work is written by hand: the macro list, which only
/// reaches disk when the project is saved, and the macro editor, whose macro has not joined the
/// list yet. Neither is on disk when the program is stopped without notice — a crash, or the
/// machine losing power — so the work is written out as it changes, and the next run can offer
/// it back.
/// </summary>
/// <remarks>
/// One file holds both the project and the editor draft, written by whoever has something to
/// keep. Each writer changes only its own part and leaves the other where it was, so the two
/// never overwrite each other's work. Writes go to a temporary file and are moved into place,
/// the way a package is written, so a stop in the middle of a write cannot leave a half file
/// where a whole one was.
/// </remarks>
public static class RecoveryStore
{
    /// <summary>
    /// How long the unsaved work may sit in memory before it is written out. Short enough that
    /// losing power costs a few seconds of work at most, long enough that a keystroke is not a
    /// disk write.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Where the snapshot lives: beside the program, with the settings, so the safety net belongs
    /// to this copy and can be moved out of the way by a check.
    /// </summary>
    internal static string FilePath { get; set; } = Path.Combine(AppPaths.Root, "recovery.json");

    /// <summary>True when the last run left something behind.</summary>
    public static bool Exists
    {
        get
        {
            lock (Gate)
            {
                return File.Exists(FilePath);
            }
        }
    }

    /// <summary>Records the macro list, its shared variables and the package it came from.</summary>
    public static void SaveProject(IReadOnlyList<MacroItem> macros,
        IReadOnlyList<VariableDefinition> globals, string? packagePath)
        => Update(root =>
        {
            var entries = new JsonArray();
            foreach (var macro in macros)
            {
                entries.Add(new JsonObject
                {
                    ["enabled"] = macro.IsEnabled,
                    ["document"] = macro.ToJson(),
                });
            }

            var variables = new JsonArray();
            foreach (var variable in globals)
            {
                variables.Add(new JsonObject
                {
                    ["name"] = variable.Name,
                    ["type"] = variable.Type,
                    ["default"] = variable.DefaultValue,
                    ["description"] = variable.Description,
                });
            }

            var project = new JsonObject
            {
                ["macros"] = entries,
                ["variables"] = variables,
            };

            if (!string.IsNullOrEmpty(packagePath))
            {
                project["package"] = packagePath;
            }

            root["project"] = project;
        });

    /// <summary>Forgets the macro list, because it is saved or the user let it go.</summary>
    public static void ClearProject() => Update(root => root.Remove("project"));

    /// <summary>Records the macro the editor is holding, and what it stands for.</summary>
    public static void SaveEditor(MacroItem macro, string replaces)
        => Update(root =>
        {
            var editor = new JsonObject { ["macro"] = macro.ToJson() };
            if (!string.IsNullOrEmpty(replaces))
            {
                editor["replaces"] = replaces;
            }

            root["editor"] = editor;
        });

    /// <summary>Forgets the editor draft, because the editor closed or handed its macro over.</summary>
    public static void ClearEditor() => Update(root => root.Remove("editor"));

    /// <summary>Removes the whole snapshot.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Remove();
        }
    }

    /// <summary>Reads the snapshot back, or null when there is nothing to recover.</summary>
    public static RecoveryContents? Load()
    {
        lock (Gate)
        {
            try
            {
                return Parse(ReadRoot());
            }
            catch
            {
                // A file that cannot be read is not worth keeping and must not stop the program
                // from opening; the next write replaces it.
                Remove();
                return null;
            }
        }
    }

    private static RecoveryContents? Parse(JsonObject? root)
    {
        if (root is null)
        {
            return null;
        }

        var project = root["project"] as JsonObject;
        var macros = new List<MacroItem>();
        foreach (var item in project?["macros"] as JsonArray ?? [])
        {
            if (item is not JsonObject entry || entry["document"] is not JsonObject document)
            {
                continue;
            }

            var macro = MacroItem.FromJson(document);
            macro.IsEnabled = entry["enabled"]?.GetValue<bool>() ?? true;
            macros.Add(macro);
        }

        var globals = new List<VariableDefinition>();
        foreach (var item in project?["variables"] as JsonArray ?? [])
        {
            if (item is not JsonObject variable
                || variable["name"]?.GetValue<string>() is not { Length: > 0 } name)
            {
                continue;
            }

            globals.Add(new VariableDefinition
            {
                Name = name,
                Scope = VariableScope.Global,
                Type = variable["type"]?.GetValue<string>() is { Length: > 0 } type ? type : "text",
                DefaultValue = variable["default"]?.GetValue<string>() ?? string.Empty,
                Description = variable["description"]?.GetValue<string>() ?? string.Empty,
            });
        }

        RecoveryEditor? editor = null;
        if (root["editor"] is JsonObject editorEntry && editorEntry["macro"] is JsonObject macroNode)
        {
            editor = new RecoveryEditor
            {
                Macro = MacroItem.FromJson(macroNode),
                Replaces = editorEntry["replaces"]?.GetValue<string>() ?? string.Empty,
            };
        }

        var contents = new RecoveryContents
        {
            Macros = macros,
            Globals = globals,
            PackagePath = project?["package"]?.GetValue<string>(),
            Editor = editor,
            SavedUtc = DateTime.TryParse(root["savedUtc"]?.GetValue<string>(),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var saved)
                ? saved
                : null,
        };

        return contents.IsEmpty ? null : contents;
    }

    /// <summary>
    /// Reads the file, hands the root to <paramref name="change"/>, then writes it back — or
    /// removes it when both parts are gone. Everything under one lock, so the project writer and
    /// the editor writer never leave each other a half-read file.
    /// </summary>
    private static void Update(Action<JsonObject> change)
    {
        lock (Gate)
        {
            try
            {
                var root = ReadRoot() ?? new JsonObject();
                change(root);

                if (root["project"] is null && root["editor"] is null)
                {
                    Remove();
                    return;
                }

                root["appVersion"] = AppVersion();
                root["savedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                Write(root);
            }
            catch
            {
                // The safety net failing must never be what takes the program down.
            }
        }
    }

    private static JsonObject? ReadRoot()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        return JsonNode.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as JsonObject;
    }

    private static void Write(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        // Built beside the file first, so a stop in the middle cannot damage what was there.
        var temporary = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, root.ToJsonString(Indented), new UTF8Encoding(false));
            File.Move(temporary, FilePath, true);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    private static void Remove()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            // Nothing to do about a file that will not go; the next run offers it once more.
        }
    }

    private static string AppVersion()
        => typeof(RecoveryStore).Assembly.GetName().Version is { } version
            ? $"v{version.Major}.{version.Minor}.{version.Build}"
            : "unknown";
}
