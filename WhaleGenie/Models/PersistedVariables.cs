using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Models;

/// <summary>
/// Values of global variables the person asked to keep on this machine. An entry is kept under the
/// declaration's identity, not its name: renaming the variable moves its label, and a project whose
/// global happens to share a name with another project's no longer inherits its saved value. A
/// value is stored as JSON rather than as the text it reads as, because a list with a comma in one
/// of its items and a number with more than six decimals both survive JSON and neither survives a
/// line meant for a person to read.
///
/// <para>
/// This is a service, not a static class: the program builds one for the real file, a test builds
/// one for a temporary file, and a caller that wants none builds one with no path at all. What it
/// watches is handed in, so nothing here reaches for a global table on its own.
/// </para>
/// </summary>
public sealed class PersistedVariables : IDisposable
{
    /// <summary>The shape this build writes. Version 1 kept names and the text values read as.</summary>
    private const int FileVersion = 2;

    private sealed record Saved(string Id, string Name, ValueKind Type, Value Value);

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, Saved> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _keyByName = new(StringComparer.OrdinalIgnoreCase);

    private VariableBag? _shared;
    private Timer? _timer;
    private bool _loaded;
    private bool _applying;
    private bool _backedUp;

    /// <summary>
    /// True when the file on disk was written by a newer build. It is left exactly as it is: an
    /// older program that overwrote it would drop values this one cannot read yet.
    /// </summary>
    private bool _readOnly;

    /// <param name="path">
    /// Where the values are kept, or null to keep them nowhere: a caller that only needs the shape
    /// — a test, or a window editing a library that has no project — gets an empty store that never
    /// reads or writes a file.
    /// </param>
    public PersistedVariables(string? path = null) => _path = path ?? string.Empty;

    /// <summary>A file in this user's local application data, beside the other settings.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WhaleGenie", "persistent-variables.json");

    /// <summary>Where these values are kept; empty when they are kept nowhere.</summary>
    public string FilePath => _path;

    /// <summary>
    /// Reads the file and starts watching the table for values a run writes. Called once by the
    /// program; a store with no path keeps nothing and has nothing to watch.
    /// </summary>
    public void Initialize(VariableBag? shared = null)
    {
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _shared = shared;
            Load();
        }

        if (shared is not null)
        {
            shared.Changed += OnGlobalChanged;
        }
    }

    /// <summary>
    /// The values a project's declarations have saved, and whether each one is kept. Opening a
    /// project gets a list and decides what to do with it: nothing here writes into a declaration
    /// or into a table while somebody else is opening one.
    /// </summary>
    public IReadOnlyList<(string Name, Value Value, bool Persisted)> ApplyTo(
        IEnumerable<VariableDefinition> definitions)
    {
        Initialize();
        var applied = new List<(string, Value, bool)>();
        _applying = true;
        try
        {
            foreach (var definition in definitions)
            {
                var saved = Find(definition.Name, definition.Id);
                if (saved is null)
                {
                    applied.Add((definition.Name, Value.Null, false));
                    continue;
                }

                applied.Add((definition.Name, Fit(definition, saved.Value), true));
            }
        }
        finally
        {
            _applying = false;
        }

        return applied;
    }

    /// <summary>Keeps a global's current value, or stops keeping it.</summary>
    public void PersistChanged(VariableDefinition definition, bool enabled)
    {
        if (_applying || definition.Scope is not VariableScope.Global)
        {
            return;
        }

        if (!enabled)
        {
            lock (_gate)
            {
                Remove(definition.Name);
            }

            Schedule();
            return;
        }

        if (_shared is null || !_shared.TryGet(definition.Name, out var value))
        {
            value = VariableValues.Read(definition.Type, definition.DefaultValue,
                VariableValues.DesignContext());
            _shared?.SetIfAbsent(definition.Name, value);
        }

        lock (_gate)
        {
            Store(new Saved(definition.Id, definition.Name, definition.Type, value));
        }

        Schedule();
    }

    /// <summary>Writes whatever has changed now, instead of waiting for the quiet moment.</summary>
    public void Flush()
    {
        lock (_gate)
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
    public void Rename(string oldName, string newName, string id = "")
    {
        lock (_gate)
        {
            var saved = Find(oldName, id);
            if (saved is null)
            {
                return;
            }

            var renamed = saved with { Name = newName };
            var oldKey = KeyOf(saved);
            _keyByName.Remove(saved.Name);
            Store(renamed);

            // An entry kept under a name rather than an identity moves to a new key; one kept
            // under an identity only changed its label.
            if (!string.Equals(oldKey, KeyOf(renamed), StringComparison.Ordinal))
            {
                _byKey.Remove(oldKey);
            }
        }

        Schedule();
    }

    /// <summary>Stops keeping a name, because the project no longer declares it.</summary>
    public void Forget(string name)
    {
        lock (_gate)
        {
            Remove(name);
        }

        Schedule();
    }

    /// <summary>What a name is held as, for the windows that show it before a run writes one.</summary>
    public bool TryGet(string name, out string value)
    {
        lock (_gate)
        {
            if (_keyByName.TryGetValue(name, out var key)
                && _byKey.TryGetValue(key, out var saved))
            {
                value = saved.Value.AsText();
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public void Dispose()
    {
        if (_shared is not null)
        {
            _shared.Changed -= OnGlobalChanged;
            _shared = null;
        }

        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            Save();
        }
    }

    private void OnGlobalChanged(string name)
    {
        if (_shared is null || !_shared.TryGet(name, out var value))
        {
            return;
        }

        lock (_gate)
        {
            if (!_keyByName.TryGetValue(name, out var key) || !_byKey.TryGetValue(key, out var saved))
            {
                return;
            }

            _byKey[key] = saved with { Value = value };
        }

        Schedule();
    }

    private void Schedule()
    {
        lock (_gate)
        {
            if (_readOnly || _path.Length == 0)
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
    private Saved? Find(string name, string id)
    {
        if (id.Length > 0)
        {
            if (_byKey.TryGetValue("i:" + id, out var byIdentity))
            {
                return byIdentity;
            }

            // A file written before identities existed keys its entries by name. One of those is
            // adopted by the declaration that now carries the name; an entry that belongs to a
            // different identity is another project's value and is left alone.
            return _keyByName.TryGetValue(name, out var key)
                && _byKey.TryGetValue(key, out var byName)
                && byName.Id.Length == 0
                    ? byName
                    : null;
        }

        return _keyByName.TryGetValue(name, out var legacy)
            && _byKey.TryGetValue(legacy, out var found)
                ? found
                : null;
    }

    private static string KeyOf(Saved saved)
        => saved.Id.Length > 0 ? "i:" + saved.Id : "n:" + saved.Name.ToLowerInvariant();

    /// <summary>Files one entry, taking over whatever the same name or identity held before.</summary>
    private void Store(Saved saved)
    {
        var key = KeyOf(saved);

        if (_keyByName.TryGetValue(saved.Name, out var previous)
            && !string.Equals(previous, key, StringComparison.Ordinal))
        {
            _byKey.Remove(previous);
        }

        if (saved.Id.Length > 0
            && _byKey.TryGetValue(key, out var older)
            && !string.Equals(older.Name, saved.Name, StringComparison.OrdinalIgnoreCase))
        {
            _keyByName.Remove(older.Name);
        }

        _byKey[key] = saved;
        _keyByName[saved.Name] = key;
    }

    private void Remove(string name)
    {
        if (_keyByName.Remove(name, out var key))
        {
            _byKey.Remove(key);
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

    private static bool Fits(ValueKind type, Value value) => ValueRules.Accepts(type, value);

    private void Load()
    {
        if (_path.Length == 0)
        {
            return;
        }

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
            _byKey.Clear();
            _keyByName.Clear();
        }
    }

    private void ReadEntry(JsonObject entry, int version)
    {
        var name = entry["name"]?.GetValue<string>()?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return;
        }

        if (version < 2)
        {
            // Version 1 kept the value's text and nothing about its type. What kind it is comes
            // from the declaration when the project opens, so it is read as the text it is.
            var text = entry["value"];
            var legacy = text is JsonValue value && value.TryGetValue<string>(out var written)
                ? Value.FromText(written)
                : JsonValues.FromJson(text);
            Store(new Saved(string.Empty, name, ValueKind.Text, legacy));
            return;
        }

        Store(new Saved(
            entry["id"]?.GetValue<string>() ?? string.Empty,
            name,
            VariableTypes.KindOf(entry["type"]?.GetValue<string>()),
            JsonValues.FromJson(entry["value"])));
    }

    private void Save()
    {
        try
        {
            if (_readOnly || _path.Length == 0)
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

            var variables = _byKey.Values
                .OrderBy(saved => saved.Name, StringComparer.OrdinalIgnoreCase)
                .Select(saved => new JsonObject
                {
                    ["id"] = saved.Id,
                    ["name"] = saved.Name,
                    ["type"] = VariableTypes.Name(saved.Type),
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
}
