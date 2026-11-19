using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// What the runtime remembers about each armed trigger: whether the watched pixel matched, when a
/// schedule is next due, which programs were running, whether a window was there, and whether a
/// "run once" trigger has had its turn.
/// </summary>
/// <remarks>
/// Everything is named by the entry's stable id rather than by the entry object. A plan is
/// republished every time the project changes, and each publish is a new set of entries; keying by
/// the object would make every edit look like "all of this is gone" and reset every watch. Keying by
/// id lets an edit that leaves the settings alone leave the watch alone.
/// </remarks>
internal sealed class TriggerStateStore
{
    private readonly Dictionary<string, bool> _colour = [];
    private readonly Dictionary<string, DateTime> _dueAt = [];
    private readonly Dictionary<string, HashSet<int>> _programs = [];
    private readonly Dictionary<string, bool> _windowThere = [];
    private readonly HashSet<string> _idleDone = [];
    private readonly HashSet<string> _spent = [];

    /// <summary>The settings each id was last armed with, so a changed watch can be told from an edit.</summary>
    private readonly Dictionary<string, TriggerSpec> _specs = [];

    private readonly object _gate = new();

    public bool IsSpent(string id)
    {
        lock (_gate)
        {
            return _spent.Contains(id);
        }
    }

    public void MarkSpent(string id)
    {
        lock (_gate)
        {
            _spent.Add(id);
        }
    }

    /// <summary>Whether the watched pixel matched, or null before the first look.</summary>
    public bool? Colour(string id)
    {
        lock (_gate)
        {
            return _colour.TryGetValue(id, out var matched) ? matched : null;
        }
    }

    /// <summary>Records what a look found. True when it differs from what was found before.</summary>
    public bool ColourChanged(string id, bool matched)
    {
        lock (_gate)
        {
            var known = _colour.TryGetValue(id, out var before);
            _colour[id] = matched;
            return known && before != matched;
        }
    }

    public DateTime? Due(string id)
    {
        lock (_gate)
        {
            return _dueAt.TryGetValue(id, out var due) ? due : null;
        }
    }

    public void SetDue(string id, DateTime due)
    {
        lock (_gate)
        {
            _dueAt[id] = due;
        }
    }

    public void ClearDue(string id)
    {
        lock (_gate)
        {
            _dueAt.Remove(id);
        }
    }

    /// <summary>Whether any process of that name is running, or null before the first count.</summary>
    public bool? Program(string id)
    {
        lock (_gate)
        {
            return _programs.TryGetValue(id, out var ids) ? ids.Count > 0 : null;
        }
    }

    /// <summary>
    /// What this count says the last one did not, or null on the first count: counting starts where
    /// the watching does, so a program that was already open has not "just started".
    /// </summary>
    public (bool Started, bool Stopped)? ProgramChanged(string id, IReadOnlyCollection<int> running)
    {
        lock (_gate)
        {
            var now = new HashSet<int>(running);
            var known = _programs.TryGetValue(id, out var before);
            _programs[id] = now;

            return known
                ? (now.Except(before!).Any(), before!.Except(now).Any())
                : null;
        }
    }

    /// <summary>Whether a matching window is there, or null before the first look.</summary>
    public bool? Window(string id)
    {
        lock (_gate)
        {
            return _windowThere.TryGetValue(id, out var there) ? there : null;
        }
    }

    /// <summary>Records what a look found. True when it differs from what was found before.</summary>
    public bool WindowChanged(string id, bool there)
    {
        lock (_gate)
        {
            var known = _windowThere.TryGetValue(id, out var before);
            _windowThere[id] = there;
            return known && before != there;
        }
    }

    /// <summary>Marks a quiet spell as spent. True the first time, which is the run's only turn.</summary>
    public bool MarkIdleDone(string id)
    {
        lock (_gate)
        {
            return _idleDone.Add(id);
        }
    }

    /// <summary>Lets the next quiet spell run this macro again, because the machine is in use.</summary>
    public void ClearIdleDone(string id)
    {
        lock (_gate)
        {
            _idleDone.Remove(id);
        }
    }

    /// <summary>Arms the given entries again, forgetting what they had already used up.</summary>
    public void Rearm(IEnumerable<string> ids)
    {
        lock (_gate)
        {
            foreach (var id in ids)
            {
                _spent.Remove(id);
                _colour.Remove(id);
                _dueAt.Remove(id);
                _programs.Remove(id);
                _windowThere.Remove(id);
                _idleDone.Remove(id);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _colour.Clear();
            _dueAt.Clear();
            _programs.Clear();
            _windowThere.Clear();
            _idleDone.Clear();
            _spent.Clear();
            _specs.Clear();
        }
    }

    /// <summary>
    /// Drops what belongs to entries that are no longer armed, or that are now watching something
    /// else. Returns the ids it let go of, so a file watch belonging to one can be closed too.
    /// </summary>
    public IReadOnlyList<string> Reconcile(TriggerPlan plan)
    {
        var dropped = new List<string>();
        lock (_gate)
        {
            var live = new Dictionary<string, TriggerSpec>(StringComparer.Ordinal);
            foreach (var armed in plan.Entries)
            {
                live[armed.Id] = armed.Spec;
            }

            foreach (var (id, spec) in _specs)
            {
                if (!live.TryGetValue(id, out var now) || now != spec)
                {
                    dropped.Add(id);
                }
            }

            foreach (var id in dropped)
            {
                _colour.Remove(id);
                _dueAt.Remove(id);
                _programs.Remove(id);
                _windowThere.Remove(id);
                _idleDone.Remove(id);
            }

            // A "spent" trigger keeps that fact for as long as the entry exists: switching an
            // armed entry off and on again is not the same as re-arming it.
            var known = new HashSet<string>(plan.Known, StringComparer.Ordinal);
            foreach (var id in _spent.Where(id => !known.Contains(id)).ToList())
            {
                _spent.Remove(id);
            }

            _specs.Clear();
            foreach (var (id, spec) in live)
            {
                _specs[id] = spec;
            }
        }

        return dropped;
    }
}
