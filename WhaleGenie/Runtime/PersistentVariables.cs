using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Values of global variables the person asked to keep on this machine. They are keyed by name,
/// so the same name shares its saved value across projects; the project still owns the declaration.
/// </summary>
internal static class PersistentVariables
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> Values =
        new(StringComparer.OrdinalIgnoreCase);

    private static string _path = DefaultPath;
    private static Timer? _timer;
    private static bool _initialized;
    private static bool _applying;

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
            return Values.TryGetValue(name, out value!);
        }
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
                if (TryGet(definition.Name, out var text))
                {
                    definition.PersistValue = true;
                    SharedVariables.Current.Set(definition.Name,
                        Convert(definition.Type, text, definition.DefaultValue));
                }
                else
                {
                    definition.PersistValue = false;
                }
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
                Values.Remove(definition.Name);
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
            Values[definition.Name] = value.AsText();
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

    internal static void Rename(string oldName, string newName)
    {
        lock (Gate)
        {
            if (Values.Remove(oldName, out var value))
            {
                Values[newName] = value;
            }
        }

        Schedule();
    }

    internal static void Forget(string name)
    {
        lock (Gate)
        {
            Values.Remove(name);
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
            Values.Clear();
            _path = path;
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
            if (!Values.ContainsKey(name))
            {
                return;
            }

            Values[name] = value.AsText();
        }

        Schedule();
    }

    private static void Schedule()
    {
        lock (Gate)
        {
            _timer ??= new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(250, Timeout.Infinite);
        }
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            foreach (var item in document.RootElement.GetProperty("variables").EnumerateArray())
            {
                var name = item.GetProperty("name").GetString();
                var value = item.GetProperty("value").GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    Values[name] = value;
                }
            }
        }
        catch (Exception)
        {
            Values.Clear();
        }
    }

    private static void Save()
    {
        try
        {
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var variables = Values
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new Dictionary<string, object>
                {
                    ["name"] = pair.Key,
                    ["value"] = pair.Value,
                })
                .ToList();
            var json = JsonSerializer.Serialize(new
            {
                version = 1,
                variables,
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

    private static Value Convert(string type, string text, string fallback) => type switch
    {
        "number" => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture,
            out var number)
            ? Value.FromNumber(number)
            : VariableValues.Read(type, fallback, VariableValues.DesignContext()),
        "bool" => bool.TryParse(text, out var flag)
            ? Value.FromBool(flag)
            : VariableValues.Read(type, fallback, VariableValues.DesignContext()),
        "list" => ListText.TryParse(text, out var items, out _)
            ? Value.FromList(items)
            : VariableValues.Read(type, fallback, VariableValues.DesignContext()),
        _ => Value.FromText(text),
    };

    private sealed class RestorePath(string path) : IDisposable
    {
        public void Dispose()
        {
            lock (Gate)
            {
                _timer?.Dispose();
                _timer = null;
                Values.Clear();
                _path = path;
                Load();
            }
        }
    }
}
