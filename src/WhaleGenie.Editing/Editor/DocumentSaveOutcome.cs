using WhaleGenie.Contracts;

namespace WhaleGenie.Editing;

/// <summary>What one document's save came to, before the document set adds its id and name.</summary>
internal readonly record struct DocumentSaveOutcome(string Outcome, string? Problem)
{
    /// <summary>True when the project (or the parent document) took the draft.</summary>
    public bool Succeeded
        => Outcome is EditorSaveOutcomes.Saved or EditorSaveOutcomes.Unchanged;
}
