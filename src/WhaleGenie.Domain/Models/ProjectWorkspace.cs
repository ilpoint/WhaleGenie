using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Storage;

namespace WhaleGenie.Models;

/// <summary>
/// The project as a document: the macros, their trigger entries, the shared variables and the file
/// they were last written to. Everything that changes the document goes through here, so the dirty
/// flag, the revision and the change event cannot be updated by one caller and forgotten by another.
/// </summary>
/// <remarks>
/// This is deliberately not a view model. It holds no search text, no selected view and no window
/// title; those belong to <c>MainViewModel</c>, which keeps one of these and forwards it to the
/// window. The split is what lets the MCP tools talk to the document instead of to the window: a
/// draft is applied to this object, and the window hears about it through <see cref="Changed"/>.
/// </remarks>
public sealed partial class ProjectWorkspace : ObservableObject
{
    private readonly string _appVersion;
    private string? _currentPath;
    private bool _isDirty;
    private int _revision;

    public ProjectWorkspace(PersistedVariables? persisted = null, VariableSession? variables = null,
        string appVersion = "")
    {
        Persisted = persisted ?? new PersistedVariables();
        Variables = variables ?? new VariableSession();
        _appVersion = appVersion;

        // A trigger entry is edited in the trigger panel, not through a macro. The entries are the
        // project's only triggers, so a change here only has to re-say what the cards show; the
        // project counts as changed because the entries are written into the package.
        Triggers.Changed += (_, _) =>
        {
            Triggers.Project([.. Macros]);
            MarkDirty();
        };
    }

    /// <summary>The values kept on this machine for the project's global declarations.</summary>
    public PersistedVariables Persisted { get; }

    /// <summary>The table every run of this project shares, and the one the editor reads a declared
    /// value through.</summary>
    public VariableSession Variables { get; }

    /// <summary>Every macro in the project, in the order the list shows them.</summary>
    public ObservableCollection<MacroItem> Macros { get; } = [];

    /// <summary>Every trigger entry in the project, including the one the list is filtered by.</summary>
    public ProjectTriggers Triggers { get; } = new();

    /// <summary>Package file the macros are saved to, or <c>null</c> before the first save.</summary>
    public string? CurrentPath
    {
        get => _currentPath;
        set => SetProperty(ref _currentPath, value);
    }

    /// <summary>
    /// Whether anything in the document has been changed since the package was last written. A
    /// window that edits a macro sets this directly; every operation here sets it through
    /// <see cref="MarkDirty"/>.
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    /// <summary>
    /// Which version of the document this is. A draft remembers the number it was taken from;
    /// applying it after the number moved would overwrite work done in between.
    /// </summary>
    public int Revision
    {
        get => _revision;
        private set => SetProperty(ref _revision, value);
    }

    /// <summary>
    /// Raised for every change to the work this holds, where <see cref="IsDirty"/> only says
    /// whether there is any of it: a snapshot keeping up with the work has to hear about the
    /// changes that arrive while it is already unsaved, and the dirty flag does not move for those.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Adds a macro created in the macro editor and assigns it a display name. The entries the
    /// editor made for it come in the same write, so the project never holds a macro whose triggers
    /// have not arrived yet.
    /// </summary>
    public void AddMacro(MacroItem macro, IReadOnlyList<TriggerEntry>? entries = null)
    {
        if (string.IsNullOrWhiteSpace(macro.Name))
        {
            macro.Name = MacroNames.NextDefault(Macros.Select(item => item.Name));
        }
        else if (Macros.FirstOrDefault(item => string.Equals(item.Name.Trim(), macro.Name.Trim(),
                     StringComparison.OrdinalIgnoreCase)) is not null)
        {
            // Two macros cannot share a name: a macro is reached by its name, so the second one
            // would be the one a call never finds. Neither is a mistake — importing a package that
            // holds a macro this project already has is an ordinary thing to do — so the one
            // arriving now keeps its steps under a name of its own, dated the day it arrived.
            macro.Name = MacroNames.Free(macro.Name, Macros.Select(item => item.Name), DateTime.Now);
        }

        // A macro without a trigger is a real state: it is listed in "all" and runs when something
        // calls it. Inventing an empty key entry here made every new macro look like a key macro
        // whose key had merely not been chosen yet.
        Triggers.BeginBatch();
        try
        {
            Macros.Add(macro);
            if (entries is not null)
            {
                Triggers.ReplaceMacro(macro.Name, entries);
            }
        }
        finally
        {
            Triggers.EndBatch();
        }

        Triggers.Project([macro]);
        MarkDirty();
    }

    /// <summary>
    /// Swaps an edited macro back into the list, keeping its on/off state. Answers whether the macro
    /// was still there: a draft applied while an editor is open replaces the list's instances, and
    /// the editor's save must be told rather than quietly written nowhere.
    /// </summary>
    /// <param name="entries">
    /// The trigger entries the editor was showing, or null to leave the project's own entries alone.
    /// </param>
    public bool ReplaceMacro(MacroItem original, MacroItem edited,
        IReadOnlyList<TriggerEntry>? entries = null)
    {
        var index = Macros.IndexOf(original);
        if (index < 0)
        {
            return false;
        }

        edited.IsEnabled = original.IsEnabled;
        Triggers.BeginBatch();
        try
        {
            if (!string.Equals(original.Name, edited.Name, StringComparison.Ordinal))
            {
                Triggers.RenameMacro(original.Name, edited.Name);
            }

            Macros[index] = edited;
            if (entries is not null)
            {
                Triggers.ReplaceMacro(edited.Name, entries);
            }
        }
        finally
        {
            Triggers.EndBatch();
        }

        Triggers.Project([edited]);
        MarkDirty();
        return true;
    }

    /// <summary>Removes a macro from the list.</summary>
    public void RemoveMacro(MacroItem macro)
    {
        Macros.Remove(macro);
        Triggers.RemoveMacro(macro.Name);
        MarkDirty();
    }

    /// <summary>Removes every macro, used by "close all".</summary>
    public void ClearMacros()
    {
        Macros.Clear();
        Triggers.Entries.Clear();
        Triggers.InterfaceLayouts.Clear();
        Triggers.ResetMenu();
        MarkDirty();
    }

    /// <summary>Arms or disarms a macro; the macro list shows the state as a coloured dot.</summary>
    public void ToggleMacro(MacroItem macro)
    {
        macro.IsEnabled = !macro.IsEnabled;
        MarkDirty();
    }

    /// <summary>
    /// What the shared variables hold, one line each, so that a change made while they were open
    /// can be told from no change at all.
    /// </summary>
    public IReadOnlyList<string> Globals()
        => [.. Variables.Globals.Select(variable =>
            $"{variable.Name}\u001f{variable.Type}\u001f{variable.DefaultValue}\u001f{variable.Description}")];

    /// <summary>Whether the shared variables have moved on since <paramref name="before"/> was taken.</summary>
    public bool GlobalsChangedSince(IReadOnlyList<string> before)
        => !before.SequenceEqual(Globals());

    /// <summary>Notes that the shared variables themselves were changed.</summary>
    public void MarkGlobalsChanged() => MarkDirty();

    public void MarkMenuChanged()
    {
        Triggers.NotifyChanged();
        MarkDirty();
    }

    /// <summary>Writes the macro list and the shared variables to a package file.</summary>
    public void SavePackage(string path)
    {
        // The pictures are in the folder of the project as it stands now, which is not the folder of
        // the file being written when this is a save-as: without saying so, a package written
        // somewhere else silently loses the pictures it refers to.
        MacroPackage.Save(path, [.. Macros], Triggers, [.. Variables.Globals], _appVersion,
            ImageAssets.FolderFor(CurrentPath));
        CurrentPath = path;
        IsDirty = false;
    }

    /// <summary>Replaces everything with the contents of a package file.</summary>
    public void OpenPackage(string path)
    {
        var contents = MacroPackage.Load(path);

        Macros.Clear();
        foreach (var macro in contents.Macros)
        {
            Macros.Add(macro);
        }
        Triggers.ReplaceAll(contents.Triggers);
        Triggers.Project([.. Macros]);
        LoadSharedVariables(contents.Globals, replace: true, Persisted);

        CurrentPath = path;
        IsDirty = false;
        Revision++;
    }

    /// <summary>
    /// Puts a package's shared variables into the catalog. Replace takes the current list away
    /// first, which is what opening a project means; add keeps what is there and only brings in the
    /// names the project does not have yet, which is what importing a package means.
    /// </summary>
    private void LoadSharedVariables(IReadOnlyList<VariableDefinition> globals,
        bool replace, PersistedVariables persisted)
    {
        if (replace)
        {
            Variables.ClearGlobals();
        }

        foreach (var global in globals)
        {
            if (!replace && Variables.IsGlobal(global.Name))
            {
                continue;
            }

            Variables.Adopt(global);
        }

        // The values a macro wrote last time live outside the package. They are put back once the
        // declarations are known, which is what makes a reopened project start with them; doing it
        // here rather than at each caller is what keeps a path from forgetting it.
        ApplyPersisted(persisted);
    }

    /// <summary>
    /// Puts the work a stopped run left behind back into the list. It counts as unsaved on
    /// purpose: a snapshot is what a program that stopped without notice was holding, so the list
    /// has to keep asking to write it out rather than treating it as something already on disk.
    /// </summary>
    public void RestoreFrom(RecoveryContents contents)
    {
        Macros.Clear();
        foreach (var macro in contents.Macros)
        {
            Macros.Add(macro);
        }
        Triggers.ReplaceAll(contents.Triggers);
        Triggers.Project([.. Macros]);

        LoadSharedVariables(contents.Globals, replace: true, Persisted);

        CurrentPath = contents.PackagePath;
        MarkDirty();
    }

    /// <summary>
    /// Replaces the project with a draft the MCP tools have been editing. The caller has already
    /// checked the revision; this is only the write.
    /// </summary>
    public void ApplyDraft(IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IReadOnlyList<VariableDefinition> globals)
    {
        Macros.Clear();
        foreach (var macro in macros)
        {
            Macros.Add(macro);
        }
        Triggers.ReplaceAll(triggers);
        Triggers.Project([.. Macros]);

        Variables.ClearGlobals();
        foreach (var global in globals)
        {
            Variables.Adopt(global);
        }
        ApplyPersisted(Persisted);

        MarkDirty();
    }

    /// <summary>
    /// Puts back the values kept for this project's global declarations. A declaration whose value
    /// is not kept is left at the value it declares, and its checkbox says so.
    /// </summary>
    private void ApplyPersisted(PersistedVariables persisted)
    {
        foreach (var (name, value, kept) in persisted.ApplyTo(Variables.Globals))
        {
            if (Variables.FindGlobal(name) is not { } definition)
            {
                continue;
            }

            definition.PersistValue = kept;
            if (kept)
            {
                Variables.Values.Set(name, value);
            }
        }
    }

    /// <summary>Notes that the document has moved on.</summary>
    private void MarkDirty()
    {
        IsDirty = true;
        Revision++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
