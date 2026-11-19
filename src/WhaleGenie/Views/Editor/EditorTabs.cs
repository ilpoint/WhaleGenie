using System;
using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The tab strip's two questions: which page stands for a macro, and which page is in front once one
/// of them is closed.
/// </summary>
/// <remarks>
/// Closing a tab is not one event. The close button sits on the tab, so pressing it selects that tab
/// before the click arrives — by the time the page goes away, the front is already the one leaving.
/// What was in front has to be noted on the press, which is why this is state and not a function of
/// the pages alone.
/// </remarks>
internal sealed class EditorTabs(EditorDocumentPages pages)
{
    private MacroDocumentPageViewModel? _frontBeforePress;

    /// <summary>
    /// Notes which page was in front when a close button went down: the only moment the answer is
    /// still the page the person was looking at.
    /// </summary>
    public void NotePress(MacroDocumentPageViewModel? front) => _frontBeforePress = front;

    /// <summary>
    /// The page to put in front once <paramref name="closed"/> leaves the strip. It is the one that
    /// was in front before the press — closing a tab behind the one being read must not move the
    /// reader — and the root when that was the page going away.
    /// </summary>
    /// <param name="closed">The page whose tab the click is closing.</param>
    /// <param name="front">The page in front now, for a close that arrived without a press.</param>
    public MacroDocumentPageViewModel AfterClose(MacroDocumentPageViewModel closed,
        MacroDocumentPageViewModel? front)
    {
        var before = _frontBeforePress ?? front;
        _frontBeforePress = null;
        return before is null || ReferenceEquals(before, closed) ? pages[0] : before;
    }

    /// <summary>The page holding this editor, or null when the pages no longer hold it.</summary>
    public MacroDocumentPageViewModel? PageOf(MacroEditorViewModel editor)
        => pages.FirstOrDefault(page => ReferenceEquals(page.Editor, editor));

    /// <summary>
    /// The page that stands for a macro the dock names: the one it was opened from, else the page
    /// written under that name. A page the tab strip has closed is still one of the editor's
    /// documents, so this finds it and the strip can show it again.
    /// </summary>
    public MacroDocumentPageViewModel? Find(MacroItem macro)
        => pages.FirstOrDefault(page => ReferenceEquals(page.Source, macro)
            || (!page.IsCurrent
                && string.Equals(page.Editor.Name, macro.Name, StringComparison.OrdinalIgnoreCase)));
}
