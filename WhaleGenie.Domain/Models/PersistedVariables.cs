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
/// name the macro reads, which is the same key a run uses: renaming a variable moves its entry, and
/// two globals cannot share one. A value is stored as JSON rather than as the text it reads as,
/// because a list with a comma in one of its items and a number with more than six decimals both
/// survive JSON and neither survives a line meant for a person to read.
///
/// <para>
/// This is a service, not a static class: the program builds one for the real file, a test builds
/// one for a temporary file, and a caller that wants none builds one with no path at all. What it
/// watches is handed in, so nothing here reaches for a global table on its own.
/// </para>
/// </summary>
public sealed class PersistedVariables : IDisposable
{
    /// <summary>The one shape this build reads and writes.</summary>
    private const int FileVersion = 2;

    private sealed record Saved(string Name, Value Value);

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, Saved> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The one shape the file is written in; made once rather than on every save.</summary>
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private VariableSession? _session;
    private Timer? _timer;
    private bool _loaded;
    private bool _applying;
    private bool _backedUp;

    /// <summary>
    /// True when the file on disk was written by a build that keeps more than this one understands.
    /// It is left exactly as it is rather than overwritten with values that build cannot read back.
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
    public void Initialize(VariableSession? session = null)
    {
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _session = session;
            Load();
        }

        if (session is not null)
        {
            session.Values.Changed += OnGlobalChanged;
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
                var saved = Find(definition.Name);
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
                _byName.Remove(definition.Name);
            }

            Schedule();
            return;
        }

        if (_session is null || !_session.Values.TryGet(definition.Name, out var value))
        {
            value = VariableValues.Read(definition.Type, definition.Default, _session?.Design);
            _session?.Values.SetIfAbsent(definition.Name, value);
        }

        lock (_gate)
        {
            _byName[definition.Name] = new Saved(definition.Name, value);
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

    /// <summary>Moves what was kept for a declaration to the name it now goes by.</summary>
    public void Rename(string oldName, string newName)
    {
        lock (_gate)
        {
            if (!_byName.Remove(oldName, out var saved))
            {
                return;
            }

            _byName[newName.Trim()] = saved with { Name = newName.Trim() };
        }

        Schedule();
    }

    /// <summary>Stops keeping a name, because the project no longer declares it.</summary>
    public void Forget(string name)
    {
        lock (_gate)
        {
            _byName.Remove(name);
        }

        Schedule();
    }

    /// <summary>What a name is held as, for the windows that show it before a run writes one.</summary>
    public bool TryGet(string name, out string value)
    {
        lock (_gate)
        {
            if (_byName.TryGetValue(name, out var saved))
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
        if (_session is not null)
        {
            _session.Values.Changed -= OnGlobalChanged;
            _session = null;
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
        if (_session is null || !_session.Values.TryGet(name, out var value))
        {
            return;
        }

        lock (_gate)
        {
            if (!_byName.TryGetValue(name, out var saved))
            {
                return;
            }

            _byName[name] = saved with { Value = value };
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

    private Saved? Find(string name)
        => _byName.GetValueOrDefault(name);

    /// <summary>
    /// The saved value as the declaration can hold it. A value that no longer fits the declared
    /// type — the declaration was changed — falls back to the declared starting value rather than
    /// stopping the project from opening.
    /// </summary>
    private Value Fit(VariableDefinition definition, Value saved)
        => ValueRules.Accepts(definition.Type, saved)
            ? saved
            : VariableValues.Read(definition.Type, definition.Default, _session?.Design);

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
                : 0;

            // A file from a build that keeps more than this one understands is left alone.
            if (version > FileVersion)
            {
                _readOnly = true;
                return;
            }

            // An older shape is not migrated: this build starts from what the declarations say and
            // writes its own shape on the first change.
            if (version < FileVersion)
            {
                return;
            }

            foreach (var node in variables)
            {
                try
                {
                    if (node is JsonObject entry)
                    {
                        ReadEntry(entry);
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
            _byName.Clear();
        }
    }

    private void ReadEntry(JsonObject entry)
    {
        var name = entry["name"]?.GetValue<string>()?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return;
        }

        _byName[name] = new Saved(name, JsonValues.FromJson(entry["value"]));
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

            var variables = _byName.Values
                .OrderBy(saved => saved.Name, StringComparer.OrdinalIgnoreCase)
                .Select(saved => new JsonObject
                {
                    ["name"] = saved.Name,
                    ["value"] = JsonValues.Node(saved.Value),
                })
                .ToList();
            var json = JsonSerializer.Serialize(new JsonObject
            {
                ["version"] = FileVersion,
                ["variables"] = new JsonArray([.. variables]),
            }, WriteOptions);

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
