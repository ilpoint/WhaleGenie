using System;
using System.Collections.Generic;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// The file watches a plan currently needs, and nobody else's. A watch holds an operating-system
/// handle, so one that is no longer wanted has to be closed rather than only forgotten — including
/// when the whole system is switched off.
/// </summary>
internal sealed class WatchPool : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, FileWatch> _watches = [];

    /// <summary>How many watches hold a handle right now, which is what a leak changes.</summary>
    public int LiveCount
    {
        get
        {
            lock (_gate)
            {
                return _watches.Count;
            }
        }
    }

    /// <summary>
    /// The watch for one entry, built the first time it is asked for and rebuilt when the path or
    /// the settings behind it change.
    /// </summary>
    public FileWatch Acquire(string id, FileSpec spec, string path)
    {
        lock (_gate)
        {
            if (_watches.TryGetValue(id, out var existing))
            {
                if (existing.Signature == FileWatch.SignatureOf(spec, path))
                {
                    return existing;
                }

                existing.Dispose();
                _watches.Remove(id);
            }

            var watch = FileWatch.Create(spec, path);
            _watches[id] = watch;
            return watch;
        }
    }

    /// <summary>Whether an entry currently holds a watch.</summary>
    public bool Holds(string id)
    {
        lock (_gate)
        {
            return _watches.ContainsKey(id);
        }
    }

    /// <summary>Closes the watches of entries that are no longer armed.</summary>
    public void Reconcile(IReadOnlyCollection<string> live)
    {
        var keep = new HashSet<string>(live, StringComparer.Ordinal);
        List<FileWatch> closing = [];
        lock (_gate)
        {
            foreach (var (id, watch) in new List<KeyValuePair<string, FileWatch>>(_watches))
            {
                if (keep.Contains(id))
                {
                    continue;
                }

                closing.Add(watch);
                _watches.Remove(id);
            }
        }

        foreach (var watch in closing)
        {
            watch.Dispose();
        }
    }

    public void Dispose()
    {
        List<FileWatch> closing;
        lock (_gate)
        {
            closing = [.. _watches.Values];
            _watches.Clear();
        }

        foreach (var watch in closing)
        {
            watch.Dispose();
        }
    }
}
