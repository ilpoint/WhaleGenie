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
    /// <summary>Values WhaleGenie keeps itself, such as the date or the mouse position.</summary>
    public VariableBag System { get; } = new();

    /// <summary>
    /// Where the values that move are read from, when there is somewhere to read them: the program
    /// installs one, and a store built without a device layer has none and falls back to the values it
    /// was seeded with.
    /// </summary>
    public ILiveVariables? Live { get; set; }

    /// <summary>Values every macro shares.</summary>
    public VariableBag Global { get; } = new();

    /// <summary>Values only the running macro sees.</summary>
    public VariableBag Local { get; private set; } = new();

    /// <summary>
    /// Hands the running macro's values over to <paramref name="next"/> and gives back the table
    /// that was in use, so the caller can have its own back when the call is over. A macro called
    /// by another one runs on a table of its own — that is what "only the running macro sees"
    /// means — so what it works with is what it was handed, and what it leaves behind is whatever
    /// the call asked to have back.
    /// </summary>
    public VariableBag SwapLocal(VariableBag next)
    {
        var previous = Local;
        Local = next;
        return previous;
    }

    /// <summary>
    /// Reads a value, with local beating global and global beating WhaleGenie's own. The ones that
    /// move come before the seeded ones: what was seeded is the fallback for a machine that cannot
    /// answer, while the live source is the answer.
    /// </summary>
    public bool TryGet(string name, out Value value)
        => Local.TryGet(name, out value)
           || Global.TryGet(name, out value)
           || (Live is { } live && live.TryGet(name, out value))
           || System.TryGet(name, out value);

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

        foreach (var (name, value) in Local.Values)
        {
            all[name] = value;
        }

        return all;
    }
}
