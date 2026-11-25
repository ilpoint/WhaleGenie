using System.Collections.Generic;
using System.Threading.Tasks;

namespace WhaleGenie.Editor;

/// <summary>
/// Asks the page what to do with open macro documents. The caller gets the person's answer when a
/// page is there to ask; a null answer means there is no page, and the window keeps its own native
/// question as the fallback.
/// </summary>
public interface IEditorLeavePrompt
{
    /// <summary>
    /// Shows the page's save/discard/cancel question for these documents. True means the documents
    /// were saved or discarded (leave), false means the person cancelled; null means no page.
    /// </summary>
    Task<bool?> AskAsync(IReadOnlyList<string> documents);
}
