namespace WhaleGenie.Core.Expressions;

/// <summary>
/// Where an expression says what it had to leave out: a row that was not a record, a field that was
/// missing or not a number. The answer is handed in rather than written somewhere nobody chose, so a
/// run can carry it to its log and a preview can drop it on purpose.
/// </summary>
public interface IExpressionNotes
{
    /// <summary>One thing an expression skipped, said as the log key and its arguments.</summary>
    void Note(string key, IReadOnlyList<object> arguments);
}
