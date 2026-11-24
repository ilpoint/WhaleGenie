using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Models;

namespace WhaleGenie.Editing;

/// <summary>
/// Making, copying and removing the callable children of the macro being edited. A child command
/// is a change to the macro that owns the children, so it is answered by that macro's session even
/// when the document on screen is one of the children.
/// </summary>
internal sealed partial class HeadlessEditorSession
{
    /// <summary>
    /// The session of the macro that holds the children. The action library the page offers calls
    /// from is built from the root macro, so a child made inside another child would be a name
    /// nothing could reach.
    /// </summary>
    internal HeadlessEditorSession RootDocument => _owner is null ? this : _owner.RootDocument;

    /// <summary>An empty child, named so no other macro already answers to that name.</summary>
    public EditorSnapshot CreateSubMacro(string? name)
    {
        if (_owner is not null)
        {
            return RootDocument.CreateSubMacro(name);
        }

        _model.SubMacros.Add(SubMacroEditor.NewSubMacro(TakenMacroNames(), name));
        _model.IsDirty = true;
        RefreshCallables();
        return Snapshot();
    }

    /// <summary>
    /// Copies a child or a project macro into this macro and leaves a call to it at the end of the
    /// root steps, the way adding a public macro did: the copy is only reachable once something
    /// calls it.
    /// </summary>
    public EditorSnapshot CopySubMacro(string from, string? name)
    {
        if (_owner is not null)
        {
            return RootDocument.CopySubMacro(from, name);
        }

        var source = FindCallable(from)
            ?? throw new KeyNotFoundException("the macro has no macro named " + from);
        var copy = SubMacroEditor.Copy(source, TakenMacroNames(), name);
        _model.SubMacros.Add(copy);
        _model.Steps.Add(SubMacroEditor.CallStep(copy));
        _model.SettleStepIds();
        _model.IsDirty = true;
        RefreshCallables();
        return Snapshot();
    }

    /// <summary>
    /// Removes a child the macro no longer calls. A child a step still calls stays: the call would
    /// be left pointing at a name that has gone, and nobody asked for that.
    /// </summary>
    public EditorSnapshot DeleteSubMacro(string name)
    {
        if (_owner is not null)
        {
            return RootDocument.DeleteSubMacro(name);
        }

        if (!HasSubMacro(name))
        {
            throw new KeyNotFoundException("the macro has no sub-macro named " + name);
        }

        var owner = _model.BuildMacro();
        if (!SubMacroEditor.Delete(owner, name))
        {
            throw new InvalidOperationException("the macro still calls the sub-macro " + name);
        }

        _model.SubMacros.Clear();
        _model.SubMacros.AddRange(owner.SubMacros);
        _model.IsDirty = true;
        RefreshCallables();
        return Snapshot();
    }

    /// <summary>The names a new child may not take: this document's children and the project macros.</summary>
    private HashSet<string> TakenMacroNames()
        => _model.SubMacros.Select(macro => macro.Name)
            .Concat(_workspace.Macros.Select(macro => macro.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>A copy source: a child of this document first, then a macro of the project.</summary>
    private MacroItem? FindCallable(string name)
        => _model.SubMacros.FirstOrDefault(macro => SubMacroEditor.Named(macro, name))
            ?? _workspace.Macros.FirstOrDefault(macro => SubMacroEditor.Named(macro, name));

    private bool HasSubMacro(string name)
        => _model.SubMacros.Any(macro => SubMacroEditor.Named(macro, name));
}
