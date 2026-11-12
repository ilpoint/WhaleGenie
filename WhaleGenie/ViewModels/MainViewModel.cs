using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly Func<MainViewModel, McpServerViewModel>? _makeMcp;
    private McpServerViewModel? _mcp;

    /// <summary>The values kept on this machine for the project's global declarations.</summary>
    public PersistedVariables Persisted { get; }

    public MainViewModel(Func<MainViewModel, McpServerViewModel>? mcp = null,
        PersistedVariables? persisted = null)
    {
        _makeMcp = mcp;
        Persisted = persisted ?? new PersistedVariables();
        SelectedView = LocalSettings.LoadView();

        // 启动时是否已经在听触发，由设置决定；默认关着，和按热键启用之前一样。
        IsRunning = LocalSettings.LoadSystemAtStart();
        Macros.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMacros));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            RefreshGroups();
            RefreshVisibleMacros();
        };
        Triggers.Changed += (_, _) =>
        {
            Triggers.Attach([.. Macros]);
            MarkDirty();
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

    /// <summary>Every trigger entry in the project, including the one the list is filtered by.</summary>
    public ProjectTriggers Triggers { get; } = new();

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
    /// Whether anything in the list has been changed since the package was last written — macros
    /// added, edited, removed or armed or disarmed, and shared variables imported. The armed state
    /// counts because it is written into the package: a macro that was switched off here is off
    /// the next time the package is opened, so it is a change like any other.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    public partial bool IsDirty { get; set; }

    /// <summary>
    /// True when stopping the program would throw away something the user made. A project that was
    /// never saved and holds nothing is not one of those, so "close all" on a list that is only
    /// memory does not turn into a question at exit.
    /// </summary>
    public bool HasUnsavedChanges => IsDirty && (Macros.Count > 0 || CurrentPath is not null);

    /// <summary>Package file the macros are saved to, or <c>null</c> before the first save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentFolder))]
    public partial string? CurrentPath { get; set; }

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

    /// <summary>Macros shown in the list. Empty until the user adds one.</summary>
    public ObservableCollection<MacroItem> Macros { get; } = [];

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
    public void AddMacro(MacroItem macro)
    {
        if (string.IsNullOrWhiteSpace(macro.Name))
        {
            macro.Name = MacroNames.NextDefault(Macros.Select(item => item.Name));
        }
        else if (Macros.FirstOrDefault(item => string.Equals(item.Name.Trim(), macro.Name.Trim(),
                     StringComparison.OrdinalIgnoreCase)) is not null)
        {
            // Two macros cannot share a name: a macro is reached by its name, so the second one would
            // be the one a call never finds. Neither is a mistake — importing a package that holds a
            // macro this project already has is an ordinary thing to do — so the one arriving now
            // keeps its steps under a name of its own, dated the day it arrived.
            macro.Name = MacroNames.Free(macro.Name, Macros.Select(item => item.Name), DateTime.Now);
        }

        // A macro without a trigger is a real state: it is listed in "all" and runs when something
        // calls it. Inventing an empty key entry here made every new macro look like a key macro
        // whose key had merely not been chosen yet.
        Macros.Add(macro);
        Triggers.ReplaceMacro(macro.Name, macro.Triggers.Select(entry => entry.Copy()));
        MarkDirty();
    }

    /// <summary>Swaps an edited macro back into the list, keeping its on/off state.</summary>
    public void ReplaceMacro(MacroItem original, MacroItem edited)
    {
        var index = Macros.IndexOf(original);
        if (index < 0)
        {
            return;
        }

        edited.IsEnabled = original.IsEnabled;
        if (!string.Equals(original.Name, edited.Name, StringComparison.Ordinal))
        {
            Triggers.RenameMacro(original.Name, edited.Name);
        }

        Macros[index] = edited;
        Triggers.ReplaceMacro(edited.Name, edited.Triggers.Select(entry => entry.Copy()));
        MarkDirty();
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
    /// <remarks>
    /// The list is put back together afterwards because "enabled first" is a list setting: without
    /// this the dot would say one thing and the order another until something else redrew the list.
    /// </remarks>
    public void ToggleMacro(MacroItem macro)
    {
        macro.IsEnabled = !macro.IsEnabled;
        RefreshVisibleMacros();
        MarkDirty();
    }

    /// <summary>
    /// What the shared variables hold, one line each, so that a change made while they were open
    /// can be told from no change at all.
    /// </summary>
    /// <remarks>
    /// Nothing else watches them: the package carries the shared variables beside the macros, but
    /// they are not on the macro list, so nothing on that list moves when one is added, renamed or
    /// given another starting value.
    /// </remarks>
    public static IReadOnlyList<string> SharedVariables()
        => [.. VariableCatalog.Globals.Select(variable =>
            $"{variable.Name}\u001f{variable.Type}\u001f{variable.DefaultValue}\u001f{variable.Description}")];

    /// <summary>Whether the shared variables have moved on since <paramref name="before"/> was taken.</summary>
    public static bool SharedVariablesChangedSince(IReadOnlyList<string> before)
        => !before.SequenceEqual(SharedVariables());

    /// <summary>Notes that the shared variables themselves were changed.</summary>
    public void MarkSharedVariablesChanged() => MarkDirty();

    internal void MarkMenuChanged()
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
        Storage.MacroPackage.Save(path, [.. Macros], Triggers, [.. VariableCatalog.Globals], Version,
            Storage.ImageAssets.FolderFor(CurrentPath));
        CurrentPath = path;
        IsDirty = false;
    }

    /// <summary>Replaces everything with the contents of a package file.</summary>
    public void OpenPackage(string path)
    {
        var contents = Storage.MacroPackage.Load(path);

        Macros.Clear();
        foreach (var macro in contents.Macros)
        {
            Macros.Add(macro);
        }
        Triggers.ReplaceAll(contents.Triggers);
        Triggers.Attach([.. Macros]);

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
    private static void LoadSharedVariables(IReadOnlyList<VariableDefinition> globals,
        bool replace, PersistedVariables persisted)
    {
        if (replace)
        {
            VariableCatalog.Globals.Clear();
        }

        foreach (var global in globals)
        {
            if (!replace && VariableCatalog.IsGlobal(global.Name))
            {
                continue;
            }

            VariableCatalog.Globals.Add(global);
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
    public void RestoreFrom(Storage.RecoveryContents contents)
    {
        Macros.Clear();
        foreach (var macro in contents.Macros)
        {
            Macros.Add(macro);
        }
        Triggers.ReplaceAll(contents.Triggers);
        Triggers.Attach([.. Macros]);

        LoadSharedVariables(contents.Globals, replace: true, Persisted);

        CurrentPath = contents.PackagePath;
        MarkDirty();
    }

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
    /// Raised for every change to the work this holds, where <see cref="IsDirty"/> only says
    /// whether there is any of it: the snapshot keeping up with the work has to hear about the
    /// changes that arrive while it is already unsaved, and the dirty flag does not move for
    /// those.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Notes that the work has moved on.</summary>
    private void MarkDirty()
    {
        IsDirty = true;
        Revision++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Which version of the project the interface is holding. An MCP draft remembers the number
    /// it was taken from; applying it after the number moved would overwrite work done in between.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>
    /// Replaces the project with a draft the MCP tools have been editing. The caller has already
    /// checked the revision; this is only the write.
    /// </summary>
    public void ApplyDraft(IReadOnlyList<MacroItem> macros, IReadOnlyList<VariableDefinition> globals)
        => ApplyDraft(macros, ProjectTriggers.FromMacros(macros), globals);

    public void ApplyDraft(IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IReadOnlyList<VariableDefinition> globals)
    {
        Macros.Clear();
        foreach (var macro in macros)
        {
            Macros.Add(macro);
        }
        Triggers.ReplaceAll(triggers);
        Triggers.Attach([.. Macros]);

        VariableCatalog.Globals.Clear();
        foreach (var global in globals)
        {
            VariableCatalog.Globals.Add(global);
        }
        ApplyPersisted(Persisted);

        MarkDirty();
    }

    /// <summary>
    /// Puts back the values kept for this project's global declarations. A declaration whose value
    /// is not kept is left at the value it declares, and its checkbox says so.
    /// </summary>
    private static void ApplyPersisted(PersistedVariables persisted)
    {
        foreach (var (name, value, kept) in persisted.ApplyTo(VariableCatalog.Globals))
        {
            if (VariableCatalog.FindGlobal(name) is not { } definition)
            {
                continue;
            }

            definition.PersistValue = kept;
            if (kept)
            {
                WhaleGenie.Models.SharedVariables.Current.Set(name, value);
            }
        }
    }
}
