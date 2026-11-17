using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WindowRuleModel = WhaleGenie.Core.Devices.WindowRule;

namespace WhaleGenie.ViewModels;

// Triggers and hotkeys: the entries a macro starts on, the interface toggle and the settings drawer.
public partial class MacroEditorViewModel
{
    public MacroEditorViewModel()
    {
        TriggerEntries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TriggerProblemSummary));
            OnPropertyChanged(nameof(HasTriggerProblem));
        };

        // No entry is created here. A macro without a trigger is an ordinary state now, and what
        // kind of entry a new macro starts with is decided by where the person pressed "add":
        // see PrepareNewTrigger.
        RebuildHotstringEndKeys();
        RefreshPaletteState();
        RefreshVariableCenter();
    }

    /// <summary>
    /// Gives a brand-new macro the entry its list view stands for: a key entry with nothing bound
    /// yet on the key view, and the matching entry on the window and string views. "All", "events"
    /// and the wheel start with nothing — the first two are not one kind, and a wheel position
    /// needs the macro's name, so it is placed once the macro is saved.
    /// </summary>
    public void PrepareNewTrigger(MacroView view)
    {
        var kind = view switch
        {
            MacroView.Keys => TriggerKind.Key,
            MacroView.Interface => TriggerKind.Interface,
            MacroView.Hotstring => TriggerKind.Hotstring,
            _ => (TriggerKind?)null,
        };

        if (kind is { } wanted)
        {
            AddEntry(wanted);
        }
    }

    /// <summary>Adds an entry and shows it, without counting as an edit the person made.</summary>
    private TriggerEntry AddEntry(TriggerKind kind)
    {
        var entry = new TriggerEntry { Spec = TriggerKinds.Default(kind), MacroName = Name.Trim() };
        WatchTriggerEntry(entry);
        TriggerEntries.Add(entry);
        SelectedTrigger = entry;
        return entry;
    }

    public VariableCenterViewModel VariableCenter { get; } = new();

    public ObservableCollection<TriggerEntry> TriggerEntries { get; } = [];

    public ObservableCollection<HotstringEndKeyOption> HotstringEndKeys { get; } = [];

    public ObservableCollection<string> InterfaceGroupMembers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InterfaceHotkeyButton))]
    public partial bool IsCapturingInterfaceHotkey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InterfaceHotkeyButton))]
    [NotifyPropertyChangedFor(nameof(InterfaceToggleHotkeyDisplay))]
    public partial string InterfaceToggleHotkey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InterfaceHotkeyProblem { get; set; } = string.Empty;

    public string InterfaceHotkeyButton => Strings.Get(IsCapturingInterfaceHotkey
        ? "Editor.HotstringEndCaptureWaiting"
        : "Settings.HotkeySet");

    public string InterfaceToggleHotkeyDisplay => InterfaceToggleHotkey.Length > 0
        ? InterfaceToggleHotkey
        : Strings.Get("Common.None");

    public void LoadProjectTriggerSettings(ProjectTriggers triggers,
        IReadOnlyList<MacroItem> macros)
    {
        _interfaceLayouts.Clear();
        foreach (var layout in triggers.InterfaceLayouts)
        {
            _interfaceLayouts[layout.GroupKey] = layout with { };
        }

        _interfaceMembers.Clear();
        foreach (var group in triggers.InterfaceGroups())
        {
            _interfaceMembers[group.Key] = InterfaceMemberResolver.Resolve(group, macros);
        }

        UpdateInterfaceGroupFields();
    }

    public void LoadDraftProjectSettings(MacroItem macro)
    {
        // The layouts a project holds are the same for every one of its macros, so a draft opens on
        // what LoadProjectTriggerSettings already read; nothing here belongs to the macro itself.
    }

    /// <summary>
    /// The interface layouts as the drawer has them. Layouts belong to a group of macros, so they
    /// are written back to the project rather than kept on the macro being edited.
    /// </summary>
    public IReadOnlyList<InterfaceLayout> InterfaceLayoutsSnapshot()
        => [.. _interfaceLayouts.Values.Select(layout => layout with { })];

    /// <summary>
    /// What this editor hands the project when it saves. The entries travel with the macro because
    /// the project writes them as one change, and the layouts because the drawer may have changed a
    /// group's switch key.
    /// </summary>
    public MacroDraft Draft() => new(
        BuildMacro(),
        [.. TriggerEntries.Select(entry => entry.Copy())],
        InterfaceLayoutsSnapshot());

    public bool SetInterfaceToggleHotkey(string binding)
    {
        var text = binding.Trim();
        if (text.Length > 0 && !HotkeyBinding.IsSupported(text))
        {
            IsCapturingInterfaceHotkey = false;
            InterfaceHotkeyProblem = Strings.Get("Editor.InterfaceHotkeyUnsupported");
            return false;
        }

        IsCapturingInterfaceHotkey = false;
        InterfaceHotkeyProblem = string.Empty;
        InterfaceToggleHotkey = text;
        RememberInterfaceHotkey();
        return true;
    }

    public void ClearInterfaceToggleHotkey()
    {
        IsCapturingInterfaceHotkey = false;
        InterfaceHotkeyProblem = string.Empty;
        InterfaceToggleHotkey = string.Empty;
        RememberInterfaceHotkey();
    }

    [RelayCommand]
    private void ClearInterfaceHotkey()
    {
        ClearInterfaceToggleHotkey();
        MarkDirty();
    }

    /// <summary>
    /// Takes a key the hook can read as this macro's arm/disarm switch. A key the hook cannot see
    /// is refused here rather than saved and found dead at the first press; the answer comes back
    /// so the window can drop its capture state.
    /// </summary>
    public bool SetToggleKey(string binding)
    {
        var text = binding.Trim();
        if (text.Length > 0 && !HotkeyBinding.IsSupported(text))
        {
            IsCapturingToggleKey = false;
            ToggleKeyProblem = Strings.Get("Editor.InterfaceHotkeyUnsupported");
            return false;
        }

        IsCapturingToggleKey = false;
        ToggleKeyProblem = string.Empty;
        ToggleKey = text;
        return true;
    }

    public void ClearToggleKey()
    {
        IsCapturingToggleKey = false;
        ToggleKeyProblem = string.Empty;
        ToggleKey = string.Empty;
    }

    [RelayCommand]
    private void ClearToggleKeyCommand()
    {
        ClearToggleKey();
        MarkDirty();
    }

    /// <summary>
    /// Which page holds the reason a save was refused: a bad name or hotstring is written on the
    /// first page, a bad declaration on the second.
    /// </summary>
    internal MacroSettingsPage PageThatBlocksSaving =>
        HasNameProblem || HasBlockingHotstringProblem
            ? MacroSettingsPage.General
            : MacroSettingsPage.Specific;

    private void RememberInterfaceHotkey()
    {
        if (!WindowTarget.HasTarget)
        {
            return;
        }

        var key = ProjectTriggers.GroupKey(WindowTarget);
        var current = _interfaceLayouts.TryGetValue(key, out var layout)
            ? layout
            : new InterfaceLayout(WindowTarget, 16, 64);
        _interfaceLayouts[key] = current with { ToggleHotkey = InterfaceToggleHotkey.Trim() };
    }

    private void UpdateInterfaceGroupFields()
    {
        InterfaceGroupMembers.Clear();
        OnPropertyChanged(nameof(WindowTargetSummary));
        var previousLoading = _loading;
        _loading = true;
        try
        {
            if (!WindowTarget.HasTarget || SelectedTrigger?.Kind != TriggerKind.Interface)
            {
                InterfaceToggleHotkey = string.Empty;
                return;
            }

            var key = ProjectTriggers.GroupKey(WindowTarget);
            InterfaceToggleHotkey = _interfaceLayouts.TryGetValue(key, out var layout)
                ? layout.ToggleHotkey
                : string.Empty;
            if (_interfaceMembers.TryGetValue(key, out var members))
            {
                foreach (var member in members)
                {
                    InterfaceGroupMembers.Add(member.Display);
                }
            }

            if (Name.Length > 0
                && !InterfaceGroupMembers.Contains(Name, StringComparer.OrdinalIgnoreCase))
            {
                InterfaceGroupMembers.Add(Name);
            }
        }
        finally
        {
            _loading = previousLoading;
        }
    }

    public TriggerEntry? SelectedTrigger
    {
        get => _selectedTrigger;
        set
        {
            if (ReferenceEquals(_selectedTrigger, value))
            {
                return;
            }

            CommitTriggerFields();
            _selectedTrigger = value;
            LoadTriggerFields(value);
            UpdateInterfaceGroupFields();
            OnPropertyChanged();
            NotifyTriggerSelectionChanged();
        }
    }

    public bool HasSelectedTrigger => SelectedTrigger is not null;

    public bool ShowsTriggerSettings => HasSelectedTrigger && TriggerMode != TriggerKind.Menu;

    public bool ShowsLoopMode => HasSelectedTrigger && TriggerMode == TriggerKind.Key;

    public bool ShowsTriggerOnce => HasSelectedTrigger && TriggerMode
        is TriggerKind.Key or TriggerKind.Color or TriggerKind.Timer or TriggerKind.File
        or TriggerKind.Process or TriggerKind.Window or TriggerKind.Idle;

    public string TriggerProblemSummary
    {
        get
        {
            var problems = new List<string>();
            foreach (var spec in TriggerEntries.Select(entry => entry.Spec).OfType<HotstringSpec>())
            {
                if (spec.Abbreviation.Length == 0)
                {
                    problems.Add(Strings.Get("Editor.Problem.EmptyHotstring"));
                }
                else if (spec.Abbreviation.Length > HotstringRecognizer.MaximumAbbreviationLength)
                {
                    problems.Add(Strings.Format("Editor.Problem.LongHotstring",
                        HotstringRecognizer.MaximumAbbreviationLength));
                }

                if (!HotstringCharacters.IsAllowed(spec.Abbreviation))
                {
                    problems.Add(Strings.Get("Editor.Problem.HotstringCharacters"));
                }

                if (spec.RequireEndChar
                    && HotstringCharacters.ParseEndChars(spec.EndChars).Count == 0)
                {
                    problems.Add(Strings.Get("Editor.Problem.EmptyHotstringEndChars"));
                }

                if (HotstringCharacters.ParseEndChars(spec.EndChars)
                    .Any(value => value.Length != 1 || !HotstringCharacters.IsAllowed(value[0])))
                {
                    problems.Add(Strings.Get("Editor.Problem.HotstringEndCharacters"));
                }
            }

            var bindings = TriggerEntries
                .Where(entry => entry.IsEnabled && entry.Spec is KeySpec { Binding.Length: > 0 })
                .Select(entry => ((KeySpec)entry.Spec).Binding.Trim())
                .Concat(InterfaceHotkeysForChecks())
                .Append(ToggleKey.Trim())
                .Append(LocalSettings.LoadMenuLauncher())
                .Append(LocalSettings.LoadSystemHotkey())
                .Where(binding => binding.Length > 0)
                .GroupBy(binding => binding, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);
            foreach (var binding in bindings.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                problems.Add(Strings.Format("Editor.Problem.TriggerConflict", binding));
            }

            return string.Join("\n", problems);
        }
    }

    private IEnumerable<string> InterfaceHotkeysForChecks()
    {
        var currentKey = WindowTarget.HasTarget
            ? ProjectTriggers.GroupKey(WindowTarget)
            : string.Empty;
        return _interfaceLayouts.Values
            .Where(layout => layout.ToggleHotkey.Trim().Length > 0
                && (layout.GroupKey == currentKey
                    || _interfaceMembers.ContainsKey(layout.GroupKey)))
            .Select(layout => layout.ToggleHotkey.Trim());
    }

    public bool HasTriggerProblem => TriggerProblemSummary.Length > 0;

    public IReadOnlyList<string> TriggerKindOptions =>
        [.. TriggerKinds.Editable.Select(TriggerKinds.Text)];

    public IReadOnlyList<TriggerKindChoice> TriggerKindChoices =>
    [
        .. TriggerKinds.Editable.Select((kind, index) =>
            new TriggerKindChoice(kind, TriggerKindOptions[index])),
    ];

    [RelayCommand]
    private void AddTrigger(TriggerKind? kind)
    {
        CommitTriggerFields();
        AddEntry(kind ?? TriggerKind.Key);
        MarkDirty();
    }

    [RelayCommand]
    private void SelectTrigger(TriggerEntry? entry)
    {
        if (entry is not null)
        {
            SelectedTrigger = entry;
        }
    }

    [RelayCommand]
    private void RemoveTrigger(TriggerEntry? removing)
    {
        if (removing is null || !removing.IsRemovable)
        {
            return;
        }

        var index = TriggerEntries.IndexOf(removing);
        if (index < 0)
        {
            return;
        }

        removing.PropertyChanged -= OnTriggerEntryChanged;
        TriggerEntries.Remove(removing);
        _selectedTrigger = TriggerEntries.Count == 0
            ? null
            : TriggerEntries[Math.Clamp(index, 0, TriggerEntries.Count - 1)];
        LoadTriggerFields(_selectedTrigger);
        OnPropertyChanged(nameof(SelectedTrigger));
        NotifyTriggerSelectionChanged();
        MarkDirty();
    }

    private void NotifyTriggerSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedTrigger));
        OnPropertyChanged(nameof(ShowsTriggerSettings));
        OnPropertyChanged(nameof(ShowsLoopMode));
        OnPropertyChanged(nameof(ShowsTriggerOnce));
    }

    private void WatchTriggerEntry(TriggerEntry entry)
        => entry.PropertyChanged += OnTriggerEntryChanged;

    private void OnTriggerEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loading)
        {
            MarkDirty();
            OnPropertyChanged(nameof(TriggerProblemSummary));
            OnPropertyChanged(nameof(HasTriggerProblem));
            OnPropertyChanged(nameof(CanSave));
        }
    }
}
