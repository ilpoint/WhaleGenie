using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The words a step that cannot be undone is stopped and asked about with. The editor's own trial
/// run, the run window and a macro a trigger started all reach this through
/// <see cref="WhaleGenie.Core.Execution.ActionSemanticsCatalog.CannotBeUndone"/>, so which steps are
/// asked about is decided in the catalogue and how they are worded is decided here.
/// </summary>
internal static class IrreversiblePrompt
{
    /// <summary>The action's name in the reader's language, or the raw type when it means nothing.</summary>
    public static string Name(string type) => ActionCatalog.Find(type)?.LocalName ?? type;

    /// <summary>The question itself: which step, and that it cannot be put back.</summary>
    public static string Question(string type) => Strings.Format("Run.CannotBeUndone", Name(type));

    /// <summary>The same question over a whole run, where the step's own note is worth reading.</summary>
    public static string Question(string type, string note)
    {
        var text = Question(type);
        var comment = note.Trim();
        return comment.Length == 0 ? text : $"{text}\n{Strings.Format("Run.AskComment", comment)}";
    }
}
