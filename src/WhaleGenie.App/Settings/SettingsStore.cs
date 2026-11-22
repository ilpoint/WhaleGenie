using System;
using System.IO;
using System.Text.Json;

namespace WhaleGenie.Settings;

/// <summary>
/// One settings file: an in-memory model, atomic writes and a path a check can move. The static
/// facade above it is being replaced by an injected instance, but the file rules live here.
/// </summary>
internal sealed class SettingsStore(string filePath)
{
    private readonly object _gate = new();
    private string _filePath = filePath;
    private SettingsModel? _cached;
    private bool _loaded;

    internal string FilePath
    {
        get
        {
            lock (_gate)
            {
                return _filePath;
            }
        }

        set
        {
            lock (_gate)
            {
                _filePath = value;
                ForgetLocked();
            }
        }
    }

    internal void Forget()
    {
        lock (_gate)
        {
            ForgetLocked();
        }
    }

    internal SettingsModel Read()
    {
        lock (_gate)
        {
            if (_loaded)
            {
                return _cached!;
            }

            _loaded = true;
            try
            {
                _cached = File.Exists(_filePath)
                    ? JsonSerializer.Deserialize<SettingsModel>(File.ReadAllText(_filePath))
                        ?? new SettingsModel()
                    : new SettingsModel();
            }
            catch
            {
                _cached = new SettingsModel();
            }

            return _cached;
        }
    }

    internal void Write(Action<SettingsModel> change)
    {
        lock (_gate)
        {
            var model = Read();
            var temp = _filePath + ".tmp";
            try
            {
                change(model);
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(temp, JsonSerializer.Serialize(model));
                File.Move(temp, _filePath, overwrite: true);
            }
            catch
            {
                // A settings file that cannot be written must never break the app; the in-memory
                // choice still applies for this run.
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // Best effort: a leftover temp file is not worth surfacing to the person.
                }
            }
        }
    }

    private void ForgetLocked()
    {
        _cached = null;
        _loaded = false;
    }
}
