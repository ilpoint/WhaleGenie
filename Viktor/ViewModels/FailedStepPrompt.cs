using Viktor.Localization;
using Viktor.Models;

namespace Viktor.ViewModels;

/// <summary>
/// The words a failure rule of "ask me" is put to the user with. The debugger asks inside its own
/// window and the trigger service asks in a box of its own, so the question is worded in one place
/// and both tell the same story about which step went wrong and why.
/// </summary>
internal static class FailedStepPrompt
{
    /// <summary>The action's name in the reader's language, or the raw type when it means nothing.</summary>
    public static string Name(string type) => ActionCatalog.Find(type)?.LocalName ?? type;

    /// <summary>
    /// The question itself. The reason is a message key that may want the detail filled in, so a
    /// failure with nothing to add is worded with an empty one rather than left half-written.
    /// The note the user wrote on the step gets a line of its own: it is often the only thing
    /// that says why the step was in the macro at all, which is exactly what somebody looking at
    /// a failure wants to know.
    /// </summary>
    public static string Question(string step, string reason, string detail, string comment)
    {
        var why = detail.Length == 0 ? Strings.Format(reason, string.Empty) : Strings.Format(reason, detail);
        var text = $"{Strings.Format("Run.Ask", Name(step))}\n{why}";

        var note = comment.Trim();
        return note.Length == 0 ? text : $"{text}\n{Strings.Format("Run.AskComment", note)}";
    }
}
