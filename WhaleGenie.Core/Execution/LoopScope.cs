using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Names a loop writes for its own rounds. They belong to the innermost loop that names them:
/// leaving it puts back what the loop around it had, so an inner "for each" using "item" does not
/// erase the outer one's item. A name no loop around it was using is the macro's own to read
/// afterwards, and a counter stays readable while the round number does not.
/// </summary>
internal readonly struct LoopScope : IDisposable
{
    private readonly VariableBag _values;
    private readonly List<(string Name, VariableMark Mark)> _marks;
    private readonly bool _keepLast;

    private LoopScope(VariableBag values, List<(string Name, VariableMark Mark)> marks, bool keepLast)
    {
        _values = values;
        _marks = marks;
        _keepLast = keepLast;
    }

    /// <summary>The round number every loop keeps for itself, and nobody else can read afterwards.</summary>
    public static LoopScope Round(VariableBag values) => Of(values, false, VariableStore.LoopIndexName);

    /// <summary>
    /// The names this loop writes, some of which the macro keeps after the loop is over: a counter
    /// stays readable, an item a loop introduced stays too, and one it borrowed is put back.
    /// </summary>
    public static LoopScope Named(VariableBag values, bool keepLast, params string[] names)
        => Of(values, keepLast, names);

    private static LoopScope Of(VariableBag values, bool keepLast, params string[] names)
    {
        var taken = names.Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var marks = new List<(string Name, VariableMark Mark)>();
        foreach (var name in taken)
        {
            // A loop's variable is its own: it borrows the name and never the value, so a
            // declaration that already answers to it keeps its value for the length of the block
            // and gets it back on the way out.
            marks.Add((name, values.Mark(name)));
        }

        return new LoopScope(values, marks, keepLast);
    }

    /// <summary>Writes one of the loop's own names. The name is the key, so a step inside the
    /// body writing the same name is writing the same value the next round writes.</summary>
    public void Set(string name, Value value) => _values.Set(name, value);

    public void Dispose()
    {
        foreach (var (name, mark) in _marks)
        {
            if (_keepLast && !mark.Existed)
            {
                continue;
            }

            _values.Restore(name, mark);
        }
    }
}
