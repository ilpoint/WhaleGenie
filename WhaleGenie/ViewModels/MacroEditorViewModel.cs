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
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

using WindowRuleModel = WhaleGenie.Core.Devices.WindowRule;

namespace WhaleGenie.ViewModels;

public sealed record TriggerKindChoice(TriggerKind Kind, string Label);

public partial class MacroEditorViewModel : ViewModelBase
{
    /// <summary>Raised when the editor should close, with the macro to save or <c>null</c> when cancelled.</summary>
    public event Action<MacroItem?>? CloseRequested;

    /// <summary>Action types offered by the Action Editor palette.</summary>
    public IReadOnlyList<MacroAction> ActionPalette { get; } =
    [
        new() { Icon = AppIcons.Undo,
                LabelKey = "Palette.Undo", Key = "undo", NeedsUndo = true },

        new() { Icon = AppIcons.Redo,
                LabelKey = "Palette.Redo", Key = "redo", NeedsRedo = true },

        new() { Icon = AppIcons.Pencil,
                LabelKey = "Palette.Edit", Key = "edit", NeedsSingleSelection = true },

        new() { Icon = AppIcons.Toggle,
                LabelKey = "Palette.ToggleEnabled", Key = "toggleEnabled", NeedsSelection = true },

        // Copy borrows the two-sheets glyph: with the duplicate entry gone there is one copy
        // button, and this reads as "a copy of the steps" more plainly than a single sheet.
        new() { Icon = AppIcons.Copy,
                LabelKey = "Palette.Copy", Key = "copy", NeedsSelection = true },

        new() { Icon = AppIcons.Scissors,
                LabelKey = "Palette.Cut", Key = "cut", NeedsSelection = true },

        // A plain clipboard, the usual "paste" glyph. The old one was a clipboard with a
        // downward arrow, which reads as "download" and had nothing to do with the step list.
        new() { Icon = AppIcons.Paste,
                LabelKey = "Palette.Paste", Key = "paste" },

        new() { Icon = AppIcons.Trash,
                LabelKey = "Palette.Delete", Key = "delete", NeedsSelection = true },

        new() { Icon = AppIcons.Group,
                LabelKey = "Palette.Group",
                Key = "group", NeedsSelection = true },

        // An arrow leaving a box: the selected steps are taken out of this macro and put
        // into one of their own, with a call left where they were.
        new() { Icon = AppIcons.ExternalLink,
                LabelKey = "Palette.LiftToMacro",
                Key = "liftToMacro", NeedsSelection = true },

        new() { Icon = AppIcons.ArrowUp,
                LabelKey = "Palette.MoveUp", Key = "moveUp", NeedsSelection = true },

        new() { Icon = AppIcons.ArrowDown,
                LabelKey = "Palette.MoveDown", Key = "moveDown", NeedsSelection = true },

        new() { Icon = AppIcons.X,
                LabelKey = "Palette.Clear", Key = "clear", NeedsSteps = true },

        new() { Icon = AppIcons.Clock,
                LabelKey = "Palette.AdjustDelays", Key = "adjustDelays",
                NeedsSteps = true },

        new() { Icon = AppIcons.Play,
                LabelKey = "Palette.RunMacro", Key = "runMacro" },
    ];

    /// <summary>Palette id of the entry that opens the add-action dialog.</summary>
    private const string AddStepPaletteKey = "add";

    private readonly List<MacroStep> _selection = [];

    /// <summary>
    /// The exact list and index an action chosen from the library will occupy. The owner and
    /// parameter travel with it because a section can limit which action kinds it accepts.
    /// </summary>
    private StepInsertionTarget? _insertAt;

    private sealed record StepInsertionTarget(IList<MacroStep> List, int Index,
        MacroStep? Owner, StepParameter? Parameter);

    private bool _loading;

    /// <summary>Where a captured key goes when the inspector asked for one, or null for the trigger.</summary>
    private Action<string>? _keyCaptureTarget;

    private bool _captureKeyboardOnly;
    private bool _captureSingleKey;
    private TriggerEntry? _selectedTrigger;
    private bool _updatingHotstringEndKeys;
    private readonly Dictionary<string, InterfaceLayout> _interfaceLayouts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<InterfaceGroupMember>> _interfaceMembers =
        new(StringComparer.Ordinal);
    private string _projectMenuLauncher = string.Empty;

    /// <summary>How many step-list changes can be undone.</summary>
    private const int HistoryLimit = 60;

    private readonly List<HistoryEntry> _undo = [];
    private readonly List<HistoryEntry> _redo = [];

    /// <summary>
    /// The steps and the selection as they were before one change. The selection is kept as the
    /// positions the steps are written in, counting nested ones in the order the list shows them,
    /// because the history holds copies of the steps rather than the steps themselves.
    /// </summary>
    private sealed record HistoryEntry(
        IReadOnlyList<MacroStep> Steps, IReadOnlyList<int> Selection, double DelayScale);

    public MacroEditorViewModel()
    {
        TriggerEntries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TriggerProblemSummary));
            OnPropertyChanged(nameof(HasTriggerProblem));
        };
        var first = new TriggerEntry { Kind = TriggerKind.Key };
        WatchTriggerEntry(first);
        TriggerEntries.Add(first);
        _selectedTrigger = first;
        LoadTriggerFields(first);
        RebuildHotstringEndKeys();
        RefreshPaletteState();
        RefreshVariableCenter();
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
        _projectMenuLauncher = triggers.MenuLauncher;
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
        var loading = _loading;
        _loading = true;
        try
        {
            _interfaceLayouts.Clear();
            foreach (var layout in macro.InterfaceLayouts)
            {
                _interfaceLayouts[layout.GroupKey] = layout with { };
            }
        }
        finally
        {
            _loading = loading;
        }
        UpdateInterfaceGroupFields();
    }

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
            foreach (var entry in TriggerEntries.Where(entry => entry.Kind == TriggerKind.Hotstring))
            {
                if (entry.Abbreviation.Length == 0)
                {
                    problems.Add(Strings.Get("Editor.Problem.EmptyHotstring"));
                }
                else if (entry.Abbreviation.Length > HotstringRecognizer.MaximumAbbreviationLength)
                {
                    problems.Add(Strings.Format("Editor.Problem.LongHotstring",
                        HotstringRecognizer.MaximumAbbreviationLength));
                }

                if (!HotstringCharacters.IsAllowed(entry.Abbreviation))
                {
                    problems.Add(Strings.Get("Editor.Problem.HotstringCharacters"));
                }

                if (entry.RequireEndChar
                    && HotstringCharacters.ParseEndChars(entry.HotstringEndChars).Count == 0)
                {
                    problems.Add(Strings.Get("Editor.Problem.EmptyHotstringEndChars"));
                }

                if (HotstringCharacters.ParseEndChars(entry.HotstringEndChars)
                    .Any(value => value.Length != 1 || !HotstringCharacters.IsAllowed(value[0])))
                {
                    problems.Add(Strings.Get("Editor.Problem.HotstringEndCharacters"));
                }
            }

            var bindings = TriggerEntries
                .Where(entry => entry.IsEnabled && entry.Kind == TriggerKind.Key
                    && entry.BindKey.Trim().Length > 0)
                .Select(entry => entry.BindKey.Trim())
                .Concat(InterfaceHotkeysForChecks())
                .Append(_projectMenuLauncher)
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

    private static readonly TriggerKind[] EditableTriggerKinds =
    [
        TriggerKind.Key,
        TriggerKind.Color,
        TriggerKind.Interface,
        TriggerKind.Hotstring,
        TriggerKind.Timer,
        TriggerKind.File,
        TriggerKind.Process,
        TriggerKind.Window,
        TriggerKind.Idle,
    ];

    public IReadOnlyList<string> TriggerKindOptions =>
    [
        .. EditableTriggerKinds.Select(kind => Strings.Get(kind switch
        {
            TriggerKind.Key => "Trigger.Keys",
            TriggerKind.Color => "Trigger.Color",
            TriggerKind.Interface => "Trigger.Interface",
            TriggerKind.Hotstring => "Trigger.Hotstring",
            TriggerKind.Timer => "Trigger.Timer",
            TriggerKind.File => "Trigger.File",
            TriggerKind.Process => "Trigger.Process",
            TriggerKind.Window => "Trigger.Window",
            _ => "Trigger.Idle",
        })),
    ];

    public IReadOnlyList<TriggerKindChoice> TriggerKindChoices =>
    [
        .. EditableTriggerKinds.Select((kind, index) =>
            new TriggerKindChoice(kind, TriggerKindOptions[index])),
    ];

    [RelayCommand]
    private void AddTrigger(TriggerKind? kind)
    {
        CommitTriggerFields();
        var entry = new TriggerEntry
        {
            Kind = kind ?? TriggerKind.Key,
            MacroName = Name.Trim(),
        };
        WatchTriggerEntry(entry);
        TriggerEntries.Add(entry);
        SelectedTrigger = entry;
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

    /// <summary>Re-reads the labels this editor computes, the palette's tips and the step rows.</summary>
    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        PlacementNotice = string.Empty;
        foreach (var action in ActionPalette)
        {
            action.RefreshLanguage();
        }

        foreach (var row in Rows)
        {
            row.RefreshLanguage();
        }

        WindowPreview.RefreshLanguage();
    }

    /// <summary>Clears changes made only by controls taking their initial bound values.</summary>
    internal void AcceptInitialBindings() => IsDirty = false;

    /// <summary>Raised when the editor should open the "Add Action" dialog.</summary>
    public event Action? AddStepRequested;

    /// <summary>Raised when the editor should open the edit dialog for an existing step.</summary>
    public event Action<MacroStep>? EditStepRequested;

    /// <summary>Raised when the palette asks to clear the list, so the window can confirm it.</summary>
    public event Action? ClearRequested;

    /// <summary>Raised when the palette or a shortcut asks to copy the selected steps.</summary>
    public event Action? CopyRequested;

    /// <summary>Raised when the palette or a shortcut asks to cut the selected steps.</summary>
    public event Action? CutRequested;

    /// <summary>Raised when the palette or a shortcut asks to paste below the selection.</summary>
    public event Action? PasteRequested;

    /// <summary>Raised when the editor should open the run window for the current steps.</summary>
    public event Action? RunRequested;

    /// <summary>
    /// Raised when a save was asked for while the name or a declaration still blocks it. The window
    /// answers by showing the page where the reason is written.
    /// </summary>
    public event Action? SaveBlocked;

    /// <summary>Raised when the palette asks to open the run-speed dialog.</summary>
    public event Action? AdjustDelaysRequested;

    /// <summary>
    /// Raised when a stretch of steps was lifted into a macro of its own: the project takes the
    /// new macro, because the call left behind in this one is a call to it.
    /// </summary>
    public event Action<MacroItem>? MacroLifted;

    /// <summary>Raised when the palette asks for a step that runs another macro.</summary>
    public event Action? RunMacroRequested;

    /// <summary>
    /// Raised when recording starts or stops, so the window can put the input hook up or take
    /// it down. <c>true</c> means a recording is beginning.
    /// </summary>
    public event Action<bool>? RecordingChanged;

    /// <summary>
    /// Raised when the list should re-apply the current selection, for example after
    /// a move or a duplicate, where the items change identity or position.
    /// </summary>
    public event Action? SelectionRefreshRequested;

    /// <summary>Steps built so far; saved as the macro's JSON tree.</summary>
    public ObservableCollection<MacroStep> Steps { get; } = [];

    /// <summary>
    /// The same steps as the editor shows them: a block that is open shows the steps inside it
    /// indented underneath, with a line where it starts and a line where it ends. The list is
    /// flat because that is what the control and every command work on, and a row carries the
    /// depth and the list it belongs to, so the shape of the macro can be read off one list.
    /// </summary>
    public ObservableCollection<StepRow> Rows { get; } = [];

    public bool HasSteps => Steps.Count > 0;

    /// <summary>The document tabs shared by the parent and every child editor in one window.</summary>
    public ObservableCollection<MacroDocumentPageViewModel> Documents { get; internal set; } = [];

    /// <summary>Child definitions carried by this document when it is read back or saved.</summary>
    public List<MacroItem> SubMacros { get; } = [];

    /// <summary>
    /// True when this editor is showing a callable definition owned by another macro. Such a
    /// definition has no trigger of its own, so trigger-only validation must not block it.
    /// </summary>
    public bool IsSubMacro { get; internal set; }

    /// <summary>The live reader the variable wall uses for WhaleGenie's own values.</summary>
    internal SystemVariableReader? SystemReader { get; set; }

    /// <summary>The public macro this definition was copied from, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OriginLabel))]
    [NotifyPropertyChangedFor(nameof(HasOrigin))]
    public partial MacroOrigin? Origin { get; set; }

    public bool HasOrigin => Origin is not null;

    public string OriginLabel => Origin is { } origin
        ? Strings.Format("Editor.PublicOrigin", origin.Name, origin.Version)
        : string.Empty;

    /// <summary>
    /// Names of the other macros in the project. A step that calls a macro is checked against
    /// these, so a call left pointing at a name that has gone is called out here rather than
    /// waiting for the trigger to fire. The macro being edited is added by the check itself,
    /// because a macro may call itself.
    /// </summary>
    private IReadOnlyList<MacroCallable> _projectMacros = [];

    /// <summary>
    /// True when another macro of the same project also sends keys or clicks: the check has to know
    /// that to say two of them running at once would write their input through each other.
    /// </summary>
    private bool _inputShared;

    /// <summary>
    /// True when another macro of the same project also uses the browser. One browser serves the
    /// whole program, so a macro that changes tabs changes what the other one reads.
    /// </summary>
    private bool _browserShared;

    /// <summary>
    /// What this macro asks a call to fill in. The editor holds it so that opening a macro and
    /// saving it again does not drop what it declares, and so a call this macro makes to itself is
    /// judged by the same list.
    /// </summary>
    public List<MacroParameter> DeclaredParameters { get; } = [];

    /// <summary>The names this macro offers to hand back to a call.</summary>
    public List<MacroResult> DeclaredResults { get; } = [];

    /// <summary>This macro's own declarations, in the shape a call to it is read against.</summary>
    /// <remarks>
    /// The steps travel with the declarations because the call graph has to be able to follow a
    /// macro that calls itself. Without them a self-call looks like a call into a macro with no
    /// steps, so the input and browser analysis would miss everything behind the recursion.
    /// </remarks>
    private MacroCallable OwnCallable => new(Name, DeclaredParameters, DeclaredResults, Steps);

    /// <summary>What the design-time check has to say about the steps as they stand now.</summary>
    public IReadOnlyList<MacroProblem> Problems { get; private set; } = [];

    /// <summary>What is wrong with this macro's own parameters and results, with no step to hang it on.</summary>
    public IReadOnlyList<string> DeclarationProblems { get; private set; } = [];

    public bool HasProblems => Problems.Count > 0;

    public bool HasStepProblems => Problems.Count > 0;

    public bool HasDeclarationProblems => DeclarationProblems.Count > 0;

    /// <summary>
    /// True while a declaration problem makes the macro impossible to call: an empty or unreadable
    /// name, a name that collides with the supplied-empty marker, a duplicate, a run-reserved name,
    /// or a required argument with a default. Warnings such as "this result is never written" do
    /// not block saving work that is still being written.
    /// </summary>
    public bool HasBlockingDeclarationProblems => OwnCallable.Problems().Count > 0;

    /// <summary>The save button's answer: the macro name and the declarations both have to be usable.</summary>
    public bool CanSave => !HasNameProblem && !HasBlockingDeclarationProblems
        && (IsSubMacro || !HasBlockingHotstringProblem);

    private bool HasBlockingHotstringProblem
        => (SelectedTrigger?.Kind == TriggerKind.Hotstring
            && IsBlockingHotstring(HotstringText, HotstringRequireEnd, HotstringEndChars))
            || TriggerEntries.Any(entry => entry.Kind == TriggerKind.Hotstring
                && !ReferenceEquals(entry, SelectedTrigger)
                && IsBlockingHotstring(
                    entry.Abbreviation, entry.RequireEndChar, entry.HotstringEndChars));

    private static bool IsBlockingHotstring(
        string abbreviation, bool requireEndChar, string endChars)
    {
        if (abbreviation.Length is 0 or > HotstringCharacters.MaximumLength
            || !HotstringCharacters.IsAllowed(abbreviation))
        {
            return true;
        }

        if (!requireEndChar)
        {
            return false;
        }

        var characters = HotstringCharacters.ParseEndChars(endChars);
        return characters.Count == 0
            || characters.Any(value =>
                value.Length != 1 || !HotstringCharacters.IsAllowed(value[0]));
    }

    /// <summary>
    /// One line saying how many calls in the project no longer match these declarations. The
    /// declarations are edited in this macro, but the calls they broke live in other macros, so
    /// the consequence is said here rather than left until each caller is opened.
    /// </summary>
    public string CallImpactSummary { get; private set; } = string.Empty;

    public bool HasCallImpact => CallImpactSummary.Length > 0;

    /// <summary>
    /// Why the last drag, paste or structural edit was refused. It stays above the step list until
    /// another edit succeeds or the selection moves, so a refused drop does not look like a silent
    /// failure.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlacementNotice))]
    public partial string PlacementNotice { get; set; } = string.Empty;

    public bool HasPlacementNotice => PlacementNotice.Length > 0;

    /// <summary>One line above the list saying how much the editor has to say about the steps.</summary>
    public string ProblemSummary => Strings.Format("Editor.ProblemSummary",
        Problems.Select(problem => problem.Step).Distinct().Count());

    /// <summary>The declaration problems, one per line, for the editor to show above the step list.</summary>
    public string DeclarationProblemSummary => string.Join("\n", DeclarationProblems);

    /// <summary>
    /// What is wrong with the name the macro is about to be saved under. A macro is called by its
    /// name, so an empty one cannot be called at all and a duplicate would make the project hold
    /// two macros that answer to the same call — one of them silently winning.
    /// </summary>
    public string? NameProblem => Name.Trim().Length == 0
        ? Strings.Get("Editor.Problem.EmptyMacroName")
        : _projectMacros.Any(macro =>
            string.Equals(macro.Name.Trim(), Name.Trim(), StringComparison.OrdinalIgnoreCase))
            ? Strings.Format("Editor.Problem.DuplicateMacroName", Name.Trim())
            : null;

    public bool HasNameProblem => NameProblem is not null;

    /// <summary>Which steps the check is unhappy about, and what it says about each one.</summary>
    private Dictionary<MacroStep, string> _problemByStep = [];

    /// <summary>Hands in the macro names a call may reach, and checks the steps again.</summary>
    public void SetProjectMacros(IEnumerable<MacroCallable> macros, bool inputShared = false,
        bool browserShared = false)
    {
        _projectMacros = [.. macros];
        OnPropertyChanged(nameof(NameProblem));
        OnPropertyChanged(nameof(HasNameProblem));
        _inputShared = inputShared;
        _browserShared = browserShared;
        RebuildRows();
    }

    public bool HasSelection => _selection.Count > 0;

    /// <summary>The steps highlighted in the action list.</summary>
    public IReadOnlyList<MacroStep> SelectedSteps => _selection;

    /// <summary>True while the editor holds changes that have not been saved yet.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>
    /// True while the rows are being rebuilt. The list lets go of its selection when the rows
    /// under it are replaced, and that is not the user unpicking anything, so the view has to
    /// know to look past it — otherwise every edit would end with nothing selected.
    /// </summary>
    public bool IsRebuildingRows { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameProblem))]
    [NotifyPropertyChangedFor(nameof(HasNameProblem))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>A sentence shown with a callable macro in pickers and libraries.</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsColorTrigger))]
    [NotifyPropertyChangedFor(nameof(IsKeyTrigger))]
    [NotifyPropertyChangedFor(nameof(IsInterfaceTrigger))]
    [NotifyPropertyChangedFor(nameof(IsHotstringTrigger))]
    [NotifyPropertyChangedFor(nameof(IsMenuTrigger))]
    [NotifyPropertyChangedFor(nameof(IsTimerTrigger))]
    [NotifyPropertyChangedFor(nameof(IsFileTrigger))]
    [NotifyPropertyChangedFor(nameof(IsProcessTrigger))]
    [NotifyPropertyChangedFor(nameof(IsWindowTrigger))]
    [NotifyPropertyChangedFor(nameof(IsWindowRuleTrigger))]
    [NotifyPropertyChangedFor(nameof(IsIdleTrigger))]
    public partial TriggerKind TriggerMode { get; set; } = TriggerKind.Key;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoopIndex))]
    public partial MacroLoop LoopMode { get; set; } = MacroLoop.Toggle;

    public IReadOnlyList<string> LoopOptions =>
    [
        Strings.Get("Loop.Toggle"),
        Strings.Get("Loop.Hold"),
        Strings.Get("Loop.Press"),
        Strings.Get("Loop.Release"),
    ];

    /// <summary>True when the macro is started by a key or a mouse button.</summary>
    public bool IsKeyTrigger => TriggerMode == TriggerKind.Key;

    /// <summary>True when the macro is started by a colour / pixel change rather than by input.</summary>
    public bool IsColorTrigger => TriggerMode == TriggerKind.Color;

    public bool IsInterfaceTrigger => TriggerMode == TriggerKind.Interface;

    public bool IsHotstringTrigger => TriggerMode == TriggerKind.Hotstring;

    public bool IsMenuTrigger => TriggerMode == TriggerKind.Menu;

    /// <summary>True when the macro is started by the clock.</summary>
    public bool IsTimerTrigger => TriggerMode == TriggerKind.Timer;

    /// <summary>True when the macro is started by a file or a folder changing.</summary>
    public bool IsFileTrigger => TriggerMode == TriggerKind.File;

    /// <summary>True when the macro is started by a program starting or finishing.</summary>
    public bool IsProcessTrigger => TriggerMode == TriggerKind.Process;

    /// <summary>True when the macro is started by a window appearing or going away.</summary>
    public bool IsWindowTrigger => TriggerMode == TriggerKind.Window;

    public bool IsWindowRuleTrigger => TriggerMode is TriggerKind.Window or TriggerKind.Interface;

    /// <summary>True when the macro is started by the machine going untouched.</summary>
    public bool IsIdleTrigger => TriggerMode == TriggerKind.Idle;

    /// <summary>How long the machine has to be left alone before an idle trigger runs the macro.</summary>
    [ObservableProperty]
    public partial int? IdleSeconds { get; set; } = 60;

    /// <summary>The captured window and the rule a window trigger uses to recognise it again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowRuleNow))]
    [NotifyPropertyChangedFor(nameof(WindowTargetSummary))]
    public partial WindowRuleModel WindowTarget { get; set; } = WindowRuleModel.Empty;

    /// <summary>
    /// The windows this trigger would fire for right now, listed under the fields that say how the
    /// window is recognised. A window trigger is the easiest thing in a macro to get wrong, since a
    /// rule written a little too wide fires on a window nobody meant, and the list says what the
    /// rule comes to without waiting for it to happen.
    /// </summary>
    public WindowPreviewViewModel WindowPreview { get; } = new();

    /// <summary>The windows the list is read from, handed over by the editor that opened this.</summary>
    public IWindowDevice? Windows
    {
        get => _windows;
        set
        {
            _windows = value;
            WindowPreview.Watch(() => _windows);
        }
    }

    private IWindowDevice? _windows;

    /// <summary>The rule as the fields stand, which is what the preview of open windows follows.</summary>
    public WindowRuleModel WindowRuleNow => WindowTarget;

    public string WindowTargetSummary => WindowTarget.HasTarget
        ? $"{WindowTarget.MatchValue} · {Strings.Get($"WindowRule.Short.{WindowTarget.How}", WindowTarget.How)}"
        : Strings.Get("WindowRule.NotSet");

    /// <summary>Stores the rule the trigger picker captured.</summary>
    public void UseWindowTarget(WindowRuleModel rule)
    {
        WindowTarget = rule;
        OnPropertyChanged(nameof(WindowRuleNow));
        OnPropertyChanged(nameof(WindowTargetSummary));
    }

    partial void OnWindowTargetChanged(WindowRuleModel value)
        => UpdateInterfaceGroupFields();

    partial void OnInterfaceToggleHotkeyChanged(string value)
    {
        if (!_loading)
        {
            RememberInterfaceHotkey();
            MarkDirty();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotstringTextDisplay))]
    [NotifyPropertyChangedFor(nameof(TriggerProblemSummary))]
    [NotifyPropertyChangedFor(nameof(HasTriggerProblem))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string HotstringText { get; set; } = string.Empty;

    /// <summary>The literal abbreviation shown on the typed-string keyboard button.</summary>
    public string HotstringTextDisplay => HotstringText.Length > 0
        ? HotstringCharacters.Display(HotstringText)
        : Strings.Get("Editor.HotstringChoose");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TriggerProblemSummary))]
    [NotifyPropertyChangedFor(nameof(HasTriggerProblem))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string HotstringEndChars { get; set; } = HotstringCharacters.DefaultEndChars;

    public int HotstringMaximumLength => HotstringCharacters.MaximumLength;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotstringEndCaptureButton))]
    public partial bool IsCapturingHotstringEnd { get; set; }

    [ObservableProperty]
    public partial string HotstringEndCaptureStatus { get; set; } = string.Empty;

    public string HotstringEndCaptureButton => Strings.Get(IsCapturingHotstringEnd
        ? "Editor.HotstringEndCaptureWaiting"
        : "Editor.HotstringEndCapture");

    partial void OnHotstringEndCharsChanged(string value)
    {
        if (!_updatingHotstringEndKeys)
        {
            RebuildHotstringEndKeys();
        }
    }

    [RelayCommand]
    private void ResetHotstringEndChars()
    {
        HotstringEndChars = HotstringCharacters.DefaultEndChars;
        MarkDirty();
    }

    public void BeginHotstringEndCapture()
    {
        StopCapturingKey();
        HotstringEndCaptureStatus = string.Empty;
        IsCapturingHotstringEnd = true;
    }

    public void StopHotstringEndCapture()
    {
        IsCapturingHotstringEnd = false;
        HotstringEndCaptureStatus = string.Empty;
    }

    public void CaptureHotstringEnd(string? character)
    {
        if (!IsCapturingHotstringEnd)
        {
            return;
        }

        if (character is not { Length: 1 } || !HotstringCharacters.IsAllowed(character[0]))
        {
            HotstringEndCaptureStatus = Strings.Get("Editor.HotstringEndCaptureInvalid");
            return;
        }

        var existing = HotstringEndKeys.FirstOrDefault(option => option.Value == character);
        if (existing is not null)
        {
            existing.IsChecked = true;
        }
        else
        {
            AddHotstringEndKey(character);
        }

        IsCapturingHotstringEnd = false;
        HotstringEndCaptureStatus = string.Empty;
    }

    private void AddHotstringEndKey(string value)
    {
        var values = HotstringCharacters.ParseEndChars(HotstringEndChars)
            .Append(value)
            .Distinct(StringComparer.Ordinal);
        _updatingHotstringEndKeys = true;
        try
        {
            HotstringEndChars = HotstringCharacters.WriteEndChars(values);
        }
        finally
        {
            _updatingHotstringEndKeys = false;
        }
        RebuildHotstringEndKeys();
    }

    private void RebuildHotstringEndKeys()
    {
        foreach (var option in HotstringEndKeys)
        {
            option.PropertyChanged -= OnHotstringEndKeyChanged;
        }
        HotstringEndKeys.Clear();

        var selected = HotstringCharacters.ParseEndChars(HotstringEndChars);
        foreach (var (name, value) in HotstringCharacters.EndKeyChoices)
        {
            AddHotstringEndOption(new HotstringEndKeyOption(value, name, isCustom: false)
            {
                IsChecked = selected.Contains(value),
            });
        }

        var standard = HotstringCharacters.EndKeyChoices
            .Select(choice => choice.Value)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var value in selected.Where(value => !standard.Contains(value))
                     .Order(StringComparer.Ordinal))
        {
            AddHotstringEndOption(new HotstringEndKeyOption(
                value, HotstringCharacters.Display(value), isCustom: true)
            {
                IsChecked = true,
            });
        }
    }

    private void AddHotstringEndOption(HotstringEndKeyOption option)
    {
        option.PropertyChanged += OnHotstringEndKeyChanged;
        HotstringEndKeys.Add(option);
    }

    private void OnHotstringEndKeyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingHotstringEndKeys || e.PropertyName != nameof(HotstringEndKeyOption.IsChecked))
        {
            return;
        }

        _updatingHotstringEndKeys = true;
        try
        {
            HotstringEndChars = HotstringCharacters.WriteEndChars(
                HotstringEndKeys.Where(option => option.IsChecked).Select(option => option.Value));
        }
        finally
        {
            _updatingHotstringEndKeys = false;
        }
    }

    [ObservableProperty]
    public partial bool HotstringCaseSensitive { get; set; }

    [ObservableProperty]
    public partial bool HotstringInsideWord { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TriggerProblemSummary))]
    [NotifyPropertyChangedFor(nameof(HasTriggerProblem))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool HotstringRequireEnd { get; set; } = true;

    [ObservableProperty]
    public partial bool HotstringEraseTyped { get; set; } = true;

    [ObservableProperty]
    public partial bool HotstringKeepEnd { get; set; } = true;

    public IReadOnlyList<string> WindowChangeOptions =>
    [
        Strings.Get("WindowChange.Appeared"),
        Strings.Get("WindowChange.Disappeared"),
        Strings.Get("WindowChange.Any"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowChangeIndex))]
    public partial WindowChangeKind WindowChange { get; set; } = WindowChangeKind.Appeared;

    public int WindowChangeIndex
    {
        get => (int)WindowChange;
        set
        {
            if (value >= 0 && value < WindowChangeOptions.Count)
            {
                WindowChange = (WindowChangeKind)value;
            }
        }
    }

    /// <summary>The program a process trigger waits for, without the ".exe".</summary>
    [ObservableProperty]
    public partial string ProcessName { get; set; } = string.Empty;

    public IReadOnlyList<string> ProcessChangeOptions =>
    [
        Strings.Get("ProcessChange.Started"),
        Strings.Get("ProcessChange.Stopped"),
        Strings.Get("ProcessChange.Any"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessChangeIndex))]
    public partial ProcessChangeKind ProcessChange { get; set; } = ProcessChangeKind.Started;

    public int ProcessChangeIndex
    {
        get => (int)ProcessChange;
        set
        {
            if (value >= 0 && value < ProcessChangeOptions.Count)
            {
                ProcessChange = (ProcessChangeKind)value;
            }
        }
    }

    /// <summary>The file or folder a file trigger watches.</summary>
    [ObservableProperty]
    public partial string WatchPath { get; set; } = string.Empty;

    public IReadOnlyList<string> WatchChangeOptions =>
    [
        Strings.Get("FileChange.Any"),
        Strings.Get("FileChange.Created"),
        Strings.Get("FileChange.Changed"),
        Strings.Get("FileChange.Deleted"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchChangeIndex))]
    public partial FileChangeKind WatchChange { get; set; } = FileChangeKind.Any;

    public int WatchChangeIndex
    {
        get => (int)WatchChange;
        set
        {
            if (value >= 0 && value < WatchChangeOptions.Count)
            {
                WatchChange = (FileChangeKind)value;
            }
        }
    }

    /// <summary>The names watched inside a watched folder, such as <c>*.txt</c>.</summary>
    [ObservableProperty]
    public partial string WatchFilter { get; set; } = "*";

    /// <summary>Whether a watched folder also reports what happens in the folders inside it.</summary>
    [ObservableProperty]
    public partial bool WatchSubfolders { get; set; }

    public IReadOnlyList<string> ScheduleOptions =>
    [
        Strings.Get("Schedule.Interval"),
        Strings.Get("Schedule.Daily"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleIndex))]
    [NotifyPropertyChangedFor(nameof(IsIntervalSchedule))]
    public partial ScheduleMode ScheduleMode { get; set; } = ScheduleMode.Interval;

    public int ScheduleIndex
    {
        get => (int)ScheduleMode;
        set
        {
            if (value >= 0 && value < ScheduleOptions.Count)
            {
                ScheduleMode = (ScheduleMode)value;
            }
        }
    }

    /// <summary>True while the schedule waits a fixed gap rather than naming a time of day.</summary>
    public bool IsIntervalSchedule => ScheduleMode == ScheduleMode.Interval;

    /// <summary>How many seconds, minutes or hours an interval schedule waits.</summary>
    [ObservableProperty]
    public partial int? ScheduleInterval { get; set; } = 5;

    public IReadOnlyList<string> ScheduleUnitOptions =>
    [
        Strings.Get("Schedule.Unit.Seconds"),
        Strings.Get("Schedule.Unit.Minutes"),
        Strings.Get("Schedule.Unit.Hours"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleUnitIndex))]
    public partial ScheduleUnit ScheduleUnit { get; set; } = ScheduleUnit.Seconds;

    public int ScheduleUnitIndex
    {
        get => (int)ScheduleUnit;
        set
        {
            if (value >= 0 && value < ScheduleUnitOptions.Count)
            {
                ScheduleUnit = (ScheduleUnit)value;
            }
        }
    }

    /// <summary>The time of day a daily schedule runs at, written <c>08:30</c>.</summary>
    [ObservableProperty]
    public partial string ScheduleTime { get; set; } = "08:00";

    public int LoopIndex
    {
        get => (int)LoopMode;
        set
        {
            if (value >= 0 && value <= (int)MacroLoop.Release)
            {
                LoopMode = (MacroLoop)value;
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BindKeyDisplay))]
    public partial string BindKey { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetKeyLabel))]
    public partial bool IsCapturingKey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordLabel))]
    [NotifyPropertyChangedFor(nameof(RecordStatusText))]
    public partial bool IsRecording { get; set; }

    /// <summary>How much has been captured since recording started, for the status line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordStatusText))]
    public partial int RecordedEvents { get; set; }

    /// <summary>The recorder asks for the count to be shown as it grows.</summary>
    public void ReportRecordedEvents(int count) => RecordedEvents = count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionCaptureIndex))]
    public partial MousePositionMode PositionCapture { get; set; } = MousePositionMode.SaveCurrentPosition;

    public IReadOnlyList<string> PositionCaptureOptions =>
    [
        Strings.Get("Position.Current"),
        Strings.Get("Position.Differences"),
    ];

    public int PositionCaptureIndex
    {
        get => (int)PositionCapture;
        set
        {
            if (value >= 0 && value <= (int)MousePositionMode.SavePositionDifferences)
            {
                PositionCapture = (MousePositionMode)value;
            }
        }
    }

    public IReadOnlyList<string> ColorMatchOptions =>
    [
        Strings.Get("ColorMatch.Matches"),
        Strings.Get("ColorMatch.NotMatches"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorMatchIndex))]
    public partial ColorMatchCondition ColorMatch { get; set; } = ColorMatchCondition.ColorMatches;

    public int ColorMatchIndex
    {
        get => (int)ColorMatch;
        set
        {
            if (value >= 0 && value <= (int)ColorMatchCondition.ColorNotMatches)
            {
                ColorMatch = (ColorMatchCondition)value;
            }
        }
    }

    /// <summary>Horizontal screen position watched when a colour / pixel trigger is used.</summary>
    [ObservableProperty]
    public partial string ColorPositionX { get; set; } = "0";

    /// <summary>Vertical screen position watched when a colour / pixel trigger is used.</summary>
    [ObservableProperty]
    public partial string ColorPositionY { get; set; } = "0";

    /// <summary>When set, the watched position follows the cursor instead of the stored coordinates.</summary>
    [ObservableProperty]
    public partial bool UseCursorPosition { get; set; }

    /// <summary>When set, the macro runs only once instead of repeating.</summary>
    [ObservableProperty]
    public partial bool TriggerOnce { get; set; }

    /// <summary>
    /// When set, this macro takes turns with the other macros that are set up the same way: only one
    /// of them sends keys or clicks at a time. It says nothing about when any of them starts.
    /// </summary>
    [ObservableProperty]
    public partial bool ExclusiveInput { get; set; }

    /// <summary>Colour watched for the trigger, as a hex string (for example <c>#000000</c>).</summary>
    [ObservableProperty]
    public partial string HexColor { get; set; } = "#000000";

    /// <summary>Allowed colour difference (percent) before the watched pixel counts as a match.</summary>
    [ObservableProperty]
    public partial string ColorTolerance { get; set; } = "5";

    [ObservableProperty]
    public partial bool RecordMouseMovement { get; set; } = true;

    [ObservableProperty]
    public partial bool RecordMouseButtons { get; set; } = true;

    [ObservableProperty]
    public partial bool RecordKeyboardKeys { get; set; } = true;

    /// <summary>Whether the buttons of the controller in the person's hands are recorded.</summary>
    [ObservableProperty]
    public partial bool RecordGamepadButtons { get; set; } = true;

    [ObservableProperty]
    public partial bool RecordDelays { get; set; } = true;

    [ObservableProperty]
    public partial decimal? IgnoreDelaysBelowMs { get; set; } = 50;

    /// <summary>When set, input another program sends is left out of the recording.</summary>
    [ObservableProperty]
    public partial bool RecordIgnoreInjected { get; set; } = true;

    /// <summary>
    /// How much longer or shorter this macro's waits are made when it runs. 1 leaves everything
    /// as written; it bends the timings only while running, so the steps keep their own numbers.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DelayScaleDisplay))]
    [NotifyPropertyChangedFor(nameof(HasDelayScale))]
    public partial double DelayScale { get; set; } = 1;

    /// <summary>The speed setting as it is shown, for example <c>×2</c>.</summary>
    public string DelayScaleDisplay => FormatScale(DelayScale);

    /// <summary>True when the macro runs at something other than its written speed.</summary>
    public bool HasDelayScale => Math.Abs(DelayScale - 1) > 0.001;

    /// <summary>Writes a speed factor the way the interface shows it.</summary>
    public static string FormatScale(double scale) => DelayScaleViewModel.FormatScale(scale);

    /// <summary>Applies a new speed factor, keeping it inside the range a run accepts.</summary>
    public void SetDelayScale(double scale)
    {
        var clamped = double.IsFinite(scale) ? Math.Clamp(scale, 0.1, 10) : 1;
        if (Math.Abs(clamped - DelayScale) < 0.001)
        {
            return;
        }

        PushUndo();
        DelayScale = clamped;
        MarkDirty();
    }

    public string BindKeyDisplay => string.IsNullOrEmpty(BindKey) ? Strings.Get("Editor.NoKey") : BindKey;

    public string SetKeyLabel => IsCapturingKey ? Strings.Get("Editor.PressKey") : Strings.Get("Editor.SetKey");

    partial void OnTriggerModeChanged(TriggerKind value)
    {
        if (!_loading && SelectedTrigger is { } selected)
        {
            selected.Kind = value;
        }

        OnPropertyChanged(nameof(IsKeyTrigger));
        OnPropertyChanged(nameof(IsColorTrigger));
        OnPropertyChanged(nameof(IsInterfaceTrigger));
        OnPropertyChanged(nameof(IsHotstringTrigger));
        OnPropertyChanged(nameof(IsMenuTrigger));
        OnPropertyChanged(nameof(IsTimerTrigger));
        OnPropertyChanged(nameof(IsFileTrigger));
        OnPropertyChanged(nameof(IsProcessTrigger));
        OnPropertyChanged(nameof(IsWindowTrigger));
        OnPropertyChanged(nameof(IsWindowRuleTrigger));
        OnPropertyChanged(nameof(IsIdleTrigger));
        OnPropertyChanged(nameof(ShowsTriggerSettings));
        OnPropertyChanged(nameof(ShowsLoopMode));
        OnPropertyChanged(nameof(ShowsTriggerOnce));
        UpdateInterfaceGroupFields();
    }

    /// <summary>Copies the fields of the form into the selected trigger entry.</summary>
    private void CommitTriggerFields()
    {
        if (SelectedTrigger is not { } entry)
        {
            return;
        }

        entry.Kind = TriggerMode;
        entry.MacroName = Name.Trim();
        entry.BindKey = BindKey.Trim();
        entry.LoopMode = LoopMode;
        entry.TriggerOnce = TriggerOnce;
        entry.ScheduleMode = ScheduleMode;
        entry.ScheduleInterval = Math.Max(1, ScheduleInterval ?? 5);
        entry.ScheduleUnit = ScheduleUnit;
        entry.ScheduleTime = ScheduleTime.Trim();
        entry.WatchPath = WatchPath.Trim();
        entry.WatchChange = WatchChange;
        entry.WatchFilter = WatchFilter.Trim().Length == 0 ? "*" : WatchFilter.Trim();
        entry.WatchSubfolders = WatchSubfolders;
        entry.ProcessName = ProcessName.Trim();
        entry.ProcessChange = ProcessChange;
        entry.WindowTarget = WindowTarget;
        entry.WindowChange = WindowChange;
        entry.IdleSeconds = Math.Max(1, IdleSeconds ?? 60);
        entry.ColorMatch = ColorMatch;
        entry.ColorPositionX = ParseCoordinate(ColorPositionX);
        entry.ColorPositionY = ParseCoordinate(ColorPositionY);
        entry.UseCursorPosition = UseCursorPosition;
        entry.HexColor = HexColor;
        entry.ColorTolerance = ParseCoordinate(ColorTolerance);
        entry.Abbreviation = HotstringText;
        entry.HotstringEndChars = HotstringEndChars;
        entry.CaseSensitive = HotstringCaseSensitive;
        entry.MatchInsideWord = HotstringInsideWord;
        entry.RequireEndChar = HotstringRequireEnd;
        entry.EraseTyped = HotstringEraseTyped;
        entry.KeepEndChar = HotstringKeepEnd;
    }

    /// <summary>Brings the trigger form onto the entry selected in the list.</summary>
    private void LoadTriggerFields(TriggerEntry? entry)
    {
        _loading = true;
        try
        {
            TriggerMode = entry?.Kind ?? TriggerKind.Key;
            BindKey = entry?.BindKey ?? string.Empty;
            LoopMode = entry?.LoopMode ?? MacroLoop.Toggle;
            TriggerOnce = entry?.TriggerOnce ?? false;
            ScheduleMode = entry?.ScheduleMode ?? ScheduleMode.Interval;
            ScheduleInterval = entry?.ScheduleInterval ?? 5;
            ScheduleUnit = entry?.ScheduleUnit ?? ScheduleUnit.Seconds;
            ScheduleTime = entry?.ScheduleTime ?? "08:00";
            WatchPath = entry?.WatchPath ?? string.Empty;
            WatchChange = entry?.WatchChange ?? FileChangeKind.Any;
            WatchFilter = entry?.WatchFilter ?? "*";
            WatchSubfolders = entry?.WatchSubfolders ?? false;
            ProcessName = entry?.ProcessName ?? string.Empty;
            ProcessChange = entry?.ProcessChange ?? ProcessChangeKind.Started;
            WindowTarget = entry?.WindowTarget ?? WindowRule.Empty;
            WindowChange = entry?.WindowChange ?? WindowChangeKind.Appeared;
            IdleSeconds = entry?.IdleSeconds ?? 60;
            ColorMatch = entry?.ColorMatch ?? ColorMatchCondition.ColorMatches;
            ColorPositionX = (entry?.ColorPositionX ?? 0).ToString(CultureInfo.InvariantCulture);
            ColorPositionY = (entry?.ColorPositionY ?? 0).ToString(CultureInfo.InvariantCulture);
            UseCursorPosition = entry?.UseCursorPosition ?? false;
            HexColor = entry?.HexColor ?? "#000000";
            ColorTolerance = (entry?.ColorTolerance ?? 5).ToString(CultureInfo.InvariantCulture);
            HotstringText = entry?.Abbreviation ?? string.Empty;
            HotstringEndChars = entry?.HotstringEndChars ?? HotstringCharacters.DefaultEndChars;
            HotstringCaseSensitive = entry?.CaseSensitive ?? false;
            HotstringInsideWord = entry?.MatchInsideWord ?? false;
            HotstringRequireEnd = entry?.RequireEndChar ?? true;
            HotstringEraseTyped = entry?.EraseTyped ?? true;
            HotstringKeepEnd = entry?.KeepEndChar ?? true;
        }
        finally
        {
            _loading = false;
        }
    }

    public string RecordLabel => IsRecording ? Strings.Get("Editor.StopRecording") : Strings.Get("Editor.Record");

    /// <summary>Hint under the record button, so the stop shortcuts stay discoverable.</summary>
    public string RecordStatusText => IsRecording
        ? Strings.Format("Editor.RecordActive", RecordedEvents)
        : Strings.Get("Editor.RecordIdle");

    /// <summary>True while the current capture only takes keys, not a mouse button or a pad button.</summary>
    internal bool CaptureKeyboardOnly => _captureKeyboardOnly;

    /// <summary>True while a single key is taken rather than a combination.</summary>
    internal bool CaptureSingleKey => _captureSingleKey;

    /// <summary>
    /// Starts a capture for the inspector's key field. The key is handed to <paramref name="deliver"/>
    /// instead of the trigger, and only the keyboard is listened to: a key field cannot hold a
    /// mouse button. A combination takes the modifiers with the key, the way a trigger does.
    /// </summary>
    internal bool BeginKeyCapture(Action<string> deliver, bool combination, bool keyboardOnly = true)
    {
        if (IsRecording)
        {
            return false;
        }

        _keyCaptureTarget = deliver;
        _captureKeyboardOnly = keyboardOnly;
        _captureSingleKey = !combination;
        IsCapturingKey = true;
        return true;
    }

    /// <summary>Records the key pressed while the editor is waiting for a binding.</summary>
    public void CaptureKey(string keyName)
    {
        if (!IsCapturingKey)
        {
            return;
        }

        var deliver = _keyCaptureTarget;
        StopCapturingKey();
        if (deliver is not null)
        {
            deliver(keyName);
            return;
        }

        BindKey = keyName;
    }

    /// <summary>Ends a capture and forgets whatever it was going to write into.</summary>
    private void StopCapturingKey()
    {
        IsCapturingKey = false;
        IsCapturingInterfaceHotkey = false;
        _keyCaptureTarget = null;
        _captureKeyboardOnly = false;
        _captureSingleKey = false;
    }

    [RelayCommand]
    private void AddAction(MacroAction? action)
    {
        if (action is null || !action.IsEnabled)
        {
            return;
        }

        switch (action.Key)
        {
            case AddStepPaletteKey:
                NewStepCommand.Execute(null);
                break;
            case "edit":
                EditSelectedCommand.Execute(null);
                break;
            case "toggleEnabled":
                ToggleStepEnabledCommand.Execute(null);
                break;
            case "undo":
                UndoCommand.Execute(null);
                break;
            case "redo":
                RedoCommand.Execute(null);
                break;
            case "copy":
                CopyRequested?.Invoke();
                break;
            case "cut":
                CutRequested?.Invoke();
                break;
            case "paste":
                PasteRequested?.Invoke();
                break;
            case "delete":
                DeleteSelectedCommand.Execute(null);
                break;
            case "group":
                GroupSelectedCommand.Execute(null);
                break;
            case "liftToMacro":
                LiftSelectedCommand.Execute(null);
                break;
            case "moveUp":
                MoveSelectedUpCommand.Execute(null);
                break;
            case "moveDown":
                MoveSelectedDownCommand.Execute(null);
                break;
            case "clear":
                ClearStepsCommand.Execute(null);
                break;
            case "adjustDelays":
                AdjustDelaysRequested?.Invoke();
                break;
            case "runMacro":
                RunMacroRequested?.Invoke();
                break;
        }
    }

    /// <summary>
    /// Asks the window to open the "Add Action" dialog. The new step goes below whatever is
    /// picked, inside the block it sits in, which is where a step added while reading a block
    /// is wanted: with nothing picked it goes at the end of the macro.
    /// </summary>
    [RelayCommand]
    private void NewStep()
    {
        _insertAt = InsertionPoint();
        AddStepRequested?.Invoke();
    }

    /// <summary>
    /// Asks the window to open the dialog for a step to go at the end of one of a block's lists,
    /// which is what the "add" button on a head row does.
    /// </summary>
    [RelayCommand]
    private void AddInside(StepRow? row)
    {
        if (row?.List is not { } parameter)
        {
            return;
        }

        _insertAt = new StepInsertionTarget(parameter.Steps, parameter.Steps.Count,
            row.Step, parameter);
        AddStepRequested?.Invoke();
    }

    /// <summary>
    /// What the "Add Action" dialog may offer for the step being built: a switch's branch list
    /// only takes branches, everything else takes anything. Read by the window when it opens the
    /// dialog.
    /// </summary>
    public IReadOnlyList<ActionDefinition>? InsertChoices => _insertAt is
    { Owner: { } owner, Parameter: { } parameter }
        ? ChoicesFor(owner, parameter)
        : null;

    private static IReadOnlyList<ActionDefinition>? ChoicesFor(MacroStep owner, StepParameter parameter)
    {
        var definition = owner.Definition?.Parameters
            .FirstOrDefault(candidate => candidate.Name == parameter.Name);

        return definition is { ChildKeys.Count: > 0 } ? ActionCatalog.ForKeys(definition.ChildKeys) : null;
    }

    /// <summary>
    /// Checks a proposed insertion without moving the editor's pending target. Drag feedback uses
    /// this before the pointer is released, so an illegal seam never promises a drop it will refuse.
    /// </summary>
    public string? PlacementRefusalAt(int slot, MacroStep? into, IEnumerable<MacroStep> steps)
        => PlacementRefusal(TargetAt(slot, into), steps);

    /// <summary>Checks the current selection at a proposed drop seam.</summary>
    public string? MoveSelectionRefusalAt(int slot, MacroStep? into)
    {
        var target = TargetAt(slot, into);
        var moving = Ordered(_selection);
        return target is not null && moving.Any(step => IsInside(target.List, step))
            ? Strings.Get("Editor.PlaceIntoItself")
            : PlacementRefusal(target, moving);
    }

    /// <summary>Shows the reason a placement was refused without changing the macro.</summary>
    public void ReportPlacementRefusal(string reason) => PlacementNotice = reason;

    /// <summary>Folds a block open or shut, so a long macro can be read at the level wanted.</summary>
    [RelayCommand]
    private void ToggleFold(StepRow? row)
    {
        if (row is null)
        {
            return;
        }

        SetExpanded(row.Step, !row.Step.IsExpanded);
    }

    /// <summary>
    /// Shows or hides the steps inside a block. Folding a block the selection was inside takes
    /// the selection with it — onto the block itself — because steps that are not on screen
    /// must not be the ones a delete or a cut acts on.
    /// </summary>
    public void SetExpanded(MacroStep step, bool expanded)
    {
        step.IsExpanded = expanded;
        RebuildRows();

        var visible = Rows.Where(row => row.IsStep).Select(row => row.Step).ToHashSet();
        if (_selection.Any(selected => !visible.Contains(selected)))
        {
            var kept = _selection.Where(visible.Contains).ToList();
            _selection.Clear();
            _selection.AddRange(kept.Count > 0 ? kept : [step]);
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedSteps));
            RefreshPaletteState();
        }

        SelectionRefreshRequested?.Invoke();
    }

    /// <summary>
    /// Opens every block a step sits inside, so a step somebody asked to be shown is really on the
    /// list rather than hidden inside a folded block. Answers false when the step is not part of
    /// this macro, which is the case for one that has since been replaced.
    /// </summary>
    public bool Reveal(MacroStep step)
    {
        if (!Opens(Steps, step))
        {
            return false;
        }

        RebuildRows();
        return true;
    }

    /// <summary>
    /// Opens the blocks on the way down to a step. Whatever a step is nested in has to be unfolded
    /// for it to have a row at all, so the answer is collected on the way back up.
    /// </summary>
    private static bool Opens(IEnumerable<MacroStep> steps, MacroStep wanted)
    {
        foreach (var candidate in steps)
        {
            if (ReferenceEquals(candidate, wanted))
            {
                return true;
            }

            foreach (var list in candidate.StepLists)
            {
                if (Opens(list.Steps, wanted))
                {
                    candidate.IsExpanded = true;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Asks the window to open the run window for the steps as they are now, which is what the button
    /// beside 「保存」 does, and it loads the steps there rather than running them. It is the only way
    /// into a run from this window: the palette had a second entry for it and F5 a third, and all
    /// three asked for the same window.
    /// </summary>
    [RelayCommand]
    private void Run() => RunRequested?.Invoke();

    /// <summary>Asks the window to open the edit dialog for the selected step.</summary>
    [RelayCommand]
    private void EditSelected()
    {
        if (_selection.Count == 1)
        {
            EditStepRequested?.Invoke(_selection[0]);
        }
    }

    /// <summary>
    /// Turns the selected steps on or off. When any of them is already off they all come
    /// back on, so one press always changes something.
    /// </summary>
    [RelayCommand]
    private void ToggleStepEnabled()
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0)
        {
            return;
        }

        PushUndo();

        var enable = selected.Any(step => !step.Meta.IsEnabled);
        foreach (var step in selected)
        {
            step.Meta = step.Meta.WithEnabled(enable);
        }

        NotifyStepsChanged();
    }

    /// <summary>
    /// Wraps the selected steps in a <c>control.sequence</c> step, which is the macro
    /// editor's way of grouping: a sequence runs its children in order, so grouping and
    /// running in order are the same thing.
    /// </summary>
    [RelayCommand]
    private void GroupSelected()
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0 || ListOf(selected[0]) is not { } list)
        {
            return;
        }

        // Grouping wraps steps that sit side by side. Steps picked out of different blocks have
        // no one place to be wrapped into, so the block the first of them is in is the one that
        // gets the group.
        var grouped = selected.Where(step => ReferenceEquals(ListOf(step), list)).ToList();
        var index = list.IndexOf(grouped[0]);
        var group = new MacroStep
        {
            Type = "control.sequence",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter
                {
                    Name = "steps",
                    Kind = ActionParameterKind.Steps,
                    Steps = [.. grouped],
                },
            ],
        };

        if (PlacementRefusal(TargetForList(list, index), [group]) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();
        foreach (var step in grouped)
        {
            list.Remove(step);
        }

        list.Insert(Math.Clamp(index, 0, list.Count), group);
        StepIds.Settle(Steps);
        RestoreSelection([group]);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Moves the selected steps into a macro of their own and leaves a call to it in their place.
    /// What the stretch reads from this macro becomes the new macro's arguments and what it writes
    /// becomes its results, so the steps around the call go on reading the names they always did.
    /// </summary>
    [RelayCommand]
    private void LiftSelected()
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0 || ListOf(selected[0]) is not { } list)
        {
            return;
        }

        // The stretch is the part of the selection that sits in one list. Steps picked out of
        // several blocks have no one place to come out of, and the call they left behind would
        // have nowhere to go; as with grouping, the block the first of them is in is the one.
        var stretch = selected.Where(step => ReferenceEquals(ListOf(step), list)).ToList();
        if (stretch.Count == 0)
        {
            return;
        }

        var lifted = LiftedMacro(stretch);
        var call = CallTo(lifted);
        if (PlacementRefusal(TargetForList(list, list.IndexOf(stretch[0])), [call]) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();
        var index = list.IndexOf(stretch[0]);
        foreach (var step in stretch)
        {
            list.Remove(step);
        }

        list.Insert(Math.Clamp(index, 0, list.Count), call);
        StepIds.Settle(Steps);

        // The new macro is one of the ones a call may reach from here, so the step just written is
        // checked against it rather than being marked as calling something that is not there.
        _projectMacros = [.. _projectMacros, lifted.Callable];
        RestoreSelection([call]);
        NotifyStepsChanged();
        MacroLifted?.Invoke(lifted);
    }

    /// <summary>
    /// The macro a stretch of steps becomes: named after this one, taking as arguments the values
    /// it read from around itself, and offering back the values it writes.
    /// </summary>
    private MacroItem LiftedMacro(IReadOnlyList<MacroStep> stretch)
    {
        var lifted = new MacroItem { Name = FreeMacroName() };

        // What the stretch makes is what the steps after the call can still be reading, so every
        // name it writes is offered back rather than left behind in the called macro's own table.
        var written = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in stretch)
        {
            step.CollectVariables(written);
        }

        // Shared values and the ones WhaleGenie provides are readable on both sides of a call, so
        // they need no argument; everything else the stretch reads has to be handed in.
        var alwaysVisible = new HashSet<string>(
            VariableCatalog.SystemVariables.Select(variable => variable.Name),
            StringComparer.OrdinalIgnoreCase);
        alwaysVisible.UnionWith(VariableCatalog.Globals.Select(variable => variable.Name));
        alwaysVisible.Add(MacroRunner.CaughtKeyName);
        alwaysVisible.Add(MacroRunner.CaughtDetailName);

        // What each name holds, read from the steps themselves: a value a call handed back as a
        // number stays a number when it becomes an argument, so the call form says what it wants and
        // the checks either side of the call agree about it.
        var types = MacroCheck.TypesOf(stretch, OwnCallable, _projectMacros);

        // A lifted argument is required because the call this very lift writes always passes it. The
        // alternative — declaring it optional — would let a later edit delete the row and leave the
        // macro reading an empty value with nothing said, which is the failure the declaration rules
        // are for.
        var read = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in MacroCheck.Reads(stretch))
        {
            if (!read.Add(name) || written.Contains(name) || alwaysVisible.Contains(name))
            {
                continue;
            }

            lifted.Parameters.Add(new MacroParameter
            {
                Name = name,
                Type = TypeOf(types, name),
                Required = true,
            });
        }

        foreach (var name in written)
        {
            lifted.Results.Add(new MacroResult { Name = name, Type = TypeOf(types, name) });
        }

        foreach (var step in stretch)
        {
            lifted.Steps.Add(step);
        }

        return lifted;
    }

    /// <summary>
    /// What a name was seen to hold, or text when nothing said. Text is the honest answer for a name
    /// whose value nothing declares: a wrong guess here would be a type the call site enforces.
    /// </summary>
    private static ActionReturnType TypeOf(IReadOnlyDictionary<string, ActionReturnType> types,
        string name)
        => types.TryGetValue(name, out var type) ? type : ActionReturnType.Text;

    /// <summary>The call left where the stretch was: it hands in this macro's values and takes back
    /// every name the stretch writes, each under the name it already had.</summary>
    private MacroStep CallTo(MacroItem lifted)
    {
        var parameters = new List<StepParameter>
        {
            new()
            {
                Name = "macro",
                Kind = ActionParameterKind.Macro,
                Value = lifted.Name,
            },
        };

        // What the call passes is the same values the stretch read, so each argument row points at
        // the name the value already had: the lifted steps keep working without a rename anywhere.
        parameters.Add(new StepParameter
        {
            Name = FieldNames.Arguments,
            Kind = ActionParameterKind.Arguments,
            Rows =
            [
                .. lifted.Parameters
                    .Where(argument => argument.Name.Trim().Length > 0)
                    .Select(argument => new StepParameterRow
                    {
                        Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            [FieldNames.Name] = argument.Name.Trim(),
                            [FieldNames.Value] = "$" + argument.Name.Trim(),
                        },
                    }),
            ],
        });

        parameters.Add(new StepParameter
        {
            Name = "returns",
            Kind = ActionParameterKind.Returns,
            Rows =
            [
                .. lifted.Results.Select(result => new StepParameterRow
                {
                    Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [FieldNames.Key] = result.Name,
                        [FieldNames.Name] = result.Name,
                    },
                }),
            ],
        });

        return new MacroStep
        {
            Type = "control.runMacro",
            Id = NewStepId(),
            Meta = StepMeta.New,
            Parameters = parameters,
        };
    }

    /// <summary>A name for the new macro that is not one of the project's own.</summary>
    private string FreeMacroName()
    {
        var stem = Name.Trim();
        var wanted = stem.Length == 0
            ? Strings.Get("Editor.LiftedMacroNameEmpty")
            : Strings.Format("Editor.LiftedMacroName", stem);

        var taken = new HashSet<string>(
            _projectMacros.Select(macro => macro.Name), StringComparer.OrdinalIgnoreCase);
        if (stem.Length > 0)
        {
            taken.Add(stem);
        }

        return VariableNames.Free(wanted, taken);
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (_selection.Count == 0)
        {
            return;
        }

        PushUndo();

        // A block that is dropped takes the steps inside it with it, and a step that is already
        // gone has no list to be taken from, so any order but this one would trip over itself.
        foreach (var step in Ordered(_selection))
        {
            ListOf(step)?.Remove(step);
        }

        _selection.Clear();
        NotifyStepsChanged();
    }

    [RelayCommand]
    private void MoveSelectedUp() => MoveSelection(-1);

    [RelayCommand]
    private void MoveSelectedDown() => MoveSelection(1);

    /// <summary>Asks the window to confirm, because clearing cannot be undone.</summary>
    [RelayCommand]
    private void ClearSteps()
    {
        if (Steps.Count == 0)
        {
            return;
        }

        ClearRequested?.Invoke();
    }

    /// <summary>Removes every step once the window has confirmed the request.</summary>
    public void ClearStepsNow()
    {
        PushUndo();
        Steps.Clear();
        _selection.Clear();
        NotifyStepsChanged();
    }

    /// <summary>
    /// Puts a step the add-action window handed over where the editor is pointing now. That window
    /// stays open while the list is worked on, so where the step goes is read again here rather
    /// than when the window was opened. Returns null once the step is in the list, or what to say
    /// when the place picked does not take that kind of step.
    /// </summary>
    public string? AddStepHere(MacroStep step)
    {
        // A place asked for by name — the "add" button on a block's line — stands until the
        // selection moves: moving it is the user saying "somewhere else".
        _insertAt ??= InsertionPoint();

        if (PlacementRefusalForPending(step) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return refusal;
        }

        AddStep(step);
        return null;
    }

    /// <summary>Checks the step a form is about to add at the target the editor locked for it.</summary>
    internal string? PlacementRefusalForPending(MacroStep step)
        => PlacementRefusal(
            _insertAt ?? InsertionPoint() ?? TargetForList(Steps, Steps.Count),
            [step]);

    /// <summary>Checks an edited step against the list its original occupies.</summary>
    internal string? PlacementRefusalForReplacement(MacroStep original, MacroStep replacement)
        => ListOf(original) is { } list
            ? PlacementRefusal(TargetForList(list, list.IndexOf(original)), [replacement])
            : null;

    /// <summary>Returns the one explanation every insertion and move uses for a rejected step.</summary>
    private static string? PlacementRefusal(
        StepInsertionTarget? target, IEnumerable<MacroStep> steps)
    {
        var destination = target is { Owner: { } owner, Parameter: { } parameter }
            ? owner.ParameterDefinition(parameter)
            : null;
        if (StepPlacement.FirstIssue(destination, steps) is not { } issue)
        {
            return null;
        }

        if (issue.Destination is { ChildKeys.Count: > 0 } restricted)
        {
            return Strings.Format("Editor.PlaceTakesOnly",
                StepPlacement.AllowedSummary(restricted));
        }

        return issue.Step.Type == "control.case"
            ? Strings.Get("Editor.PlaceCaseOnly")
            : Strings.Format("Editor.PlaceUnavailable", issue.Step.DisplayName);
    }

    /// <summary>
    /// Puts a step built by the "Add Action" dialog where the editor said it was to go, which is
    /// the end of the macro unless a step or a block was picked first.
    /// </summary>
    public void AddStep(MacroStep step)
    {
        step.EnsureDeclaredLists();
        var target = _insertAt ?? TargetForList(Steps, Steps.Count);
        if (PlacementRefusal(target, [step]) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();
        if (step.StepLists.Any())
        {
            step.IsExpanded = true;
        }

        var (list, index) = (target.List, target.Index);
        _insertAt = null;
        list.Insert(Math.Clamp(index, 0, list.Count), step);

        StepIds.Settle(Steps);
        ShowInside(list);
        RestoreSelection([step]);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Appends the steps a recording produced. They arrive together, so one undo puts the
    /// list back the way it was before the recording started.
    /// </summary>
    public void AppendSteps(IReadOnlyList<MacroStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        if (PlacementRefusal(TargetForList(Steps, Steps.Count), steps) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();
        foreach (var step in steps)
        {
            Steps.Add(step);
        }

        StepIds.Settle(Steps);
        NotifyStepsChanged();
        MarkDirty();
    }

    /// <summary>Swaps a step for the edited version returned by the dialog.</summary>
    public void ReplaceStep(MacroStep original, MacroStep replacement)
    {
        replacement.EnsureDeclaredLists();
        if (ListOf(original) is not { } list)
        {
            return;
        }

        var index = list.IndexOf(original);
        if (index < 0)
        {
            return;
        }

        if (PlacementRefusal(TargetForList(list, index), [replacement]) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();

        // A block the user had folded open stays folded open while it is being edited.
        replacement.IsExpanded = original.IsExpanded;
        list[index] = replacement;
        RestoreSelection([replacement]);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Drops the given steps in below the selection, which is where a paste is expected
    /// to land. With nothing selected the steps go to the end of the list.
    /// </summary>
    public void PasteSteps(IReadOnlyList<MacroStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        var anchor = Ordered(_selection).LastOrDefault();
        var list = anchor is null ? Steps : ListOf(anchor) ?? Steps;
        var index = anchor is null ? list.Count : list.IndexOf(anchor) + 1;
        var copies = steps.Select(Clone).ToList();
        foreach (var copy in copies)
        {
            copy.EnsureDeclaredLists();
        }

        if (PlacementRefusal(TargetForList(list, index), copies) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        PushUndo();
        for (var offset = 0; offset < copies.Count; offset++)
        {
            list.Insert(Math.Clamp(index + offset, 0, list.Count), copies[offset]);
        }

        // A paste is a copy, and two steps sharing a name share the variable it fills in and the
        // row a run lights up; a name that is free in this macro is kept, so pasting from another
        // macro leaves the steps as they were.
        StepIds.Settle(Steps);
        ShowInside(list);
        RestoreSelection(copies);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Writes the given steps as the JSON array that copy and cut place on the clipboard.
    /// </summary>
    public static string SerializeSteps(IEnumerable<MacroStep> steps)
    {
        var array = new JsonArray();
        foreach (var step in steps)
        {
            array.Add(step.ToJson());
        }

        return array.ToJsonString();
    }

    /// <summary>
    /// Rebuilds steps from clipboard text, or returns <c>null</c> when the text is not a
    /// step array, so pasting an unrelated phrase leaves the list untouched.
    /// </summary>
    public static List<MacroStep>? DeserializeSteps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonArray array)
            {
                return null;
            }

            var steps = new List<MacroStep>();
            foreach (var node in array)
            {
                if (node is JsonObject obj)
                {
                    steps.Add(MacroStep.FromJson(obj));
                }
            }

            return steps.Count > 0 ? steps : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Records what the action list has highlighted, so the palette can react.</summary>
    public void SetSelection(IEnumerable<MacroStep> steps)
    {
        // A step asked for before this one is no longer the step being pointed at.
        _insertAt = null;
        PlacementNotice = string.Empty;
        _selection.Clear();
        _selection.AddRange(steps);

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedSteps));
        RefreshPaletteState();
    }

    /// <summary>
    /// Asks the view to put the highlight back on the rows the selection sits on. A block's title
    /// and its closing line stand for the block itself, so picking one of those moves the
    /// highlight onto the block's own row rather than leaving it on the line that was clicked.
    /// </summary>
    public void RefreshSelection() => SelectionRefreshRequested?.Invoke();

    /// <summary>Fills the editor from an existing macro so it can be edited.</summary>
    public void LoadFrom(MacroItem macro)
    {
        _loading = true;
        try
        {
            Name = macro.Name;
            Description = macro.Description;
            Origin = macro.Origin;
            TriggerEntries.Clear();
            if (macro.Triggers.Count == 0)
            {
                var initial = TriggerEntry.FromMacro(macro);
                WatchTriggerEntry(initial);
                TriggerEntries.Add(initial);
            }
            else
            {
                foreach (var entry in macro.Triggers)
                {
                    var copy = entry.Copy();
                    WatchTriggerEntry(copy);
                    TriggerEntries.Add(copy);
                }
            }

            _selectedTrigger = TriggerEntries[0];
            OnPropertyChanged(nameof(SelectedTrigger));
            OnPropertyChanged(nameof(HasSelectedTrigger));
            LoadTriggerFields(_selectedTrigger);
            PositionCapture = macro.PositionCapture;
            ExclusiveInput = macro.ExclusiveInput;
            RecordMouseMovement = macro.RecordMouseMovement;
            RecordMouseButtons = macro.RecordMouseButtons;
            RecordKeyboardKeys = macro.RecordKeyboardKeys;
            RecordGamepadButtons = macro.RecordGamepadButtons;
            RecordDelays = macro.RecordDelays;
            IgnoreDelaysBelowMs = macro.IgnoreDelaysBelowMs;
            RecordIgnoreInjected = macro.RecordIgnoreInjected;
            DelayScale = macro.DelayScale;

            DeclaredParameters.Clear();
            DeclaredParameters.AddRange(macro.Parameters);
            DeclaredResults.Clear();
            DeclaredResults.AddRange(macro.Results);

            Steps.Clear();
            foreach (var step in macro.Steps)
            {
                Steps.Add(step);
            }

            SubMacros.Clear();
            SubMacros.AddRange(macro.SubMacros);

            // A macro written before steps had names, or one edited by hand outside the program,
            // arrives with none or with the same one twice; both are settled here, once, rather
            // than defended against in every place that reads a step's name.
            StepIds.Settle(Steps);
        }
        finally
        {
            _loading = false;
        }

        NotifyStepsChanged();
        _undo.Clear();
        _redo.Clear();
        IsDirty = false;
    }

    /// <summary>
    /// Gives a new macro its first name without counting that name as an edit. The default is
    /// already usable, so closing a new editor without touching anything still means "nothing to
    /// save"; once a step is added, the default travels with it.
    /// </summary>
    internal void UseDefaultName(IEnumerable<string> existingNames)
    {
        _loading = true;
        try
        {
            Name = MacroNames.NextDefault(existingNames);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Variables the pickers offer: the ones this macro makes for itself, the shared ones the
    /// Variable Center defines, the ones WhaleGenie provides, and the ending every step leaves
    /// behind — each with what a person choosing one needs to know about it.
    /// </summary>
    public IReadOnlyList<VariableChoice> CollectVariables()
        => VariableChoices.For(Steps, DeclaredParameters, DeclaredResults, ReadVariableContent);

    /// <summary>Renames a local name created by this macro's steps and all exact references to it.</summary>
    public bool RenameLocalVariable(string oldName, string newName, out string problem)
    {
        var old = oldName.Trim();
        var next = newName.Trim();
        if (!VariableText.IsName(next))
        {
            problem = Strings.Format("Editor.Problem.InvalidVariableName", next);
            return false;
        }

        if (string.Equals(old, next, StringComparison.Ordinal))
        {
            problem = string.Empty;
            return true;
        }

        if (CollectVariables().Any(choice =>
                string.Equals(choice.Name, next, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(choice.Name, old, StringComparison.OrdinalIgnoreCase)))
        {
            problem = Strings.Format("Editor.Problem.VariableNameTaken", next);
            return false;
        }

        VariableRename.RenameSteps(Steps, old, next);
        NotifyStepsChanged();
        problem = string.Empty;
        return true;
    }

    /// <summary>Applies a project-level global rename to this editor page's steps and declarations.</summary>
    public void RenameGlobalReferences(string oldName, string newName)
    {
        VariableRename.RenameSteps(Steps, oldName, newName, global: true);
        foreach (var parameter in DeclaredParameters)
        {
            parameter.Default = VariableText.Rename(parameter.Default, oldName, newName);
        }

        NotifyStepsChanged();
    }

    private string? ReadVariableContent(string name, VariableScope scope)
    {
        if (scope is VariableScope.Global)
        {
            if (SharedVariables.Current.TryGet(name, out var value))
            {
                return value.AsText();
            }

            if (PersistentVariables.TryGet(name, out var saved))
            {
                return saved;
            }

            var definition = VariableCatalog.FindGlobal(name);
            return definition is null
                ? null
                : VariableDefinition.Preview(definition.Type, definition.DefaultValue);
        }

        if (scope is VariableScope.System)
        {
            return SystemReader?.Read(name);
        }

        return null;
    }

    /// <summary>
    /// A name for a step that is not in the list yet. The name is handed to the dialog while the
    /// step is being written, so the variables it creates can carry it — and it is drawn from the
    /// names this macro already uses, so the name the dialog shows is the name the step will keep.
    /// </summary>
    public string NewStepId() => StepIds.Next(AllIds());

    /// <summary>Every name in use anywhere in this macro, nested steps and conditions included.</summary>
    private HashSet<string> AllIds()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in Steps)
        {
            Collect(step);
        }

        return taken;

        void Collect(MacroStep step)
        {
            if (step.Id.Length > 0)
            {
                taken.Add(step.Id);
            }

            foreach (var parameter in step.Parameters)
            {
                foreach (var child in parameter.Steps)
                {
                    Collect(child);
                }

            }
        }
    }

    /// <summary>
    /// The steps of the macro being written, which is what a step that has to point at another one
    /// picks from.
    /// </summary>
    public IReadOnlyList<ActionParameterOption> StepChoices()
    {
        var choices = new List<ActionParameterOption>();
        foreach (var step in Steps)
        {
            Collect(choices, step);
        }

        return choices;

        static void Collect(List<ActionParameterOption> choices, MacroStep step)
        {
            if (step.Id.Length > 0)
            {
                choices.Add(new ActionParameterOption(step.Id, step.PickerLabel));
            }

            foreach (var parameter in step.Parameters)
            {
                foreach (var child in parameter.Steps)
                {
                    Collect(choices, child);
                }

            }
        }
    }

    private static MacroStep Clone(MacroStep step) => new()
    {
        Type = step.Type,
        Id = step.Id,
        Parameters = step.Parameters.Select(Clone).ToList(),
        Meta = step.Meta,
        IsExpanded = step.IsExpanded,
    };

    /// <summary>
    /// Copies a parameter together with the steps nested inside it,
    /// so duplicating a loop or an if keeps its body.
    /// </summary>
    private static StepParameter Clone(StepParameter parameter) => new()
    {
        Name = parameter.Name,
        Kind = parameter.Kind,
        Value = parameter.Value,
        Jitter = parameter.Jitter,
        Steps = parameter.Steps.Select(Clone).ToList(),
        Rows = parameter.Rows.Select(row => row.Copy()).ToList(),
    };

    /// <summary>
    /// The given steps in the order they are written, nested ones included, so a run of them
    /// keeps its order whichever block each came from.
    /// </summary>
    private List<MacroStep> Ordered(IReadOnlyList<MacroStep> steps)
    {
        var order = DocumentOrder();
        return [.. steps.Where(order.Contains).OrderBy(order.IndexOf)];
    }

    /// <summary>Every step of the macro in the order it is written, deepest steps included.</summary>
    private List<MacroStep> DocumentOrder()
    {
        var ordered = new List<MacroStep>();
        Walk(Steps);
        return ordered;

        void Walk(IEnumerable<MacroStep> steps)
        {
            foreach (var step in steps)
            {
                ordered.Add(step);
                foreach (var list in step.StepLists)
                {
                    Walk(list.Steps);
                }
            }
        }
    }

    /// <summary>Every list of steps in the macro: the top level first, then the ones in blocks.</summary>
    private IEnumerable<IList<MacroStep>> AllLists()
    {
        yield return Steps;

        foreach (var list in Descend(Steps))
        {
            yield return list;
        }
    }

    private static IEnumerable<IList<MacroStep>> Descend(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            foreach (var list in step.StepLists)
            {
                yield return list.Steps;

                foreach (var nested in Descend(list.Steps))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>The list of steps a step sits in, or null when the macro no longer holds it.</summary>
    private IList<MacroStep>? ListOf(MacroStep step)
        => AllLists().FirstOrDefault(list => list.Contains(step));

    private bool InTree(MacroStep step) => ListOf(step) is not null;

    /// <summary>
    /// True when a list of steps lives inside the given step, so moving steps into it would put
    /// the step inside itself.
    /// </summary>
    private static bool IsInside(IList<MacroStep> list, MacroStep step)
        => step.StepLists.Any(own => ReferenceEquals(own.Steps, list)
            || own.Steps.Any(child => IsInside(list, child)));

    /// <summary>
    /// Opens every block on the way to a list of steps, so a step put in there is not put in
    /// a block the user cannot see.
    /// </summary>
    private void ShowInside(IList<MacroStep> list) => Open(list, Steps);

    private static bool Open(IList<MacroStep> list, IEnumerable<MacroStep> from)
    {
        foreach (var step in from)
        {
            foreach (var own in step.StepLists)
            {
                if (ReferenceEquals(own.Steps, list) || Open(list, own.Steps))
                {
                    step.IsExpanded = true;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The list a drop in front of this row lands in, and where in that list. The rows are what
    /// is read rather than the steps, because a head row means "the top of this block's steps"
    /// and a foot row means "the end of them", and neither of those is a step.
    /// </summary>
    private (IList<MacroStep> List, int Index) DropTarget(int slot)
    {
        if (slot < 0 || slot >= Rows.Count)
        {
            return (Steps, Steps.Count);
        }

        var row = Rows[slot];

        // A head row is the top of the steps it names, a foot row is the end of the block it
        // closes, and any other row is the step itself, dropped in front of.
        if (row.Kind is StepRowKind.Head && row.List is { } head)
        {
            return (head.Steps, 0);
        }

        if (row.Kind is StepRowKind.Foot && row.Step.StepLists.LastOrDefault() is { } last)
        {
            return (last.Steps, last.Steps.Count);
        }

        return (row.List?.Steps ?? (IList<MacroStep>)Steps, Math.Max(0, row.Index));
    }

    /// <summary>
    /// Locks the place a dropped action will occupy. The form opens after this, so the list cannot
    /// move the target under it while the person fills the fields.
    /// </summary>
    public void PrepareInsertionAt(int slot, MacroStep? into)
    {
        if (TargetAt(slot, into) is not { } target)
        {
            return;
        }

        if (into is not null)
        {
            into.IsExpanded = true;
        }

        _insertAt = target;
    }

    /// <summary>The exact insertion target at a visible seam, or at the end of a single-list block.</summary>
    private StepInsertionTarget? TargetAt(int slot, MacroStep? into)
    {
        if (into is not null)
        {
            return into.StepLists.FirstOrDefault() is { } parameter
                ? new StepInsertionTarget(parameter.Steps, parameter.Steps.Count, into, parameter)
                : null;
        }

        var (list, index) = DropTarget(slot);
        return TargetForList(list, index);
    }

    /// <summary>The target for a known list, including the owner's declaration that restricts it.</summary>
    private StepInsertionTarget TargetForList(IList<MacroStep> list, int index)
        => OwnerOfList(list) is { } owner
            ? new StepInsertionTarget(list, index, owner.Owner, owner.Parameter)
            : new StepInsertionTarget(list, index, null, null);

    /// <summary>The block and parameter that own a child list, or null for the macro's root list.</summary>
    private (MacroStep Owner, StepParameter Parameter)? OwnerOfList(IList<MacroStep> list)
    {
        foreach (var owner in DocumentOrder())
        {
            foreach (var parameter in owner.StepLists)
            {
                if (ReferenceEquals(parameter.Steps, list))
                {
                    return (owner, parameter);
                }
            }
        }

        return null;
    }

    /// <summary>True when the place currently accepting a drop allows this action.</summary>
    public bool CanPlace(string actionKey)
        => InsertChoices is not { } allowed
           || allowed.Any(definition => definition.Key == actionKey);

    public void ClearPendingInsertion()
        => _insertAt = null;

    /// <summary>Where a step asked for now should go: below the picked one, in its own block.</summary>
    private StepInsertionTarget? InsertionPoint()
    {
        if (Ordered(_selection).LastOrDefault() is not { } anchor || OwnerOf(anchor) is not { } at)
        {
            return null;
        }

        return new StepInsertionTarget(at.Parameter.Steps,
            at.Parameter.Steps.IndexOf(anchor) + 1, at.Owner, at.Parameter);
    }

    /// <summary>
    /// The step that holds the list a step is written in, and the parameter carrying that list.
    /// </summary>
    private (MacroStep Owner, StepParameter Parameter)? OwnerOf(MacroStep step)
    {
        foreach (var owner in DocumentOrder())
        {
            foreach (var parameter in owner.StepLists)
            {
                if (parameter.Steps.Contains(step))
                {
                    return (owner, parameter);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Puts the picked steps inside a block, at the end of the steps it runs. This is what a drop
    /// onto the block itself asks for, as opposed to a drop between two rows.
    /// </summary>
    public void MoveSelectionInto(MacroStep block)
    {
        if (block.StepLists.FirstOrDefault() is not { } parameter || _selection.Count == 0)
        {
            return;
        }

        if (Ordered(_selection).Any(step => IsInside(parameter.Steps, step)))
        {
            ReportPlacementRefusal(Strings.Get("Editor.PlaceIntoItself"));
            return;
        }

        if (PlacementRefusal(
                new StepInsertionTarget(parameter.Steps, parameter.Steps.Count, block, parameter),
                Ordered(_selection)) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        block.IsExpanded = true;
        MoveSelectionToTarget(parameter.Steps, parameter.Steps.Count);
    }

    private void MoveSelection(int offset)
    {
        if (_selection.Count == 0)
        {
            return;
        }

        PushUndo();

        // Capture the steps first: moving them makes the list drop its selection,
        // which clears the selection the loop below needs to keep stable.
        var moved = Ordered(_selection);
        var moving = new HashSet<MacroStep>(moved);

        // Walk from the far end so a block of selected steps keeps its order.
        foreach (var step in offset < 0 ? moved : moved.AsEnumerable().Reverse())
        {
            if (ListOf(step) is not { } list)
            {
                continue;
            }

            var index = list.IndexOf(step);
            var target = index + offset;

            if (index < 0 || target < 0 || target >= list.Count || moving.Contains(list[target]))
            {
                continue;
            }

            list.RemoveAt(index);
            list.Insert(target, step);
        }

        RestoreSelection(moved);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Puts the selected steps where a drag dropped them. <paramref name="slot"/> is the row the
    /// block would land in front of, counted against the list as it stands now. The row is what
    /// says which block the steps land in, so one drag can carry a step out of a loop, into one,
    /// or past the line that closes a block.
    /// </summary>
    public void MoveSelectionTo(int slot)
    {
        var (target, index) = DropTarget(slot);
        MoveSelectionToTarget(target, index);
    }

    /// <summary>
    /// Moves the picked steps into a list of steps at a position. A step is never moved into
    /// itself, and a move that would change nothing leaves no undo entry behind.
    /// </summary>
    private void MoveSelectionToTarget(IList<MacroStep> target, int index)
    {
        var selected = Ordered(_selection);
        if (selected.Count == 0)
        {
            return;
        }

        if (selected.Any(step => IsInside(target, step)))
        {
            ReportPlacementRefusal(Strings.Get("Editor.PlaceIntoItself"));
            return;
        }

        if (PlacementRefusal(TargetForList(target, index), selected) is { } refusal)
        {
            ReportPlacementRefusal(refusal);
            return;
        }

        var moving = new HashSet<MacroStep>(selected);

        // The step the block lands in front of. A step being dragged cannot be that anchor, or
        // the block would be measured against itself and could never move past it.
        MacroStep? anchor = null;
        for (var at = Math.Clamp(index, 0, target.Count); at < target.Count; at++)
        {
            if (!moving.Contains(target[at]))
            {
                anchor = target[at];
                break;
            }
        }

        var reordered = target.Where(step => !moving.Contains(step)).ToList();
        var insertAt = anchor is null ? reordered.Count : reordered.IndexOf(anchor);
        reordered.InsertRange(insertAt, selected);

        // A drag that ends where it started is not a change, so it leaves no undo entry.
        if (reordered.SequenceEqual(target))
        {
            return;
        }

        PushUndo();

        foreach (var step in selected)
        {
            ListOf(step)?.Remove(step);
        }

        target.Clear();
        foreach (var step in reordered)
        {
            target.Add(step);
        }

        ShowInside(target);
        RestoreSelection(selected);
        NotifyStepsChanged();
    }

    /// <summary>
    /// Re-applies a selection after the step list dropped it, and asks the view to
    /// highlight the same rows again so repeated moves do not need another click.
    /// </summary>
    private void RestoreSelection(IEnumerable<MacroStep> steps)
    {
        _selection.Clear();
        _selection.AddRange(steps.Where(InTree));

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedSteps));
        RefreshPaletteState();
    }

    /// <summary>
    /// Rebuilds the rows the list shows and puts the selection back on them, in that order: the
    /// view can only highlight a row that is there.
    /// </summary>
    internal void NotifyStepsChanged()
    {
        PlacementNotice = string.Empty;
        RebuildRows();
        OnPropertyChanged(nameof(HasSteps));
        RefreshPaletteState();
        RefreshVariableCenter();
        SelectionRefreshRequested?.Invoke();
    }

    /// <summary>
    /// Lays the steps out as rows, opening the blocks that are not folded away. Every row is
    /// rebuilt rather than patched, so what the list shows can never drift from the steps.
    /// </summary>
    private void RebuildRows()
    {
        RefreshProblems();

        IsRebuildingRows = true;
        try
        {
            Rows.Clear();
            AddRows(Steps, null, 0);
        }
        finally
        {
            IsRebuildingRows = false;
        }
    }

    /// <summary>
    /// Runs the design-time check over the steps as they stand and puts the answers where the
    /// rows and the line above the list can read them.
    /// </summary>
    private void RefreshProblems()
    {
        Problems = MacroCheck.Inspect(Steps, OwnCallable, _projectMacros, _inputShared, _browserShared);
        DeclarationProblems = MacroCheck.DeclarationProblems(OwnCallable);
        CallImpactSummary = MacroCheck.CallImpact(OwnCallable, _projectMacros);
        _problemByStep = Problems
            .GroupBy(problem => problem.Step)
            .ToDictionary(group => group.Key,
                group => string.Join("\n", group.Select(problem => problem.Message).Distinct()));

        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(HasStepProblems));
        OnPropertyChanged(nameof(DeclarationProblems));
        OnPropertyChanged(nameof(HasDeclarationProblems));
        OnPropertyChanged(nameof(HasBlockingDeclarationProblems));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CallImpactSummary));
        OnPropertyChanged(nameof(HasCallImpact));
        OnPropertyChanged(nameof(ProblemSummary));
        OnPropertyChanged(nameof(DeclarationProblemSummary));
    }

    /// <summary>Checks the declarations again after the macro interface changed a name or a kind.</summary>
    internal void RefreshDeclarations()
    {
        // MarkDirty raises Changed even when the macro was already unsaved. Without that, a second
        // declaration edit would never reach the recovery writer: it listens for changes that arrive
        // while the macro is already dirty, and the dirty flag itself does not move again.
        MarkDirty();
        RefreshProblems();
    }
    private void AddRows(IList<MacroStep> steps, StepParameter? owner, int depth)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            step.EnsureDeclaredLists();
            Rows.Add(new StepRow
            {
                Kind = StepRowKind.Step,
                Step = step,
                List = owner,
                Index = index,
                RowIndex = Rows.Count,
                Depth = depth,
                Problem = _problemByStep.GetValueOrDefault(step) ?? string.Empty,
            });

            var lists = step.StepLists.ToList();
            if (lists.Count == 0 || !step.IsExpanded)
            {
                continue;
            }

            foreach (var list in lists)
            {
                Rows.Add(new StepRow
                {
                    Kind = StepRowKind.Head,
                    Step = step,
                    List = list,
                    RowIndex = Rows.Count,
                    Depth = depth + 1,
                });

                AddRows(list.Steps, list, depth + 2);
            }

            Rows.Add(new StepRow
            {
                Kind = StepRowKind.Foot,
                Step = step,
                RowIndex = Rows.Count,
                Depth = depth,
            });
        }
    }

    /// <summary>True while the step list can be rolled back.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True while an undone change can be put back.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Remembers the list as it is now, so the next change can be rolled back.</summary>
    private void PushUndo()
    {
        _undo.Add(Capture());
        if (_undo.Count > HistoryLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        RefreshPaletteState();
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(Capture());
        Restore(entry);
        RefreshPaletteState();
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(Capture());
        Restore(entry);
        RefreshPaletteState();
    }

    private HistoryEntry Capture() => new(
        [.. Steps.Select(Clone)],
        [.. _selection.Select(step => DocumentOrder().IndexOf(step)).Where(index => index >= 0)],
        DelayScale);

    private void Restore(HistoryEntry entry)
    {
        DelayScale = entry.DelayScale;
        Steps.Clear();
        foreach (var step in entry.Steps)
        {
            Steps.Add(Clone(step));
        }

        var written = DocumentOrder();
        var restored = entry.Selection
            .Where(index => index < written.Count)
            .Select(index => written[index])
            .ToList();

        RestoreSelection(restored);
        NotifyStepsChanged();
    }

    /// <summary>Greys out palette entries that have nothing to act on.</summary>
    private void RefreshPaletteState()
    {
        foreach (var entry in ActionPalette)
        {
            var enabled = entry.IsAvailable;

            if (enabled && entry.NeedsSelection)
            {
                enabled = _selection.Count > 0;
            }

            if (enabled && entry.NeedsSingleSelection)
            {
                enabled = _selection.Count == 1;
            }

            if (enabled && entry.NeedsSteps)
            {
                enabled = Steps.Count > 0;
            }

            if (enabled && entry.NeedsUndo)
            {
                enabled = CanUndo;
            }

            if (enabled && entry.NeedsRedo)
            {
                enabled = CanRedo;
            }

            entry.IsEnabled = enabled;
        }
    }

    [RelayCommand]
    private void SetKey()
    {
        // The button is disabled while recording; this guards the keyboard path too.
        if (IsRecording)
        {
            return;
        }

        // The trigger's capture takes mouse and pad buttons as well as keys.
        StopCapturingKey();
        IsCapturingKey = true;
    }

    [RelayCommand]
    private void ToggleRecord()
    {
        if (IsRecording)
        {
            StopRecording();
            return;
        }

        // A pending bind-key capture would swallow the shortcut and the first recorded keystroke.
        StopCapturingKey();
        IsRecording = true;
    }

    /// <summary>Ends an active recording without closing the editor.</summary>
    public void StopRecording() => IsRecording = false;

    /// <summary>
    /// Tells the window to put the input hook up or take it down, so the recording is tied to
    /// the button and the shortcut rather than to either of them alone.
    /// </summary>
    partial void OnIsRecordingChanged(bool value)
    {
        RecordedEvents = 0;
        RecordingChanged?.Invoke(value);
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanSave)
        {
            SaveBlocked?.Invoke();
            return;
        }

        StopRecording();
        CloseRequested?.Invoke(BuildMacro());
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Highlighting rows in the list, and starting or stopping a recording, are not edits
        // of the macro itself, and neither is what the design-time check made of it.
        if (!_loading && e.PropertyName is not (nameof(IsDirty) or nameof(HasSelection)
                or nameof(SelectedSteps) or nameof(IsRecording) or nameof(RecordedEvents)
                or nameof(Problems) or nameof(HasProblems) or nameof(HasStepProblems)
                or nameof(DeclarationProblems) or nameof(HasDeclarationProblems)
                or nameof(SelectedTrigger) or nameof(HasSelectedTrigger)
                or nameof(ShowsTriggerSettings) or nameof(ShowsLoopMode) or nameof(ShowsTriggerOnce)
                or nameof(TriggerEntries) or nameof(TriggerKindOptions) or nameof(TriggerKindChoices)
                or nameof(TriggerProblemSummary) or nameof(HasTriggerProblem)
                or nameof(HotstringEndKeys) or nameof(IsCapturingHotstringEnd)
                or nameof(HotstringEndCaptureStatus) or nameof(HotstringEndCaptureButton)
                or nameof(IsCapturingInterfaceHotkey) or nameof(InterfaceToggleHotkey)
                or nameof(InterfaceHotkeyProblem)
                or nameof(InterfaceHotkeyButton) or nameof(InterfaceToggleHotkeyDisplay)
                or nameof(HasBlockingDeclarationProblems) or nameof(CanSave)
                or nameof(CallImpactSummary) or nameof(HasCallImpact)
                or nameof(ProblemSummary) or nameof(DeclarationProblemSummary)
                or nameof(NameProblem) or nameof(HasNameProblem)))
        {
            MarkDirty();
        }

        // A window trigger lists the windows its rule recognises right now, so a rule written a
        // little too wide is seen while it is being written. Only while the trigger is a window
        // trigger: a rule nothing reads has no windows to list, and a list of them would say the
        // macro does something it does not.
        if (e.PropertyName is nameof(WindowTarget) or nameof(TriggerMode))
        {
            var asking = IsWindowRuleTrigger;
            WindowPreview.Show(asking);
            if (asking)
            {
                WindowPreview.Follow(WindowRuleNow);
            }
        }
    }

    /// <summary>
    /// Raised for every change to the macro being written, where <see cref="IsDirty"/> only says
    /// whether there is any unsaved work at all: the snapshot has to hear about the changes that
    /// arrive while the macro is already unsaved, and the dirty flag does not move for those.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Notes that the macro being written has moved on.</summary>
    private void MarkDirty()
    {
        IsDirty = true;
        RefreshVariableCenter();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshVariableCenter()
        => VariableCenter.Refresh(CollectVariables());

    /// <summary>Refreshes what global declaration changes mean to this macro without marking it edited.</summary>
    public void RefreshSharedVariables()
    {
        RefreshVariableCenter();
        RefreshProblems();
    }

    /// <summary>
    /// Renaming a macro changes which calls point at it, so the check has to be run again —
    /// including when the new name is the one a self-call was waiting for.
    /// </summary>
    partial void OnNameChanged(string value)
    {
        UpdateInterfaceGroupFields();
        if (!_loading)
        {
            NotifyStepsChanged();
        }
    }

    /// <summary>
    /// Reads the editor into a macro. Internal rather than private so the window can keep the
    /// work in the recovery snapshot while it is still being written.
    /// </summary>
    internal MacroItem BuildMacro()
    {
        CommitTriggerFields();
        var macro = new MacroItem
        {
            Name = Name.Trim(),
            Description = Description.Trim(),
            Origin = Origin,
            PositionCapture = PositionCapture,
            ExclusiveInput = ExclusiveInput,
            RecordMouseMovement = RecordMouseMovement,
            RecordMouseButtons = RecordMouseButtons,
            RecordKeyboardKeys = RecordKeyboardKeys,
            RecordGamepadButtons = RecordGamepadButtons,
            RecordDelays = RecordDelays,
            IgnoreDelaysBelowMs = (int)(IgnoreDelaysBelowMs ?? 0),
            RecordIgnoreInjected = RecordIgnoreInjected,
            DelayScale = DelayScale,
        };
        RememberInterfaceHotkey();
        macro.InterfaceLayouts.AddRange(_interfaceLayouts.Values.Select(layout => layout with { }));

        if (SelectedTrigger is { } current)
        {
            // Keep the legacy projections in step for the macro card and old entry guards while
            // the project-wide trigger collection is becoming the only saved source.
            macro.TriggerMode = current.Kind switch
            {
                TriggerKind.Color => MacroTrigger.ColorPixelChanges,
                TriggerKind.Timer => MacroTrigger.Timer,
                TriggerKind.File => MacroTrigger.FileChanges,
                TriggerKind.Process => MacroTrigger.Process,
                TriggerKind.Window or TriggerKind.Interface => MacroTrigger.Window,
                TriggerKind.Idle => MacroTrigger.Idle,
                _ => MacroTrigger.KeystrokesButtonInputs,
            };
            macro.LoopMode = current.LoopMode;
            macro.BindKey = current.BindKey;
            macro.ScheduleMode = current.ScheduleMode;
            macro.ScheduleInterval = current.ScheduleInterval;
            macro.ScheduleUnit = current.ScheduleUnit;
            macro.ScheduleTime = current.ScheduleTime;
            macro.WatchPath = current.WatchPath;
            macro.WatchChange = current.WatchChange;
            macro.WatchFilter = current.WatchFilter;
            macro.WatchSubfolders = current.WatchSubfolders;
            macro.ProcessName = current.ProcessName;
            macro.ProcessChange = current.ProcessChange;
            macro.WindowTarget = current.WindowTarget;
            macro.WindowChange = current.WindowChange;
            macro.IdleSeconds = current.IdleSeconds;
            macro.ColorMatch = current.ColorMatch;
            macro.ColorPositionX = current.ColorPositionX;
            macro.ColorPositionY = current.ColorPositionY;
            macro.UseCursorPosition = current.UseCursorPosition;
            macro.TriggerOnce = current.TriggerOnce;
            macro.HexColor = current.HexColor;
            macro.ColorTolerance = current.ColorTolerance;
        }

        foreach (var entry in TriggerEntries)
        {
            entry.MacroName = macro.Name;
            macro.Triggers.Add(entry.Copy());
        }

        foreach (var step in Steps)
        {
            macro.Steps.Add(step);
        }

        macro.Parameters.AddRange(DeclaredParameters);
        macro.Results.AddRange(DeclaredResults);
        macro.SubMacros.AddRange(SubMacros);

        return macro;
    }

    /// <summary>Reads a numeric parameter typed into a plain input box, falling back to zero.</summary>
    private static int ParseCoordinate(string? value)
        => int.TryParse(value?.Trim(), out var result) ? result : 0;

}
