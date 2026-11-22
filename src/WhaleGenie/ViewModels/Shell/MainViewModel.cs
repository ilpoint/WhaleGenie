using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The project's windowless view: the document every page, trigger and MCP tool reads, plus the
/// system switch and the MCP server the shell and the native menu share. The macro list filters,
/// searches and sorts in the page now; this class only forwards the document.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly Func<MainViewModel, McpServerViewModel>? _makeMcp;
    private McpServerViewModel? _mcp;
    private readonly SystemState _system;

    public MainViewModel(Func<MainViewModel, McpServerViewModel>? mcp = null,
        PersistedVariables? persisted = null, VariableSession? variables = null,
        ProjectWorkspace? workspace = null, SystemState? system = null)
    {
        _makeMcp = mcp;
        _system = system ?? new SystemState();
        _system.Changed += OnSystemChanged;

        // The document is made first: every other member here reads it, and a window that opens a
        // package during construction should find the same table the runs will use. A caller that
        // already holds the document — the MCP container does — hands it in rather than letting a
        // second one be made beside it.
        Workspace = workspace ?? new ProjectWorkspace(persisted, variables, AppVersion.Text);
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        Workspace.Macros.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMacros));
            OnPropertyChanged(nameof(HasUnsavedChanges));
        };
        Localization.Strings.Current.LanguageChanged += () =>
        {
            RefreshLanguage();
            Mcp.RefreshLanguage();
            foreach (var macro in Macros)
            {
                macro.RefreshLanguage();
            }
        };
    }

    /// <summary>
    /// The project document: macros, triggers, shared variables, the package path, the dirty flag
    /// and the revision. The window shows it; the editor and the MCP tools change it.
    /// </summary>
    public ProjectWorkspace Workspace { get; }

    /// <summary>The values kept on this machine for the project's global declarations.</summary>
    public PersistedVariables Persisted => Workspace.Persisted;

    /// <summary>The table every run of this project shares.</summary>
    public VariableSession Variables => Workspace.Variables;

    /// <summary>Every macro in the project.</summary>
    public ObservableCollection<MacroItem> Macros => Workspace.Macros;

    /// <summary>Every trigger entry in the project.</summary>
    public ProjectTriggers Triggers => Workspace.Triggers;

    /// <summary>Whether the document has changed since it was last written.</summary>
    public bool IsDirty
    {
        get => Workspace.IsDirty;
        set => Workspace.IsDirty = value;
    }

    /// <summary>Package file the macros are saved to, or <c>null</c> before the first save.</summary>
    public string? CurrentPath
    {
        get => Workspace.CurrentPath;
        set => Workspace.CurrentPath = value;
    }

    /// <summary>Which version of the document the window is holding.</summary>
    public int Revision => Workspace.Revision;

    /// <summary>Raised for every change to the document, after the dirty flag and revision moved.</summary>
    public event EventHandler? Changed
    {
        add => Workspace.Changed += value;
        remove => Workspace.Changed -= value;
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ProjectWorkspace.IsDirty):
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                break;
            case nameof(ProjectWorkspace.CurrentPath):
                OnPropertyChanged(nameof(CurrentPath));
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(CurrentFolder));
                break;
            case nameof(ProjectWorkspace.Revision):
                OnPropertyChanged(nameof(Revision));
                break;
        }
    }

    /// <summary>
    /// The local MCP server switch and the address it hands to an MCP client. Built on first use:
    /// the server needs the project and the project shows the switch, so one of the two has to be
    /// lazy — and this is the cheaper side to make lazy.
    /// </summary>
    public McpServerViewModel Mcp => _mcp ??= _makeMcp?.Invoke(this) ?? new McpServerViewModel();

    /// <summary>
    /// The system switch the window draws. The value belongs to <see cref="SystemState"/>, so a page,
    /// a hotkey and this window all move the same switch rather than keeping a copy each.
    /// </summary>
    public bool IsRunning
    {
        get => _system.IsEnabled;
        set => _system.Set(value);
    }

    /// <summary>The one switch, handed to anything that needs to read or move it.</summary>
    public SystemState SystemSwitch => _system;

    private void OnSystemChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(SystemLabel));
        OnPropertyChanged(nameof(SystemHint));
    }

    /// <summary>
    /// Label of the system button, which names the state rather than the action: it reads
    /// "已启用" while the system is on, and pressing it turns the system off.
    /// </summary>
    public string SystemLabel => Localization.Strings.Get(IsRunning ? "Main.SystemEnabled" : "Main.SystemDisabled");

    /// <summary>
    /// Hint under the button. Which macros are running is already plain on their cards, so this
    /// only has to point at the key that switches the system.
    /// </summary>
    public string SystemHint
    {
        get
        {
            var hotkey = LocalSettings.LoadSystemHotkey();
            return hotkey.Length == 0
                ? Localization.Strings.Get(IsRunning
                    ? "Main.SystemDisableNoHotkey"
                    : "Main.SystemEnableNoHotkey")
                : Localization.Strings.Format(
                    IsRunning ? "Main.SystemDisableHint" : "Main.SystemEnableHint",
                    hotkey);
        }
    }

    public void RefreshHotkeyHints() => OnPropertyChanged(nameof(SystemHint));

    /// <summary>
    /// What the program is called in the corner of the window, and what a package says wrote it.
    /// Read out of the program itself rather than typed here, so the release tag stays the only
    /// place a version is written.
    /// </summary>
    public string Version { get; } = AppVersion.Text;

    /// <summary>
    /// True when stopping the program would throw away something the user made. A project that was
    /// never saved and holds nothing is not one of those, so "close all" on a list that is only
    /// memory does not turn into a question at exit.
    /// </summary>
    public bool HasUnsavedChanges => IsDirty && (Macros.Count > 0 || CurrentPath is not null);

    /// <summary>Title bar text, which names the open package once there is one.</summary>
    public string WindowTitle => CurrentPath is null
        ? BaseTitle
        : $"{BaseTitle} · {System.IO.Path.GetFileName(CurrentPath)}";

    /// <summary>
    /// The application name as the title bar shows it. The admin badge appears only when the
    /// process really is elevated: a badge that is always there says nothing, and it made the
    /// window claim rights the program did not have.
    /// </summary>
    private static string BaseTitle => ProcessRights.IsElevated
        ? Localization.Strings.Get("Main.Title") + Localization.Strings.Get("Main.AdminBadge")
        : Localization.Strings.Get("Main.Title");

    /// <summary>Folder holding the open package, used by "open location".</summary>
    public string? CurrentFolder => CurrentPath is null
        ? null
        : System.IO.Path.GetDirectoryName(CurrentPath);

    public bool HasMacros => Macros.Count > 0;

    /// <summary>Adds a macro created in the macro editor and assigns it a display name.</summary>
    public void AddMacro(MacroItem macro, IReadOnlyList<TriggerEntry>? entries = null)
        => Workspace.AddMacro(macro, entries);

    /// <summary>
    /// Swaps an edited macro back into the list, keeping its on/off state. False means the macro was
    /// no longer in the list — the project changed while the editor was open.
    /// </summary>
    public bool ReplaceMacro(MacroItem original, MacroItem edited,
        IReadOnlyList<TriggerEntry>? entries = null)
        => Workspace.ReplaceMacro(original, edited, entries);

    /// <summary>Removes a macro from the list.</summary>
    public void RemoveMacro(MacroItem macro) => Workspace.RemoveMacro(macro);

    /// <summary>Removes every macro, used by "close all".</summary>
    public void ClearMacros() => Workspace.ClearMacros();

    /// <summary>Arms or disarms a macro; the macro list shows the state as a coloured dot.</summary>
    public void ToggleMacro(MacroItem macro) => Workspace.ToggleMacro(macro);

    /// <summary>
    /// What the shared variables hold, one line each, so that a change made while they were open
    /// can be told from no change at all.
    /// </summary>
    public IReadOnlyList<string> Globals() => Workspace.Globals();

    /// <summary>Whether the shared variables have moved on since <paramref name="before"/> was taken.</summary>
    public bool GlobalsChangedSince(IReadOnlyList<string> before)
        => Workspace.GlobalsChangedSince(before);

    /// <summary>Notes that the shared variables themselves were changed.</summary>
    public void MarkGlobalsChanged() => Workspace.MarkGlobalsChanged();

    internal void MarkMenuChanged() => Workspace.MarkMenuChanged();

    /// <summary>Writes the macro list and the shared variables to a package file.</summary>
    public void SavePackage(string path) => Workspace.SavePackage(path);

    /// <summary>Replaces everything with the contents of a package file.</summary>
    public void OpenPackage(string path) => Workspace.OpenPackage(path);

    /// <summary>Puts a stopped run's work back into the list.</summary>
    public void RestoreFrom(Storage.RecoveryContents contents) => Workspace.RestoreFrom(contents);

    [RelayCommand]
    private void ToggleRun() => IsRunning = !IsRunning;

    /// <summary>
    /// Replaces the project with a draft the MCP tools have been editing. The caller has already
    /// checked the revision; this is only the write.
    /// </summary>
    public void ApplyDraft(IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IReadOnlyList<VariableDefinition> globals)
        => Workspace.ApplyDraft(macros, triggers, globals);
}
