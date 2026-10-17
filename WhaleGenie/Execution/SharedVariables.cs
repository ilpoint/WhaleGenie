using WhaleGenie.Core.Variables;

namespace WhaleGenie.Execution;

/// <summary>
/// The one table of global values this program uses. Every run — a trigger, the editor's own run,
/// a trial in a dialog, and a macro called by any of them — is handed this table, so a value one
/// macro writes is what the next macro reads. Local values stay with the run that made them.
/// </summary>
/// <remarks>
/// The values live as long as the program and are not written into the project file: closing
/// WhaleGenie puts every global back to its declared starting value the next time it starts. A
/// value whose name is no longer declared is taken out when the next run seeds its table.
/// </remarks>
public static class SharedVariables
{
    /// <summary>The table every run in this program shares.</summary>
    public static VariableBag Current { get; } = new();
}
