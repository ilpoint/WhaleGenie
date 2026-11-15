using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The main window's own state: what is typed in the search box, which view is open, whether the
/// system is armed and the MCP flyout. The document itself lives in <see cref="Workspace"/>; this
/// class forwards it so the window and the existing bindings keep one object to talk to.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly Func<MainViewModel, McpServerViewModel>? _makeMcp;
    private McpServerViewModel? _mcp;

    public MainViewModel(Func<MainViewModel, McpServerViewModel>? mcp = null,
        PersistedVariables? persisted = null, VariableSession? variables = null,
        ProjectWorkspace? workspace = null)
    {
        _makeMcp = mcp;

        // The document is made first: every other member here reads it, and a window that opens a
        // package during construction should find the same table the runs will use. A caller that
        // already holds the document — the MCP container does — hands it in rather than letting a
        // second one be made beside it.
        Workspace = workspace ?? new ProjectWorkspace(persisted, variables, AppVersion.Text);
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        Workspace.Changed += (_, _) => RefreshVisibleMacros();

        SelectedView = LocalSettings.LoadView();

        // 启动时是否已经在听触发，由设置决定；默认关着，和按热键启用之前一样。
        IsRunning = LocalSettings.LoadSystemAtStart();
        Workspace.Macros.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMacros));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            RefreshGroups();
            RefreshVisibleMacros();
        };
        Localization.Strings.Current.LanguageChanged += () =>
        {
            RefreshLanguage();
            Mcp.RefreshLanguage();
            foreach (var macro in Macros)
            {
                macro.RefreshLanguage();
            }

            // The filter's own two words ("all groups", "ungrouped") are drawn from the item, and a
            // combo box keeps the text it was given, so the list is rebuilt for the new language.
            RefreshGroups();
        };
        RefreshGroups();
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

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => RefreshVisibleMacros();

    /// <summary>
    /// One entry of the group filter beside the search box: "all", "ungrouped", or a group name.
    /// Groups are the second layer beside the fixed views — the view says how a macro starts, the
    /// group says which part of someone's work it belongs to.
    /// </summary>
    public sealed record MacroGroupChoice(string? Name)
    {
        public override string ToString() => Name switch
        {
            null => Strings.Get("Main.Ungrouped"),
            { Length: 0 } => Strings.Get("Main.GroupAll"),
            _ => Name,
        };
    }

    public ObservableCollection<MacroGroupChoice> Groups { get; } = [];

    [ObservableProperty]
    public partial MacroGroupChoice? SelectedGroup { get; set; }

    partial void OnSelectedGroupChanged(MacroGroupChoice? value) => RefreshVisibleMacros();

    /// <summary>
    /// Rebuilds the filter from the groups the macros actually carry, keeping the chosen one by
    /// name. A group disappears from the list when its last macro leaves it.
    /// </summary>
    private void RefreshGroups()
    {
        var wanted = SelectedGroup?.Name ?? string.Empty;
        var names = Macros
            .Select(macro => macro.Group.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        Groups.Clear();
        Groups.Add(new MacroGroupChoice(string.Empty));
        foreach (var name in names)
        {
            Groups.Add(new MacroGroupChoice(name));
        }
        Groups.Add(new MacroGroupChoice(null));

        var keeping = Groups.FirstOrDefault(choice =>
            string.Equals(choice.Name, wanted, StringComparison.OrdinalIgnoreCase)) ?? Groups[0];
        if (!ReferenceEquals(SelectedGroup, keeping))
        {
            SelectedGroup = keeping;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllView))]
    [NotifyPropertyChangedFor(nameof(IsKeysView))]
    [NotifyPropertyChangedFor(nameof(IsInterfaceView))]
    [NotifyPropertyChangedFor(nameof(IsHotstringView))]
    [NotifyPropertyChangedFor(nameof(IsMenuView))]
    [NotifyPropertyChangedFor(nameof(IsWheelView))]
    [NotifyPropertyChangedFor(nameof(IsEventsView))]
    public partial MacroView SelectedView { get; set; } = MacroView.All;

    public bool IsAllView => SelectedView == MacroView.All;
    public bool IsKeysView => SelectedView == MacroView.Keys;
    public bool IsInterfaceView => SelectedView == MacroView.Interface;
    public bool IsHotstringView => SelectedView == MacroView.Hotstring;
    public bool IsMenuView => SelectedView == MacroView.Menu;
    public bool IsWheelView => SelectedView == MacroView.Wheel;
    public bool IsEventsView => SelectedView == MacroView.Events;

    partial void OnSelectedViewChanged(MacroView value)
    {
        LocalSettings.StoreView(value);
        RefreshVisibleMacros();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemLabel))]
    [NotifyPropertyChangedFor(nameof(SystemHint))]
    public partial bool IsRunning { get; set; }

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

    /// <summary>
    /// What the list actually shows: every macro, or the ones whose name matches what is in the
    /// search box. The box narrows the view rather than the project, so nothing about the macros
    /// themselves depends on what is typed there — closing the box puts them all back.
    /// </summary>
    public ObservableCollection<MacroItem> VisibleMacros { get; } = [];

    public bool HasMacros => Macros.Count > 0;

    /// <summary>True when there are macros but the search left none of them on screen.</summary>
    public bool HasNoMatch => Macros.Count > 0 && VisibleMacros.Count == 0
        && SearchText.Trim().Length > 0;

    /// <summary>
    /// True when the view that is open holds nothing and no search is narrowing it — which is a
    /// different sentence from "nothing matches what you typed", and the common one now that the
    /// list has a view for every kind of trigger.
    /// </summary>
    public bool IsEmptyView => Macros.Count > 0 && VisibleMacros.Count == 0
        && SearchText.Trim().Length == 0;

    /// <summary>
    /// Puts the macros the search box lets through into the list the window shows. Enabled ones come
    /// first when that is asked for; the sort is stable, so the order a project was made in survives
    /// inside each of the two groups.
    /// </summary>
    public void RefreshVisibleMacros()
    {
        var wanted = SearchText.Trim();
        var group = SelectedGroup?.Name;
        var keeping = Macros.Where(macro =>
            Triggers.InView(macro.Name, SelectedView)
            && (group switch
            {
                null => macro.Group.Trim().Length == 0,
                { Length: 0 } => true,
                _ => string.Equals(macro.Group.Trim(), group, StringComparison.OrdinalIgnoreCase),
            })
            && (wanted.Length == 0
                || macro.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)));

        if (LocalSettings.LoadEnabledFirst())
        {
            keeping = keeping.OrderByDescending(macro => macro.IsEnabled);
        }

        VisibleMacros.Clear();
        foreach (var macro in keeping.ToList())
        {
            macro.RefreshTriggers();
            VisibleMacros.Add(macro);
        }

        OnPropertyChanged(nameof(HasNoMatch));
        OnPropertyChanged(nameof(IsEmptyView));
    }

    /// <summary>Adds a macro created in the macro editor and assigns it a display name.</summary>
    public void AddMacro(MacroItem macro) => Workspace.AddMacro(macro);

    /// <summary>
    /// Swaps an edited macro back into the list, keeping its on/off state. False means the macro was
    /// no longer in the list — the project changed while the editor was open.
    /// </summary>
    public bool ReplaceMacro(MacroItem original, MacroItem edited)
        => Workspace.ReplaceMacro(original, edited);

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

    [RelayCommand]
    private void SelectView(string? view)
    {
        if (Enum.TryParse<MacroView>(view, true, out var selected))
        {
            SelectedView = selected;
        }
    }

    /// <summary>
    /// Replaces the project with a draft the MCP tools have been editing. The caller has already
    /// checked the revision; this is only the write.
    /// </summary>
    public void ApplyDraft(IReadOnlyList<MacroItem> macros, IReadOnlyList<VariableDefinition> globals)
        => Workspace.ApplyDraft(macros, globals);

    public void ApplyDraft(IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IReadOnlyList<VariableDefinition> globals)
        => Workspace.ApplyDraft(macros, triggers, globals);
}
