using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;

namespace WhaleGenie.Models;

/// <summary>
/// Values of global variables the person asked to keep on this machine. An entry is kept under the
/// declaration's identity, not its name: renaming the variable moves its label, and a project whose
/// global happens to share a name with another project's no longer inherits its saved value. A
/// value is stored as JSON rather than as the text it reads as, because a list with a comma in one
/// of its items and a number with more than six decimals both survive JSON and neither survives a
/// line meant for a person to read.
/// </summary>
internal static class PersistentVariables
{
    /// <summary>The shape this build writes. Version 1 kept names and the text values read as.</summary>
    private const int FileVersion = 2;

    private sealed record Saved(string Id, string Name, string Type, Value Value);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Saved> ByKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> KeyByName = new(StringComparer.OrdinalIgnoreCase);

    private static string _path = DefaultPath;
    private static Timer? _timer;
    private static bool _initialized;
    private static bool _applying;
    private static bool _backedUp;

    /// <summary>
    /// True when the file on disk was written by a newer build. It is left exactly as it is: an
    /// older program that overwrote it would drop values this one cannot read yet.
    /// </summary>
    private static bool _readOnly;

    internal static string FilePath => _path;

    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WhaleGenie", "persistent-variables.json");

    internal static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            Load();
        }

        SharedVariables.Current.Changed += OnGlobalChanged;
    }

    internal static bool TryGet(string name, out string value)
    {
        lock (Gate)
        {
            if (KeyByName.TryGetValue(name, out var key)
                && ByKey.TryGetValue(key, out var saved))
            {
                value = saved.Value.AsText();
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Applies saved values and checkbox states to the globals of a project being opened.</summary>
    internal static void ApplyTo(IEnumerable<VariableDefinition> definitions)
    {
        Initialize();
        _applying = true;
        try
        {
            foreach (var definition in definitions)
            {
                var saved = Find(definition.Name, definition.Id);
                if (saved is null)
                {
                    definition.PersistValue = false;
                    continue;
                }

                definition.PersistValue = true;
                SharedVariables.Current.Set(definition.Name, Fit(definition, saved.Value));
            }
        }
        finally
        {
            _applying = false;
        }
    }

    internal static void PersistChanged(VariableDefinition definition, bool enabled)
    {
        if (_applying || definition.Scope is not VariableScope.Global)
        {
            return;
        }

        if (!enabled)
        {
            lock (Gate)
            {
                Remove(definition.Name);
            }

            Schedule();
            return;
        }

        if (!SharedVariables.Current.TryGet(definition.Name, out var value))
        {
            value = VariableValues.Read(definition.Type, definition.DefaultValue,
                VariableValues.DesignContext());
            SharedVariables.Current.SetIfAbsent(definition.Name, value);
        }

        lock (Gate)
        {
            Store(new Saved(definition.Id, definition.Name, definition.Type, value));
        }

        Schedule();
    }

    internal static void Flush()
    {
        lock (Gate)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            Save();
        }
    }

    /// <summary>
    /// Moves what was kept for a declaration to the name it now goes by. The identity is what the
    /// entry is keyed on, so a rename only has to change the label; an entry kept under a name —
    /// one written by a build that had no identities — is moved as well.
    /// </summary>
    internal static void Rename(string oldName, string newName, string id = "")
    {
        lock (Gate)
        {
            var saved = Find(oldName, id);
            if (saved is null)
            {
                return;
            }

            var renamed = saved with { Name = newName };
            var oldKey = KeyOf(saved);
            KeyByName.Remove(saved.Name);
            Store(renamed);

            // An entry kept under a name rather than an identity moves to a new key; one kept
            // under an identity only changed its label.
            if (!string.Equals(oldKey, KeyOf(renamed), StringComparison.Ordinal))
            {
                ByKey.Remove(oldKey);
            }
        }

        Schedule();
    }

    internal static void Forget(string name)
    {
        lock (Gate)
        {
            Remove(name);
        }

        Schedule();
    }

    internal static IDisposable UsePathForTests(string path)
    {
        var previous = _path;
        lock (Gate)
        {
            _timer?.Dispose();
            _timer = null;
            ByKey.Clear();
            KeyByName.Clear();
            _path = path;
            _readOnly = false;
            _backedUp = false;
            Load();
        }

        return new RestorePath(previous);
    }

    private static void OnGlobalChanged(string name)
    {
        if (!SharedVariables.Current.TryGet(name, out var value))
        {
            return;
        }

        lock (Gate)
        {
            if (!KeyByName.TryGetValue(name, out var key)
                || !ByKey.TryGetValue(key, out var saved))
            {
                return;
            }

            ByKey[key] = saved with { Value = value };
        }

        Schedule();
    }

    private static void Schedule()
    {
        lock (Gate)
        {
            if (_readOnly)
            {
                return;
            }

            _timer ??= new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(250, Timeout.Infinite);
        }
    }

    /// <summary>
    /// The entry a declaration's value was kept under: its identity first, the name second. A build
    /// that had no identities wrote the name alone, and that file must still open.
    /// </summary>
    private static Saved? Find(string name, string id)
    {
        if (id.Length > 0)
        {
            if (ByKey.TryGetValue("i:" + id, out var byIdentity))
            {
                return byIdentity;
            }

            // A file written before identities existed keys its entries by name. One of those is
            // adopted by the declaration that now carries the name; an entry that belongs to a
            // different identity is another project's value and is left alone.
            return KeyByName.TryGetValue(name, out var key)
                && ByKey.TryGetValue(key, out var byName)
                && byName.Id.Length == 0
                    ? byName
                    : null;
        }

        return KeyByName.TryGetValue(name, out var legacy) && ByKey.TryGetValue(legacy, out var found)
            ? found
            : null;
    }

    private static string KeyOf(Saved saved)
        => saved.Id.Length > 0 ? "i:" + saved.Id : "n:" + saved.Name.ToLowerInvariant();

    /// <summary>Files one entry, taking over whatever the same name or identity held before.</summary>
    private static void Store(Saved saved)
    {
        var key = KeyOf(saved);

        if (KeyByName.TryGetValue(saved.Name, out var previous)
            && !string.Equals(previous, key, StringComparison.Ordinal))
        {
            ByKey.Remove(previous);
        }

        if (saved.Id.Length > 0
            && ByKey.TryGetValue(key, out var older)
            && !string.Equals(older.Name, saved.Name, StringComparison.OrdinalIgnoreCase))
        {
            KeyByName.Remove(older.Name);
        }

        ByKey[key] = saved;
        KeyByName[saved.Name] = key;
    }

    private static void Remove(string name)
    {
        if (KeyByName.Remove(name, out var key))
        {
            ByKey.Remove(key);
        }
    }

    /// <summary>
    /// The saved value as the declaration can hold it. A value that no longer fits the declared
    /// type — the declaration was changed, or the value was kept as text by an older build — falls
    /// back to the declared starting value rather than stopping the project from opening.
    /// </summary>
    private static Value Fit(VariableDefinition definition, Value saved)
        => Fits(definition.Type, saved)
            ? saved
            : VariableValues.Read(definition.Type, definition.DefaultValue,
                VariableValues.DesignContext());

    private static bool Fits(string type, Value value)
        => ValueRules.Accepts(VariableTypes.KindOf(type), value);

    private static void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
            if (root?["variables"] is not JsonArray variables)
            {
                return;
            }

            var version = root["version"] is JsonValue written && written.TryGetValue<int>(out var number)
                ? number
                : 1;

            // A file from a newer build is left alone. Reporting the values as unknown is better
            // than flattening them into this build's shape, which the newer one could not read back.
            if (version > FileVersion)
            {
                _readOnly = true;
                return;
            }

            foreach (var node in variables)
            {
                try
                {
                    if (node is JsonObject entry)
                    {
                        ReadEntry(entry, version);
                    }
                }
                catch (Exception)
                {
                    // One unreadable entry is not the whole file: the rest of the values are still
                    // worth keeping, and the person can fix the one that was lost.
                }
            }
        }
        catch (Exception)
        {
            ByKey.Clear();
            KeyByName.Clear();
        }
    }

    private static void ReadEntry(JsonObject entry, int version)
    {
        var name = entry["name"]?.GetValue<string>()?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return;
        }

        if (version < 2)
        {
            // Version 1 kept the value's text and nothing about its type. What kind it is comes
            // from the declaration when the project opens, which is why the type is left empty.
            var text = entry["value"];
            var legacy = text is JsonValue value && value.TryGetValue<string>(out var written)
                ? Value.FromText(written)
                : JsonValues.FromJson(text);
            Store(new Saved(string.Empty, name, string.Empty, legacy));
            return;
        }

        Store(new Saved(
            entry["id"]?.GetValue<string>() ?? string.Empty,
            name,
            entry["type"]?.GetValue<string>() ?? "text",
            JsonValues.FromJson(entry["value"])));
    }

    private static void Save()
    {
        try
        {
            if (_readOnly)
            {
                return;
            }

            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // The first write of this session keeps what the file held before it, so upgrading a
            // file this build understands stops being a one-way door.
            if (!_backedUp && File.Exists(_path))
            {
                File.Copy(_path, _path + ".bak", true);
                _backedUp = true;
            }

            var variables = ByKey.Values
                .OrderBy(saved => saved.Name, StringComparer.OrdinalIgnoreCase)
                .Select(saved => new JsonObject
                {
                    ["id"] = saved.Id,
                    ["name"] = saved.Name,
                    ["type"] = saved.Type,
                    ["value"] = JsonValues.Node(saved.Value),
                })
                .ToList();
            var json = JsonSerializer.Serialize(new JsonObject
            {
                ["version"] = FileVersion,
                ["variables"] = new JsonArray([.. variables]),
            }, new JsonSerializerOptions { WriteIndented = true });

            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, true);
        }
        catch (Exception)
        {
            // A bad local settings file must not stop the macro or the program from running.
        }
    }

    private sealed class RestorePath(string path) : IDisposable
    {
        public void Dispose()
        {
            lock (Gate)
            {
                _timer?.Dispose();
                _timer = null;
                ByKey.Clear();
                KeyByName.Clear();
                _path = path;
                _readOnly = false;
                _backedUp = false;
                Load();
            }
        }
    }
}
