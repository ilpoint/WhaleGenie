using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The documents one editor has open: the macro being edited, the sub-macros it holds and the ones
/// copied into it. It reads as a list because that is what the tab strip, the palette and the save
/// path need; what it adds is the one rule a page cannot answer alone — every page and every project
/// macro has a name no other page may take, and every step of every page has an id of its own.
/// </summary>
/// <remarks>
/// The window supplies the wiring each child editor needs and keeps the dialogs and the selection;
/// this class only builds and holds the pages. That is why it can be checked without a window.
/// </remarks>
internal sealed class EditorDocumentPages(
    MacroEditorSession session,
    MacroEditorViewModel root,
    VariableSession variables,
    IDeviceLayer? devices,
    Action<MacroEditorViewModel> watch) : IReadOnlyList<MacroDocumentPageViewModel>
{
    private readonly List<MacroDocumentPageViewModel> _pages = [];

    public int Count => _pages.Count;

    public MacroDocumentPageViewModel this[int index] => _pages[index];

    /// <summary>
    /// Opens the pages the session was loaded with: the root macro first, then its sub-macros in
    /// the order they were written, each followed by the ones written inside it.
    /// </summary>
    public MacroDocumentPageViewModel Build()
    {
        _pages.Clear();
        session.Documents.Clear();
        var current = new MacroDocumentPageViewModel(root, isCurrent: true);
        AddPage(current);

        var children = root.SubMacros.ToList();
        root.SubMacros.Clear();
        AddChildren(children);
        root.Documents = session.Documents;
        SettleIds();
        return current;
    }

    /// <summary>A new sub-macro, named so no other document or project macro answers to it.</summary>
    public MacroDocumentPageViewModel AddBlank()
    {
        var editor = NewEditor();
        editor.Name = UniqueName(Strings.Get("Editor.NewSubMacroName"));
        watch(editor);
        var page = AddChild(editor, null);
        SettleIds();
        return page;
    }

    /// <summary>An existing macro brought in as a sub-macro; it starts unsaved.</summary>
    public MacroDocumentPageViewModel AddExisting(MacroItem macro)
    {
        var editor = NewEditor();
        editor.LoadFrom(macro, []);
        editor.SubMacros.Clear();
        editor.IsDirty = true;
        watch(editor);
        var page = AddChild(editor, macro);
        SettleIds();
        return page;
    }

    /// <summary>
    /// Puts a page prepared elsewhere back into the editor: a sub-macro dragged in and then called
    /// has its page built by the drag, and it joins the tab strip only once a step actually calls it.
    /// </summary>
    public void Add(MacroDocumentPageViewModel page)
    {
        AddPage(page);
        SettleIds();
    }

    /// <summary>
    /// Closes a page's tab without letting go of what it holds: the sub-macro is still part of the
    /// macro being edited and opens again from the dock, so only the strip forgets it.
    /// </summary>
    public void Close(MacroDocumentPageViewModel page) => session.Documents.Remove(page);

    /// <summary>Takes a page out of the tab strip and out of the editor for good.</summary>
    public void Forget(MacroDocumentPageViewModel page)
    {
        session.Documents.Remove(page);
        _pages.Remove(page);
    }

    /// <summary>
    /// The document as one macro: the root page built with every other page hanging under it as a
    /// sub-macro, in the order the tab strip holds them. A save hands this over and the crash draft
    /// keeps it, so both have to read the document the same way.
    /// </summary>
    public MacroItem Snapshot()
    {
        root.SubMacros.Clear();
        foreach (var page in _pages.Where(page => !page.IsCurrent))
        {
            root.SubMacros.Add(page.Editor.BuildMacro());
        }

        return root.BuildMacro();
    }

    /// <summary>
    /// A name no page and no project macro has. A second copy of "login" is "login (2)": the number
    /// says "another one of these", which is what a copied sub-macro is.
    /// </summary>
    public string UniqueName(string basis)
        => EditorDocumentNames.Unique(basis,
            _pages.Select(page => page.Editor.Name)
                .Concat(session.Project.Select(macro => macro.Name)));

    public IEnumerator<MacroDocumentPageViewModel> GetEnumerator() => _pages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private MacroEditorViewModel NewEditor()
    {
        var editor = new MacroEditorViewModel
        {
            IsSubMacro = true,
            Persisted = root.Persisted,
            Variables = variables,
        };
        editor.Documents = session.Documents;
        if (devices is { } layer)
        {
            editor.Windows = layer.Windows;
        }

        return editor;
    }

    private void AddChildren(IEnumerable<MacroItem> children)
    {
        foreach (var child in children)
        {
            // Taken before the editor loads: the editor clears the list it was handed, and the
            // nested pages are the ones the loop has to add after this child.
            var nested = child.SubMacros.ToList();
            var editor = NewEditor();
            editor.LoadFrom(child, []);
            editor.SubMacros.Clear();
            watch(editor);
            AddChild(editor, child);
            AddChildren(nested);
        }
    }

    private MacroDocumentPageViewModel AddChild(MacroEditorViewModel editor, MacroItem? source)
    {
        var page = new MacroDocumentPageViewModel(editor, isCurrent: false, source);
        AddPage(page);
        return page;
    }

    private void AddPage(MacroDocumentPageViewModel page)
    {
        session.Documents.Add(page);
        _pages.Add(page);
    }

    /// <summary>
    /// Gives every step of every open document an id of its own, and says on the page it happened
    /// to when one had to move: a submacro brought in from elsewhere arrives with the ids it was
    /// written with, and the caller it was dropped into may already be using them.
    /// </summary>
    private void SettleIds()
    {
        var renamed = StepIds.SettleAcross(_pages.Select(page => page.Editor.Steps));
        if (renamed.Count > 0 && _pages.Count > 0)
        {
            _pages[^1].Editor.ShowStepIdsRepaired(renamed);
        }
    }
}
