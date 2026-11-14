using System;
using System.Collections.Generic;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Variables;

/// <summary>Which scope a value is written to.</summary>
public enum VariableTarget
{
    /// <summary>The macro's own value, invisible to every other macro.</summary>
    Local,

    /// <summary>A value every macro shares.</summary>
    Global,
}

/// <summary>
/// Values WhaleGenie provides that are only true for the moment they are read: where the pointer is,
/// what the clipboard holds, what the clock says. They are asked for rather than kept, because a
/// value read when a run started is not what a macro reading it a minute later means — a loop that
/// waits for the clock needs the clock to move, and asking twice has to be able to answer twice.
/// </summary>
public interface ILiveVariables
{
    /// <summary>
    /// Reads one of those values now, or answers false for a name that is not one of them. A name it
    /// does not know is left to whatever else the store holds, so a misspelled name is still reported
    /// as a variable nobody defined rather than quietly becoming nothing.
    /// </summary>
    bool TryGet(string name, out Value value);
}

/// <summary>
/// The three scopes a macro reads from. A local value shadows a global one with the same
/// name and a global value shadows a system one, so a macro can work on its own copy of a
/// value without disturbing the macros around it.
/// </summary>
public sealed class VariableStore : IVariableResolver
{
    /// <summary>
    /// A store with a table of its own. The program hands every run the same global table so that
    /// what one macro writes is what the next one reads; a store built without one is a store no
    /// other run can see, which is what a check that drives the engine on its own wants.
    /// </summary>
    public VariableStore(VariableBag? global = null)
    {
        Global = global ?? new VariableBag();
    }

    /// <summary>
    /// The round number belongs to the loop that is running, not to the machine: the engine puts
    /// it into the macro's own values for each round and takes it out again on the way out.
    /// </summary>
    public const string LoopIndexName = "sys.loopIndex";

    /// <summary>Values WhaleGenie keeps itself, such as the date or the mouse position.</summary>
    public VariableBag System { get; } = new();

    /// <summary>
    /// Where the values that move are read from, when there is somewhere to read them: the program
    /// installs one, and a store built without a device layer has none and falls back to the values it
    /// was seeded with.
    /// </summary>
    public ILiveVariables? Live { get; set; }

    /// <summary>Values every macro shares.</summary>
    public VariableBag Global { get; }

    /// <summary>
    /// Where a global's declared starting value comes from, for the steps that put one back. The
    /// engine does not know the variable catalogue, so the program hands in the reader; without
    /// one, clearing a global writes the empty value rather than guessing at a declaration.
    /// </summary>
    public Func<string, Value?>? GlobalDefault { get; set; }

    /// <summary>
    /// The tables of the calls that are running, outermost first. A call pushes its own table and
    /// pops it when it returns, so a reader always sees one frame whole rather than whichever one
    /// happened to be current when it asked.
    /// </summary>
    private readonly List<VariableBag> _frames = [new()];

    private readonly object _framesGate = new();

    /// <summary>Values only the running macro sees.</summary>
    public VariableBag Local
    {
        get
        {
            lock (_framesGate)
            {
                return _frames[^1];
            }
        }
    }

    /// <summary>
    /// Runs a called macro on a table of its own and gives back the table that was in use, so the
    /// caller can have its own back when the call is over. A macro called by another one runs on a
    /// table of its own — that is what "only the running macro sees" means — so what it works with
    /// is what it was handed, and what it leaves behind is whatever the call asked to have back.
    /// </summary>
    public void PushLocal(VariableBag next)
    {
        lock (_framesGate)
        {
            _frames.Add(next);
        }
    }

    /// <summary>Ends a call and answers with the table it was running on.</summary>
    public VariableBag PopLocal()
    {
        lock (_framesGate)
        {
            var top = _frames[^1];
            if (_frames.Count > 1)
            {
                _frames.RemoveAt(_frames.Count - 1);
            }

            return top;
        }
    }

    /// <summary>
    /// Reads a value, with local beating global and global beating WhaleGenie's own. The ones that
    /// move come before the seeded ones: what was seeded is the fallback for a machine that cannot
    /// answer, while the live source is the answer.
    /// </summary>
    public bool TryGet(string name, out Value value)
    {
        // One frame for the whole question: the call under way may finish between the two lookups,
        // and half of a caller's table beside half of a callee's is a value nobody wrote.
        var local = Local;
        if (local.TryGet(name, out value) || Global.TryGet(name, out value))
        {
            return true;
        }

        return (Live is { } live && live.TryGet(name, out value))
            || System.TryGet(name, out value);
    }

    /// <summary>Writes a value into one scope.</summary>
    public void Set(string name, Value value, VariableTarget target = VariableTarget.Local)
    {
        var bag = target is VariableTarget.Global ? Global : Local;
        bag.Set(name, value);
    }

    public void SetText(string name, string? text, VariableTarget target = VariableTarget.Local)
        => Set(name, Value.FromText(text), target);

    public void SetNumber(string name, double number, VariableTarget target = VariableTarget.Local)
        => Set(name, Value.FromNumber(number), target);

    /// <summary>Every visible value in one dictionary, which is what a watcher shows.</summary>
    public IReadOnlyDictionary<string, Value> Flatten()
    {
        var local = Local;
        var all = new Dictionary<string, Value>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in System.Values)
        {
            // The watch list says what a macro reads while it runs, so the ones that move are read
            // again here rather than reported at the value they had when the run began.
            all[name] = Live is { } live && live.TryGet(name, out var current) ? current : value;
        }

        foreach (var (name, value) in Global.Values)
        {
            all[name] = value;
        }

        foreach (var (name, value) in local.Values)
        {
            all[name] = value;
        }

        return all;
    }
}
