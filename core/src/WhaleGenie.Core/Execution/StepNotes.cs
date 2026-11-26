using System.Collections.Generic;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The notes one step's expressions leave about what they skipped: a row that was not a record, a
/// field that was missing. The runner begins one before a step and takes what it collected when the
/// step is over, which is what makes a note land in that step's log rather than in the next one's.
/// </summary>
internal sealed class StepNotes : IExpressionNotes
{
    private List<(string Key, object[] Arguments)>? _current;

    /// <summary>Starts a fresh place for the step that is about to run.</summary>
    public void Begin() => _current = [];

    /// <summary>Takes what was collected since <see cref="Begin"/>, oldest first.</summary>
    public IReadOnlyList<(string Key, object[] Arguments)> Take()
    {
        var notes = _current ?? [];
        _current = null;
        return notes;
    }

    public void Note(string key, IReadOnlyList<object> arguments)
        => _current?.Add((key, [.. arguments]));
}
