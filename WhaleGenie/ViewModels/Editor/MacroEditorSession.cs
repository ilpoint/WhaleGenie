using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// What an open editor is working on, as opposed to what it is showing. The project it was opened
/// over, the macro it is editing, the macros it has lifted out of the list, and the pages it has
/// open — all of them outlive any one control on the window, and two of them were the reason a
/// second entry point (a check, or an assistant) could not open an editor at all.
/// </summary>
/// <remarks>
/// What stays on the window is what has no meaning once it closes: which page the pointer last
/// closed, whether a refresh is already running, where a dragged step would land.
/// </remarks>
internal sealed class MacroEditorSession
{
    public MacroEditorSession(MacroItem? editing, List<MacroItem> project, string assetFolder)
    {
        Editing = editing;
        Project = project;
        AssetFolder = assetFolder;
    }

    /// <summary>The macro being edited, kept apart from the rest of the project so a call to it can
    /// be told from a call to another macro.</summary>
    public MacroItem? Editing { get; }

    /// <summary>The whole project, which is what a call by name is looked up in.</summary>
    public List<MacroItem> Project { get; }

    /// <summary>Where a picture taken from the screen is put, so it travels with the package.</summary>
    public string AssetFolder { get; }

    /// <summary>
    /// Macros the editor made while it was open, which is what a lift leaves behind: they join the
    /// project when the macro that calls them is saved, because until then the call is not saved
    /// either.
    /// </summary>
    public List<MacroItem> Lifted { get; } = [];

    /// <summary>The pages open in the tab strip: the macro being edited and the sub-macros beside it.</summary>
    public ObservableCollection<MacroDocumentPageViewModel> Documents { get; } = [];

    /// <summary>
    /// What each editor page was called before an edit renamed it. The name is what a call step
    /// points at, so a rename has to be followed from the old name to the new one.
    /// </summary>
    public Dictionary<MacroEditorViewModel, string> EditorNames { get; } = [];

    /// <summary>A sub-macro that has been lifted and is waiting to become a page of its own.</summary>
    public PendingSubMacro? Pending { get; set; }

    /// <summary>The recording in progress, or the last one that has not been stopped yet.</summary>
    public RecordingSession Recording { get; } = new();

    /// <summary>
    /// Whether this session holds work the package does not have. The pages say whether a page is
    /// dirty, and the window says whether the project is; what belongs here is the one answer the
    /// two of them add up to, so closing, the recovery draft and the close prompt cannot disagree
    /// about whether anything would be lost.
    /// </summary>
    public bool IsDirty => _dirty || Documents.Any(page => page.Editor.IsDirty);

    /// <summary>Whether an edit that no page holds a record of has been made — a macro lifted, a
    /// document added or closed, a page's name changed.</summary>
    private bool _dirty;

    /// <summary>
    /// Notes that the session has changed something — a page edited, a macro lifted, a document
    /// added or closed. It is the only place this is written, which is why every edit goes through
    /// the window's own <c>NoteEdit</c> rather than touching the project's flag where it happens.
    /// </summary>
    public void NoteEdit()
    {
        _dirty = true;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The package now holds everything this session has: the pages are clean and so is this.
    /// </summary>
    public void Saved() => _dirty = false;

    /// <summary>Raised once per edit, for whoever publishes "there is unsaved work" upwards.</summary>
    public event EventHandler? DirtyChanged;
}

/// <summary>A lifted sub-macro together with the page that is about to show it.</summary>
internal sealed record PendingSubMacro(MacroEditorViewModel Editor, MacroDocumentPageViewModel Page);
