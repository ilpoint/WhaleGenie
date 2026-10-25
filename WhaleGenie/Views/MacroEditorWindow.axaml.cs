using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MacroEditorWindow : Window
{
    private readonly MacroEditorViewModel _rootViewModel;
    private MacroEditorViewModel _viewModel;
    private readonly ActionPaletteViewModel _palette = new();
    private readonly ObservableCollection<MacroDocumentPageViewModel> _documents = [];
    private readonly Dictionary<MacroEditorViewModel, string> _lastEditorNames = [];
    private IReadOnlyList<PublicMacroEntry> _publicMacros = [];
    private string? _publicMacroLoadError;
    private bool _switchingDocuments;
    private MacroDocumentPageViewModel? _pageBeforeTabClose;
    private PendingSubMacro? _pendingSubMacro;
    private readonly List<MacroItem> _project;

    /// <summary>
    /// Macros the editor made while it was open, which is what a lift leaves behind: they join the
    /// project when the macro that calls them is saved, because until then the call is not saved
    /// either.
    /// </summary>
    private readonly List<MacroItem> _lifted = [];

    /// <summary>The macro being edited, kept apart from the rest of the project so a call it makes
    /// can be told from a call to another macro.</summary>
    private readonly MacroItem? _editing;

    /// <summary>Where a picture taken from the screen is put, so it travels with the package.</summary>
    private readonly string _assetFolder;

    /// <summary>
    /// The layer the trials and runs started from here use, handed in by the window this was
    /// opened from. Null when nobody handed one in, which is how a check opens the editor; the
    /// program's own layer answers for it then, so a picker or a recorder still works.
    /// </summary>
    private readonly IDeviceLayer? _devices;

    /// <summary>The devices to work through: the ones handed in, or the ones the program holds.</summary>
    private IDeviceLayer Layer => _devices ?? SharedDevices.Current;

    /// <summary>
    /// The frames standing around the windows a window trigger recognises right now. The trigger is
    /// the easiest part of a macro to get wrong — a rule written a little too wide fires on a window
    /// nobody meant — and a frame on each window the rule picks out answers "which ones" without
    /// waiting for it to happen.
    /// </summary>
    private readonly ScreenFrames _triggerFrames = new();

    private bool _allowClose;
    private bool _prompting;
    private bool _syncingSelection;
    private bool _closing;
    private bool _mustStayUnsaved;
    private bool _initialBindingsSettled;

    /// <summary>
    /// The run window this editor opened, kept so that pressing 「运行调试」 twice does not leave two
    /// of them behind: the one already open is closed — which stops whatever it was running — and the
    /// same button then opens a fresh one on the steps as they now stand.
    /// </summary>
    private RunWindow? _running;

    private HotstringKeyboardWindow? _hotstringKeyboard;

    /// <summary>Writes the macro being edited out as it changes, while it is not saved.</summary>
    private readonly RecoveryWriter _recovery;

    /// <summary>
    /// Whether this editor put its macro into the snapshot. Only what wrote it takes it back, so
    /// an editor opened and closed without a word cannot throw away a draft the last run left.
    /// </summary>
    private bool _editorInRecovery;

    /// <summary>
    /// The modifier keys held down while a binding is being taken. A trigger can be a combination,
    /// so pressing Ctrl does not end the capture — it may be the front of one. Whether it was is
    /// settled either by the key that finishes the combination or, when nothing follows it, by the
    /// release: a lone modifier is a binding of its own, and its two halves are two keys.
    /// </summary>
    private readonly List<Key> _captureModifiers = [];

    /// <summary>
    /// The keys the binding just taken was made of, still to see their release. A focused button
    /// treats Space and Enter on release as a click — which would arm the capture all over again —
    /// and a combination sends one release per key.
    /// </summary>
    private readonly List<Key> _swallowReleases = [];

    /// <summary>The input hook, up only while a recording is in progress.</summary>
    private IInputRecorder? _recorder;

    /// <summary>
    /// The controller, read while the editor is waiting for a binding. Windows has no word for a
    /// controller button going down, so this is a reading on a clock; what it was holding at the
    /// reading before is kept here, which is the only way a press can be told from a hold.
    /// </summary>
    private readonly GamepadButtonWatch _padHeld = new();

    /// <summary>The reading of the controller while the editor waits, or null when it is not waiting.</summary>
    private DispatcherTimer? _padTimer;

    /// <summary>What the hook has captured so far, in the order it happened.</summary>
    private readonly List<RecordedInput> _captured = [];
    private readonly object _captureLock = new();
    private DispatcherTimer? _recordTimer;

    /// <summary>Clears the "what the last pick caught" line a moment after it is shown.</summary>
    private DispatcherTimer? _pickTimer;

    /// <summary>The step list, kept because the drag handlers need it after construction.</summary>
    private ListBox? _stepList;

    /// <summary>The form field waiting for a key, so its button can go back to "set key".</summary>
    private StepParameterViewModel? _keyCaptureParameter;

    /// <summary>The key-run row whose keys are being recorded, so its button can say "stop".</summary>
    private KeyRowViewModel? _keyRowCapture;

    private MacroInterfaceView? _interfaceView;

    /// <summary>Layer the drag marker is drawn on, over the step list.</summary>
    private Canvas? _dropLayer;
    private Border? _dropMarker;
    private TextBlock? _dropHint;
    private string _dropHintKey = "Editor.DropAtPosition";

    /// <summary>Where the pointer went down, so a click can be told apart from a drag.</summary>
    private Point _dragOrigin;

    /// <summary>The row under the pointer when it went down; null when the press missed the rows.</summary>
    private StepRow? _pressedRow;

    private bool _draggingSteps;

    /// <summary>Row the dragged block would land at, or -1 while there is no drop target.</summary>
    private int _dropSlot = -1;

    /// <summary>Block a dragged step would be put inside, or null while it lands between rows.</summary>
    private MacroStep? _dropInto;

    private double _dropMarkerY;

    /// <summary>How far the drop line is indented, which is what says "inside this block".</summary>
    private double _dropMarkerIndent;

    /// <summary>Whether the seam under the pointer accepts what is being dragged.</summary>
    private bool _dropAllowed = true;

    /// <summary>Why that seam refuses it, empty while the drop is allowed.</summary>
    private string _dropRefusal = string.Empty;

    public MacroEditorWindow()
        : this(null, null, null)
    {
    }

    /// <summary>
    /// Opens the editor, optionally preloaded with an existing macro to edit. The rest of the
    /// project comes along too, so a step can be pointed at another macro by name and a run
    /// here can call it. The package path is where pictures taken from the screen are stored.
    /// </summary>
    public MacroEditorWindow(MacroItem? existing, IReadOnlyList<MacroItem>? project = null,
        string? packagePath = null, IDeviceLayer? devices = null, ProjectTriggers? triggers = null,
        bool callableOnly = false)
    {
        InitializeComponent();

        _project = [.. project ?? (existing is null ? [] : [existing])];
        _editing = existing;
        _assetFolder = ImageAssets.FolderFor(packagePath);
        _devices = devices;
        _rootViewModel = new MacroEditorViewModel();
        _rootViewModel.SystemReader = new SystemVariableReader(Layer);
        _rootViewModel.IsSubMacro = callableOnly;
        _viewModel = _rootViewModel;
        _rootViewModel.Documents = _documents;
        _recovery = new RecoveryWriter(WriteRecovery);

        // The window trigger lists the windows its rule picks out right now, and the frames on the
        // screen are the half a list cannot say: two windows with the same title read the same in a
        // list and are told apart on the screen. Handed over before the macro is loaded, so the rule
        // the file came with is read against the windows there are rather than reported as
        // unreadable for want of a device.
        if (_devices is { } layer)
        {
            _viewModel.Windows = layer.Windows;
        }

        _viewModel.WindowPreview.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WindowPreviewViewModel.Places))
            {
                DrawTriggerFrames();
            }
        };

        if (existing is not null)
        {
            _rootViewModel.LoadFrom(existing);
        }
        else
        {
            _rootViewModel.UseDefaultName(_project.Select(macro => macro.Name));
        }

        BuildDocumentPages();

        _viewModel.LoadProjectTriggerSettings(triggers ?? new ProjectTriggers(), _project);
        if (existing is not null)
        {
            _viewModel.LoadDraftProjectSettings(existing);
        }

        // The other macros a call may point at, so a run-another-macro step that no longer names
        // one is marked while it is being read rather than when the trigger fires.
        RefreshProjectCallables();
        _publicMacros = LoadPublicMacros();

        DataContext = _viewModel;
        Title = Strings.Get("Editor.Title");
        Opened += (_, _) =>
        {
            if (_initialBindingsSettled)
            {
                return;
            }

            _initialBindingsSettled = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_mustStayUnsaved)
                {
                    foreach (var page in _documentPages)
                    {
                        page.Editor.AcceptInitialBindings();
                    }
                }
            }, DispatcherPriority.Background);
        };

        // The form's "set key" button says it is waiting only while a capture is up; a capture
        // ended by the record button or by another binding has to put the label back.
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MacroEditorViewModel.IsCapturingKey)
                && !_viewModel.IsCapturingKey)
            {
                ResetKeyCaptureLabel();
            }
        };

        ActionPalette.DataContext = _palette;
        ActionPalette.SubMacroChosen += reference => SwitchToSubMacro(reference.Macro);
        ActionPalette.AddBlankSubMacroRequested += AddBlankSubMacro;
        ActionPalette.CopyProjectMacroRequested += CopyProjectMacroAsSub;
        ActionPalette.ManagePublicMacrosRequested += ManagePublicMacros;
        RefreshPalette();
        EnsureInterface();
        // Save keeps the editor open and hands the same snapshot to the project list immediately.
        // Closing is a separate action and never has to be inferred from a save.
        _viewModel.CloseRequested += _ =>
        {
            SaveAll(closeAfterSave: false);
        };
        _viewModel.AddStepRequested += OnAddStepRequested;
        _viewModel.EditStepRequested += OnEditStepRequested;
        _viewModel.ClearRequested += OnClearRequested;
        _viewModel.CopyRequested += () => _ = CopySelectionAsync();
        _viewModel.CutRequested += () => _ = CutSelectionAsync();
        _viewModel.PasteRequested += () => _ = PasteAsync();
        _viewModel.RunRequested += OnRunRequested;
        _viewModel.AdjustDelaysRequested += OnAdjustDelaysRequested;
        _viewModel.RunMacroRequested += OnRunMacroRequested;
        _viewModel.SaveBlocked += OnSaveBlocked;
        _viewModel.MacroLifted += OnMacroLifted;
        _viewModel.RecordingChanged += OnRecordingChanged;
        _viewModel.VariableCenter.EditRequested += OnEditVariableRequested;

        // A macro that has not been saved must not be lost to a crash, so it is written out as
        // it changes. This window is the only place it can be kept: it is not in the list yet.
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MacroEditorViewModel.IsCapturingKey))
            {
                if (_viewModel.IsCapturingKey)
                {
                    // A capture starts with nothing held: whatever the last one took is over by now.
                    _captureModifiers.Clear();
                    _swallowReleases.Clear();
                }

                WatchController(_viewModel.IsCapturingKey);
            }

            if (args.PropertyName == nameof(MacroEditorViewModel.IsDirty))
            {
                SyncRecovery();
            }
        };

        // A change that arrives while the macro is already unsaved reaches nothing above: the
        // dirty flag does not move for it, so the draft would be left holding the state it had
        // before that change.
        _viewModel.Changed += (_, _) =>
        {
            SavedNotice.IsVisible = false;
            RefreshDocuments();
            KeepRecovery();
        };

        // If the editor goes away by any route, the hook has to go with it. There is no point
        // telling the user an empty recording was empty once the editor is gone.
        Closed += (_, _) => StopRecorder(announceEmpty: false);
        Closed += (_, _) => _hotstringKeyboard?.Close();

        // And the reading of the controller, which has nothing left to tell once the window is gone.
        Closed += (_, _) => WatchController(false);

        // And so does the recovery draft: once this window is gone, its macro either belongs to
        // the list or was let go, and the next run must not be offered it back.
        Closed += (_, _) => StopRecovery();

        // And the frames it put on the screen: a window that is gone has nothing left to explain
        // them, and they would go on standing over the windows they were drawn around.
        Closed += (_, _) =>
        {
            _closing = true;
            _triggerFrames.Dispose();
        };

        // The keys pressed while a macro is being written or a shortcut bound must not set one off.
        MacroTriggerGate.Enter();
        Closed += (_, _) => MacroTriggerGate.Exit();

        VariableCatalog.GlobalsChanged += OnGlobalsChanged;
        SharedVariables.Current.Changed += OnSharedVariableChanged;
        Closed += (_, _) =>
        {
            VariableCatalog.GlobalsChanged -= OnGlobalsChanged;
            SharedVariables.Current.Changed -= OnSharedVariableChanged;
        };

        var pinButton = this.FindControl<Button>("PinButton");
        if (pinButton is not null)
        {
            pinButton.Click += (_, _) =>
            {
                Topmost = !Topmost;
                UpdateTitleBarButtons();
            };
        }

        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        if (minimizeButton is not null)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        var maximizeButton = this.FindControl<Button>("MaximizeButton");
        if (maximizeButton is not null)
        {
            maximizeButton.Click += (_, _) =>
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            };
        }

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) =>
            {
                // Leave the recording state behind before the editor goes away.
                _closing = true;
                _viewModel.StopRecording();
                Close(null);
            };
        }
        UpdateTitleBarButtons();
        Strings.Current.LanguageChanged += UpdateTitleBarButtons;
        Closed += (_, _) => Strings.Current.LanguageChanged -= UpdateTitleBarButtons;

        var stepList = this.FindControl<ListBox>("StepList");
        if (stepList is not null)
        {
            _stepList = stepList;

            stepList.SelectionChanged += (_, _) =>
            {
                // Rebuilding the rows makes the list let go of its selection for a moment; that
                // is the editor's own doing, not the user unpicking the steps.
                if (_syncingSelection || _viewModel.IsRebuildingRows)
                {
                    return;
                }

                // A block's title and its closing line stand for the block itself, so picking
                // one picks the block. The highlight is then put back on the block's own row,
                // because that is the row everything acts on.
                var rows = stepList.SelectedItems?.OfType<StepRow>().ToList() ?? [];
                _viewModel.SetSelection(rows.Select(row => row.Step).Distinct());
                RefreshPalette();

                // Ctrl+A selects every row at once, including each block's title and closing
                // line. Clearing that selection and adding every row back one by one makes the
                // list follow each addition to the bottom. The block's own row is already picked
                // in that case, so only a title or closing line picked on its own needs the move.
                var pickedSteps = rows
                    .Where(row => row.IsStep)
                    .Select(row => row.Step)
                    .ToHashSet();
                if (rows.Any(row => !row.IsStep && !pickedSteps.Contains(row.Step)))
                {
                    _viewModel.RefreshSelection();
                }
            };

            // The list drops its selection when rows move, so the view model asks for it back.
            _viewModel.SelectionRefreshRequested += () =>
            {
                _syncingSelection = true;
                try
                {
                    // The rows are rebuilt on every change, so the highlights have to be put on
                    // the rows as they are now rather than on the ones that were picked.
                    var wanted = _viewModel.SelectedSteps.ToHashSet();
                    var selection = stepList.Selection;
                    selection.BeginBatchUpdate();
                    try
                    {
                        selection.Clear();
                        for (var index = 0; index < _viewModel.Rows.Count; index++)
                        {
                            if (_viewModel.Rows[index] is { IsStep: true } row && wanted.Contains(row.Step))
                            {
                                selection.Select(index);
                            }
                        }
                    }
                    finally
                    {
                        selection.EndBatchUpdate();
                    }
                }
                finally
                {
                    _syncingSelection = false;
                }
            };

            // Tunnelled: a row handles the press itself, so a drag has to be seen on the way
            // down to the row rather than after it.
            stepList.AddHandler(PointerPressedEvent, OnStepPointerPressed, RoutingStrategies.Tunnel);
            stepList.AddHandler(PointerMovedEvent, OnStepPointerMoved, RoutingStrategies.Tunnel);
            stepList.AddHandler(PointerReleasedEvent, OnStepPointerReleased, RoutingStrategies.Tunnel);
            stepList.AddHandler(PointerCaptureLostEvent, OnStepPointerCaptureLost, RoutingStrategies.Tunnel);
            DragDrop.SetAllowDrop(stepList, true);
            stepList.AddHandler(DragDrop.DragOverEvent, OnActionDragOver);
            stepList.AddHandler(DragDrop.DragLeaveEvent, OnActionDragLeave);
            stepList.AddHandler(DragDrop.DropEvent, OnActionDropped);
        }

        _dropLayer = this.FindControl<Canvas>("DropLayer");
        _dropMarker = this.FindControl<Border>("DropMarker");
        _dropHint = this.FindControl<TextBlock>("DropHintText");

        // Tunnelling so the key reaches us before the focused control consumes it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // The release of the key that was just bound has to go too, or Space re-clicks the
        // very button that armed the capture.
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);

        // A mouse button can be the trigger too, so a press while the editor waits for a
        // binding is taken the same way a key is. Tunnelling keeps it from pressing a control.
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);

        // The wheel is a trigger of its own: a notch up or down can start a macro.
        AddHandler(PointerWheelChangedEvent, OnPreviewPointerWheel, RoutingStrategies.Tunnel);
    }

    private async void OnManageGlobals(object? sender, RoutedEventArgs e)
        => await ManageGlobalsAsync();

    private async Task ManageGlobalsAsync(string? initialName = null)
    {
        var allMacros = ProjectMacrosForVariableEdits();
        var expressionVariables = _viewModel.CollectVariables()
            .Where(choice => choice.Group is VariableGroup.Global or VariableGroup.System)
            .ToList();
        var choices = _viewModel.CollectVariables();
        var edited = await GlobalVariablesWindow.EditAsync(this, expressionVariables,
            name => choices.FirstOrDefault(choice =>
                string.Equals(choice.Name, name, StringComparison.OrdinalIgnoreCase))?.Uses ?? [],
            name => VariableRename.GlobalConflict(allMacros, name),
            name => VariableRename.ScriptUsers(allMacros, name),
            initialName);
        if (edited is not null)
        {
            ApplyGlobalVariableDrafts(edited, allMacros);
        }
    }

    private async void OnEditVariableRequested(VariableChoice choice)
    {
        switch (choice.Origin)
        {
            case VariableOrigin.Global:
                await ManageGlobalsAsync(choice.Name);
                break;
            case VariableOrigin.Local:
                var answer = await TextPromptDialog.ShowAsync(this,
                    Strings.Get("Editor.RenameVariable"),
                    new AskQuestion(AskKind.Text,
                        Strings.Format("Editor.RenameVariableMessage", choice.Name), choice.Name));
                if (answer.Confirmed && answer.Text is { Length: > 0 } next
                    && !_viewModel.RenameLocalVariable(choice.Name, next, out var problem))
                {
                    await ConfirmDialog.ShowAsync(this, Strings.Get("Editor.RenameVariable"),
                        problem, Strings.Get("Common.Ok"), showCancel: false);
                }

                break;
            case VariableOrigin.Parameter:
            case VariableOrigin.Result:
                EnsureInterface();
                this.FindControl<ContentControl>("MacroInterfaceHost")?.BringIntoView();
                break;
        }
    }

    private IReadOnlyList<MacroItem> ProjectMacrosForVariableEdits()
        => [.. _project,
            .. _documentPages.Select(page => page.Editor.BuildMacro())];

    private void ApplyGlobalVariableDrafts(IReadOnlyList<VariableDefinitionDraft> drafts,
        IReadOnlyList<MacroItem> macros)
    {
        var renamed = drafts.Where(draft => !draft.IsNew
                && !string.Equals(draft.OriginalName, draft.Name.Trim(),
                    StringComparison.Ordinal))
            .ToList();
        foreach (var draft in renamed)
        {
            var next = draft.Name.Trim();
            VariableRename.RenameGlobal(macros, draft.OriginalName, next);
            foreach (var page in _documentPages.Select(page => page.Editor).Append(_rootViewModel)
                         .Distinct())
            {
                page.RenameGlobalReferences(draft.OriginalName, next);
            }

            PersistentVariables.Rename(draft.OriginalName, next);
            SharedVariables.Current.Rename(draft.OriginalName, next);
        }

        var kept = drafts.Select(draft => draft.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in VariableCatalog.Globals
                     .Where(variable => !kept.Contains(variable.Name))
                     .Select(variable => variable.Name)
                     .ToList())
        {
            PersistentVariables.Forget(removed);
            SharedVariables.Current.Remove(removed);
        }

        VariableCatalog.Globals.Clear();
        foreach (var draft in drafts)
        {
            var name = draft.Name.Trim();
            var definition = new VariableDefinition
            {
                Name = name,
                Scope = VariableScope.Global,
                Type = draft.Type,
                DefaultValue = draft.DefaultValue.Trim(),
                Description = draft.Description.Trim(),
            };
            VariableCatalog.Globals.Add(definition);

            if (draft.IsNew || draft.CurrentValueDirty)
            {
                var source = draft.IsNew && !draft.CurrentValueDirty
                    ? draft.DefaultValue
                    : draft.CurrentValue;
                SharedVariables.Current.Set(name,
                    VariableValues.Read(draft.Type, source, VariableValues.DesignContext()));
                draft.AcceptCurrent();
            }

            definition.PersistValue = draft.PersistValue;
        }

        VariableCatalog.NotifyGlobalsChanged();
        RefreshSharedVariables();
    }

    private void OnGlobalsChanged() =>
        Dispatcher.UIThread.Post(RefreshSharedVariables);

    private void OnSharedVariableChanged(string _) =>
        Dispatcher.UIThread.Post(RefreshSharedVariables);

    private void RefreshSharedVariables()
    {
        var editors = _documentPages.Select(page => page.Editor)
            .Append(_rootViewModel)
            .Distinct()
            .ToList();
        foreach (var editor in editors)
        {
            editor.RefreshSharedVariables();
        }
    }

    private sealed record PendingSubMacro(MacroEditorViewModel Editor,
        MacroDocumentPageViewModel Page);

    private void BuildDocumentPages()
    {
        _documents.Clear();
        _documentPages.Clear();
        var current = new MacroDocumentPageViewModel(_rootViewModel, isCurrent: true);
        _documents.Add(current);
        _documentPages.Add(current);

        var children = _rootViewModel.SubMacros.ToList();
        _rootViewModel.SubMacros.Clear();
        AddChildPages(children);
        _rootViewModel.Documents = _documents;
        SettleDocumentIds();
        RefreshDocuments();
        SelectDocument(current);
    }

    private readonly List<MacroDocumentPageViewModel> _documentPages = [];

    private void AddChildPages(IEnumerable<MacroItem> children)
    {
        foreach (var child in children)
        {
            var nested = child.SubMacros.ToList();
            var editor = new MacroEditorViewModel { IsSubMacro = true };
            if (_devices is { } devices)
            {
                editor.Windows = devices.Windows;
            }

            editor.Documents = _documents;
            editor.LoadFrom(child);
            editor.SubMacros.Clear();
            WatchChildEditor(editor);
            var page = new MacroDocumentPageViewModel(editor, isCurrent: false, child);
            _documents.Add(page);
            _documentPages.Add(page);
            AddChildPages(nested);
        }
    }

    private void AddBlankSubMacro()
    {
        var editor = new MacroEditorViewModel { IsSubMacro = true };
        editor.Documents = _documents;
        if (_devices is { } devices)
        {
            editor.Windows = devices.Windows;
        }

        editor.Name = UniqueSubMacroName(Strings.Get("Editor.NewSubMacroName"));
        WatchChildEditor(editor);
        AddChildPage(editor, null);
        SettleDocumentIds();
        SelectDocument(_documentPages[^1]);
        _rootViewModel.IsDirty = true;
        RefreshProjectCallables();
        RefreshDocuments();
        KeepRecovery();
    }

    private async void CopyProjectMacroAsSub()
    {
        var candidates = _project
            .Where(macro => !ReferenceEquals(macro, _editing))
            .ToList();
        if (await MenuMacroPickerWindow.ShowAsync(this, candidates,
                Strings.Get("Editor.PickSubMacro"),
                Strings.Get("Editor.PickSubMacroSearch"), enabledOnly: false) is not { } source)
        {
            return;
        }

        var copy = MacroItem.FromJson(source.ToSubMacroJson());
        copy.Origin = null;
        copy.Name = UniqueSubMacroName(source.Name);
        AddExistingSubMacro(copy);
    }

    private void AddExistingSubMacro(MacroItem macro)
    {
        var editor = new MacroEditorViewModel { IsSubMacro = true };
        editor.Documents = _documents;
        if (_devices is { } devices)
        {
            editor.Windows = devices.Windows;
        }

        editor.LoadFrom(macro);
        editor.SubMacros.Clear();
        editor.IsDirty = true;
        WatchChildEditor(editor);
        AddChildPage(editor, macro);
        SettleDocumentIds();
        SelectDocument(_documentPages[^1]);
        _rootViewModel.IsDirty = true;
        RefreshProjectCallables();
        RefreshDocuments();
        KeepRecovery();
    }

    private void AddChildPage(MacroEditorViewModel editor, MacroItem? source)
    {
        var page = new MacroDocumentPageViewModel(editor, isCurrent: false, source);
        editor.Documents = _documents;
        _documents.Add(page);
        _documentPages.Add(page);
    }

    private void SettleDocumentIds()
        => StepIds.SettleAcross(_documentPages.Select(page => page.Editor.Steps));

    private string UniqueSubMacroName(string basis)
    {
        var names = _documentPages
            .Select(page => page.Editor.Name.Trim())
            .Concat(_project.Select(macro => macro.Name.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = basis.Trim();
        if (stem.Length == 0)
        {
            stem = Strings.Get("Editor.NewSubMacroName");
        }

        if (names.Add(stem))
        {
            return stem;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{stem} ({index})";
            if (names.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private void WatchChildEditor(MacroEditorViewModel editor)
    {
        editor.SystemReader = new SystemVariableReader(Layer);
        _lastEditorNames[editor] = editor.Name;
        editor.CloseRequested += _ => SaveAll(closeAfterSave: false);
        editor.AddStepRequested += OnAddStepRequested;
        editor.EditStepRequested += OnEditStepRequested;
        editor.ClearRequested += OnClearRequested;
        editor.CopyRequested += () => _ = CopySelectionAsync();
        editor.CutRequested += () => _ = CutSelectionAsync();
        editor.PasteRequested += () => _ = PasteAsync();
        editor.RunRequested += OnRunRequested;
        editor.AdjustDelaysRequested += OnAdjustDelaysRequested;
        editor.RunMacroRequested += OnRunMacroRequested;
        editor.SaveBlocked += OnSaveBlocked;
        editor.MacroLifted += OnMacroLifted;
        editor.RecordingChanged += OnRecordingChanged;
        editor.VariableCenter.EditRequested += OnEditVariableRequested;
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MacroEditorViewModel.IsDirty)
                && editor.IsDirty)
            {
                _rootViewModel.IsDirty = true;
                KeepRecovery();
            }

            if (args.PropertyName == nameof(MacroEditorViewModel.Name))
            {
                var oldName = _lastEditorNames.GetValueOrDefault(editor, string.Empty);
                var newName = editor.Name.Trim();
                _lastEditorNames[editor] = editor.Name;
                if (oldName.Length > 0 && newName.Length > 0
                    && !string.Equals(oldName, newName, StringComparison.Ordinal))
                {
                    foreach (var page in _documentPages)
                    {
                        RenameCalls(page.Editor.Steps, oldName, newName);
                        page.Editor.NotifyStepsChanged();
                    }
                }

                RefreshProjectCallables();
                RefreshDocuments();
                RefreshPalette();
            }
        };
        editor.Changed += (_, _) =>
        {
            SavedNotice.IsVisible = false;
            _rootViewModel.IsDirty = true;
            RefreshDocuments();
            KeepRecovery();
        };
    }

    private IReadOnlyList<PublicMacroEntry> LoadPublicMacros()
    {
        try
        {
            return PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary);
        }
        catch (Exception error)
        {
            _publicMacroLoadError =
                Strings.Format("PublicMacroManager.LoadFailed", error.Message);
            return [];
        }
    }

    private void RefreshProjectCallables()
    {
        var projectCalls = _project
            .Where(macro => !ReferenceEquals(macro, _editing))
            .Select(macro => macro.Callable)
            .ToList();
        var sharedChildren = _documentPages
            .Where(page => !page.IsCurrent)
            .Select(page => page.Editor.BuildMacro().Callable)
            .ToList();
        var inputShared = OtherMacroSendsInput();
        var browserShared = OtherMacroUsesBrowser();

        foreach (var page in _documentPages)
        {
            var ownName = page.Editor.Name.Trim();
            var calls = projectCalls
                .Concat(sharedChildren)
                .Where(call => !string.Equals(call.Name.Trim(), ownName,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            page.Editor.SetProjectMacros(calls, inputShared, browserShared);
        }
    }

    private void RefreshDocuments()
    {
        foreach (var page in _documentPages)
        {
            page.HasProblem = !page.Editor.CanSave;
        }

        RefreshPalette();
    }

    private void RefreshPalette()
    {
        var called = DirectlyCalledSubMacros(_viewModel);
        _palette.SetSources(_viewModel.InsertChoices, called, _publicMacros);
    }

    private IReadOnlyList<MacroItem> DirectlyCalledSubMacros(MacroEditorViewModel editor)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in editor.Steps)
        {
            Collect(step);
        }

        return [.. _documentPages
            .Where(page => !page.IsCurrent && page.Source is not null)
            .Select(page => page.Editor.BuildMacro())
            .Where(macro => names.Contains(macro.Name))
            .GroupBy(macro => macro.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())];

        void Collect(MacroStep step)
        {
            if (step.Type is "control.runMacro" or "control.startMacro")
            {
                var name = step.Text("macro").Trim();
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }

            foreach (var parameter in step.StepLists)
            {
                foreach (var child in parameter.Steps)
                {
                    Collect(child);
                }
            }
        }
    }

    private void SelectDocument(MacroDocumentPageViewModel page)
    {
        _switchingDocuments = true;
        try
        {
            _viewModel = page.Editor;
            DataContext = page.Editor;
            _viewModel.Documents = _documents;
            var tabs = this.FindControl<ListBox>("DocumentTabs");
            if (tabs is not null)
            {
                tabs.SelectedItem = page;
            }

            EnsureInterface();
            RefreshPalette();
            _viewModel.RefreshSelection();
        }
        finally
        {
            _switchingDocuments = false;
        }
    }

    private void SwitchToSubMacro(MacroItem macro)
    {
        var page = _documentPages.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Source, macro)
            || (!candidate.IsCurrent
                && string.Equals(candidate.Editor.Name, macro.Name,
                    StringComparison.OrdinalIgnoreCase)));
        if (page is not null)
        {
            if (!_documents.Contains(page))
            {
                _documents.Add(page);
            }

            SelectDocument(page);
        }
    }

    private void OnDocumentTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_switchingDocuments
            || sender is not ListBox { SelectedItem: MacroDocumentPageViewModel page })
        {
            return;
        }

        SelectDocument(page);
    }

    private void OnDocumentTabClosePressed(object? sender, PointerPressedEventArgs e)
    {
        _pageBeforeTabClose = _documentPages.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Editor, _viewModel));
    }

    private void OnCloseDocumentTab(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MacroDocumentPageViewModel page } || page.IsCurrent)
        {
            return;
        }

        var active = _pageBeforeTabClose
            ?? _documentPages.FirstOrDefault(candidate => ReferenceEquals(candidate.Editor, _viewModel));
        _pageBeforeTabClose = null;
        var closingActive = ReferenceEquals(active, page);

        _documents.Remove(page);
        SelectDocument(closingActive ? _documentPages[0] : active ?? _documentPages[0]);
        RefreshDocuments();
    }

    private async void ManagePublicMacros()
    {
        _publicMacros = await PublicMacroLibraryWindow.ShowAsync(this, _publicMacros,
            _publicMacroLoadError);
        _publicMacroLoadError = null;
        RefreshPalette();
    }

    private async void OnDeleteSubMacro(object? sender, RoutedEventArgs e)
    {
        var page = _documentPages.FirstOrDefault(candidate =>
            !candidate.IsCurrent && ReferenceEquals(candidate.Editor, _viewModel));
        if (page is null)
        {
            return;
        }

        var callers = _documentPages
            .Where(candidate => Calls(candidate.Editor.Steps, page.Editor.Name))
            .Select(candidate => candidate.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (callers.Count > 0)
        {
            await ConfirmDialog.ShowAsync(this,
                Strings.Get("Editor.SubMacroInUseTitle"),
                Strings.Format("Editor.SubMacroInUse", string.Join(" / ", callers)),
                Strings.Get("Common.Ok"),
                showCancel: false);
            return;
        }

        var choice = await ConfirmDialog.ShowAsync(this,
            Strings.Get("Editor.DeleteSubMacroTitle"),
            Strings.Format("Editor.DeleteSubMacroMessage", page.Editor.Name),
            Strings.Get("Editor.DeleteSubMacro"),
            cancelLabel: Strings.Get("Common.Cancel"));
        if (choice != ConfirmChoice.Primary)
        {
            return;
        }

        _documents.Remove(page);
        _documentPages.Remove(page);
        SelectDocument(_documentPages[0]);
        _rootViewModel.IsDirty = true;
        RefreshProjectCallables();
        RefreshDocuments();
        KeepRecovery();
    }

    private static bool Calls(IEnumerable<MacroStep> steps, string macroName)
    {
        foreach (var step in steps)
        {
            if (step.Type is "control.runMacro" or "control.startMacro"
                && string.Equals(step.Text("macro").Trim(), macroName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (step.StepLists.Any(parameter => Calls(parameter.Steps, macroName)))
            {
                return true;
            }
        }

        return false;
    }

    private static void RenameCalls(IEnumerable<MacroStep> steps, string oldName, string newName)
    {
        foreach (var step in steps)
        {
            if (step.Type is "control.runMacro" or "control.startMacro"
                && step.Parameters.FirstOrDefault(parameter => parameter.Name == "macro")
                    is { } target
                && string.Equals(target.Value?.Trim(), oldName,
                    StringComparison.OrdinalIgnoreCase))
            {
                target.Value = newName;
            }

            foreach (var parameter in step.StepLists)
            {
                RenameCalls(parameter.Steps, oldName, newName);
            }
        }
    }

    private bool SaveAll(bool closeAfterSave)
    {
        RefreshProjectCallables();
        var bad = _documentPages.FirstOrDefault(page => !page.Editor.CanSave);
        if (bad is not null)
        {
            SelectDocument(bad);
            OnSaveBlocked();
            return false;
        }

        var result = BuildRootSnapshot();
        Result = result;
        foreach (var page in _documentPages)
        {
            page.Editor.IsDirty = false;
        }

        _rootViewModel.IsDirty = false;
        _mustStayUnsaved = false;
        StopRecovery();
        Saved?.Invoke(result);
        SavedNotice.IsVisible = true;

        if (closeAfterSave)
        {
            _closing = true;
            _allowClose = true;
            Close(result);
        }

        return true;
    }

    private async void OnOpenHotstringKeyboard(object? sender, RoutedEventArgs e)
    {
        if (_hotstringKeyboard is { } open)
        {
            open.Activate();
            return;
        }

        var keyboard = new HotstringKeyboardWindow(_viewModel.HotstringText);
        _hotstringKeyboard = keyboard;
        try
        {
            if (await keyboard.ShowDialogOver<string?>(this) is { } text)
            {
                _viewModel.HotstringText = text;
            }
        }
        finally
        {
            if (ReferenceEquals(_hotstringKeyboard, keyboard))
            {
                _hotstringKeyboard = null;
            }
        }
    }

    private void OnCaptureHotstringEnd(object? sender, RoutedEventArgs e)
        => _viewModel.BeginHotstringEndCapture();

    private void OnSetInterfaceHotkey(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.BeginKeyCapture(binding =>
            {
                _viewModel.SetInterfaceToggleHotkey(binding);
                ResetKeyCaptureLabel();
            }, combination: true, keyboardOnly: true))
        {
            _viewModel.IsCapturingInterfaceHotkey = true;
        }
    }

    private void OnAddTriggerMenu(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MacroEditorViewModel viewModel
            && sender is MenuItem { Tag: TriggerKind kind })
        {
            viewModel.AddTriggerCommand.Execute(kind);
        }
    }

    /// <summary>
    /// Reads the controller while a binding is being taken, or stops reading it. A check that opened
    /// the editor without handing a layer in has no controller to read and none is asked for.
    /// </summary>
    private void WatchController(bool watching)
    {
        if (!watching || _devices is not { } layer)
        {
            _padTimer?.Stop();
            _padTimer = null;
            _padHeld.Take(GamepadButtons.None);
            return;
        }

        if (_padTimer is not null)
        {
            return;
        }

        // Whatever is held when the capture starts is not a press. The keyboard path only sees keys
        // going down after the button was pressed, and a controller button that is already down is
        // the same "already there" as a key somebody is resting a finger on.
        try
        {
            _padHeld.Take(layer.GamepadReading.Held());
        }
        catch (Exception)
        {
            _padHeld.Take(GamepadButtons.None);
        }

        // The pace the triggers read it at: quicker than a hand presses one button twice, and one
        // reading of a controller is a fraction of the cost of one look at a pixel.
        _padTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _padTimer.Tick += (_, _) => ReadController(layer);
        _padTimer.Start();
    }

    /// <summary>
    /// One reading of the controller, for the timer and for the checks. The first button found going
    /// down is the binding and ends the capture, the way the first key or mouse button does.
    /// </summary>
    internal void LookAtController()
    {
        if (_devices is { } layer)
        {
            ReadController(layer);
        }
    }

    private void ReadController(IDeviceLayer layer)
    {
        if (!_viewModel.IsCapturingKey || _viewModel.CaptureKeyboardOnly)
        {
            return;
        }

        GamepadButtons held;
        try
        {
            held = layer.GamepadReading.Held();
        }
        catch (Exception)
        {
            // A controller that cannot be read is a controller with nothing to say; the next reading
            // tries again rather than the timer dying.
            return;
        }

        foreach (var (button, down) in _padHeld.Changed(held))
        {
            var name = down ? GamepadNames.Binding(button) : string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            // A controller button is one name, so the modifiers of a combination being built do not
            // go on — the same as a binding taken from the wheel or a mouse button.
            _captureModifiers.Clear();
            _viewModel.CaptureKey(name);
            WatchController(false);
            return;
        }
    }

    /// <summary>Takes one notch of the wheel as the trigger while the editor is waiting for one.</summary>
    private void OnPreviewPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MacroEditorViewModel viewModel || !viewModel.IsCapturingKey)
        {
            return;
        }

        // A key field cannot hold a wheel notch; swallow the wheel so it does not scroll the
        // form while the field waits for a key.
        if (viewModel.CaptureKeyboardOnly)
        {
            e.Handled = true;
            return;
        }

        // The pointer reports the tilt the other way round from the input device, so the sign
        // is flipped here and the shared naming decides which way that is.
        var (axis, rotation) = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)
            ? (HookWheelAxis.Horizontal, e.Delta.X < 0 ? 1 : -1)
            : (HookWheelAxis.Vertical, e.Delta.Y >= 0 ? 1 : -1);

        // A wheel binding is one name, so the modifiers of a combination being built do not go on.
        _captureModifiers.Clear();
        viewModel.CaptureKey(KeyNames.WheelName(axis, rotation));
        e.Handled = true;
    }

    /// <summary>Takes a mouse button as the trigger while the editor is waiting for one.</summary>
    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MacroEditorViewModel viewModel || !viewModel.IsCapturingKey)
        {
            return;
        }

        // The capture button itself has to stay clickable: it is how a mouse ends a capture.
        // Every other click is swallowed, so it cannot bind a mouse button or move the focus.
        if (viewModel.CaptureKeyboardOnly)
        {
            if (e.Source is Visual source
                && source.FindAncestorOfType<Button>(true) is { } toggle
                && toggle.Classes.Contains("CaptureToggle"))
            {
                return;
            }

            e.Handled = true;
            return;
        }

        var button = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => HookMouseButton.Left,
            PointerUpdateKind.RightButtonPressed => HookMouseButton.Right,
            PointerUpdateKind.MiddleButtonPressed => HookMouseButton.Middle,
            PointerUpdateKind.XButton1Pressed => HookMouseButton.Back,
            PointerUpdateKind.XButton2Pressed => HookMouseButton.Forward,
            _ => (HookMouseButton?)null,
        };

        if (button is not { } pressed)
        {
            return;
        }

        // A mouse binding is one name, so the modifiers of a combination being built do not go on.
        _captureModifiers.Clear();
        viewModel.CaptureKey(KeyNames.MouseName(pressed));
        e.Handled = true;
    }

    /// <summary>
    /// Lets go of a modifier that nothing followed — which makes it the binding, the way a trigger
    /// bound to Right Shift reads — and swallows the releases that follow a captured press. A
    /// focused button reads Space or Enter on release as a click, which would arm the very capture
    /// the user just finished.
    /// </summary>
    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is MacroEditorViewModel viewModel && viewModel.IsCapturingKey
            && HotkeyNameMapper.IsModifier(e.Key))
        {
            if (_captureModifiers.Remove(e.Key) && _captureModifiers.Count == 0)
            {
                viewModel.CaptureKey(DescribeKey(e.Key));
            }

            // Either it was part of a combination still being built, or it was already down when
            // the capture started; neither is what finishes the binding.
            e.Handled = true;
            return;
        }

        if (_swallowReleases.Remove(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MacroEditorViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsCapturingHotstringEnd)
        {
            if (e.Key == Key.Escape)
            {
                viewModel.StopHotstringEndCapture();
                _captureModifiers.Clear();
                e.Handled = true;
                return;
            }

            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                if (!_captureModifiers.Contains(e.Key))
                {
                    _captureModifiers.Add(e.Key);
                }

                e.Handled = true;
                return;
            }

            var character = HotstringKeyCapture.Character(e.Key, e.KeyModifiers);
            viewModel.CaptureHotstringEnd(character);
            if (character is not null)
            {
                _swallowReleases.Clear();
                _swallowReleases.AddRange(_captureModifiers);
                _swallowReleases.Add(e.Key);
                _captureModifiers.Clear();
            }

            e.Handled = true;
            return;
        }

        if (viewModel.IsCapturingKey)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                // Held back rather than taken: Ctrl, Shift, Alt and Win may be the front of a
                // combination, and which it was is only known once the capture ends.
                if (!_captureModifiers.Contains(e.Key))
                {
                    _captureModifiers.Add(e.Key);
                }

                e.Handled = true;
                return;
            }

            // A single key field takes the key itself; a combination keeps the modifiers held
            // at the moment it was pressed, the way a trigger is written.
            var captured = viewModel.CaptureSingleKey
                ? DescribeKey(e.Key)
                : Chord(e.Key, Held(e));
            viewModel.CaptureKey(captured);
            _swallowReleases.Clear();
            _swallowReleases.AddRange(_captureModifiers);
            _swallowReleases.Add(e.Key);
            _captureModifiers.Clear();
            e.Handled = true;
            return;
        }

        // The screen pickers are reachable from anywhere in the editor, including while a
        // coordinate or the hex colour is being typed, so they answer before the text rule below.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            switch (e.Key)
            {
                case Key.X:
                    e.Handled = true;
                    CaptureCursorPosition(viewModel);
                    return;
                case Key.C:
                    e.Handled = true;
                    CaptureCursorColour(viewModel);
                    return;
            }
        }

        // Never take a shortcut while the user is typing into a field.
        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>(true) is not null)
        {
            return;
        }

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (control)
        {
            switch (e.Key)
            {
                case Key.A:
                    e.Handled = true;
                    SelectAllSteps();
                    return;
                case Key.C:
                    e.Handled = true;
                    _ = CopySelectionAsync();
                    return;
                case Key.X:
                    e.Handled = true;
                    _ = CutSelectionAsync();
                    return;
                case Key.V:
                    e.Handled = true;
                    _ = PasteAsync();
                    return;
                case Key.Z:
                    e.Handled = true;
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        viewModel.RedoCommand.Execute(null);
                    }
                    else
                    {
                        viewModel.UndoCommand.Execute(null);
                    }

                    return;
                case Key.Y:
                    e.Handled = true;
                    viewModel.RedoCommand.Execute(null);
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Insert:
                viewModel.NewStepCommand.Execute(null);
                break;
            case Key.F2:
                viewModel.EditSelectedCommand.Execute(null);
                break;
            case Key.Delete when !control:
                viewModel.DeleteSelectedCommand.Execute(null);
                break;
            case Key.G when control:
                viewModel.GroupSelectedCommand.Execute(null);
                break;
            case Key.E when control:
                viewModel.ToggleStepEnabledCommand.Execute(null);
                break;
            case Key.Up when control:
                viewModel.MoveSelectedUpCommand.Execute(null);
                break;
            case Key.Down when control:
                viewModel.MoveSelectedDownCommand.Execute(null);
                break;
            case Key.Right when control:
                Fold(true);
                break;
            case Key.Left when control:
                Fold(false);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Selects every step the list is showing. The list is asked for one bulk change and the
    /// editor takes the same rows from it, so a long macro is not scrolled one row at a time.
    /// </summary>
    private void SelectAllSteps()
    {
        if (_stepList is null)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            _stepList.SelectAll();
        }
        finally
        {
            _syncingSelection = false;
        }

        _viewModel.SetSelection(_viewModel.Rows
            .Where(row => row.IsStep)
            .Select(row => row.Step)
            .Distinct());
    }

    /// <summary>Puts the selected steps on the clipboard as the editor's step JSON.</summary>
    private async Task CopySelectionAsync()
    {
        if (_viewModel.SelectedSteps.Count == 0)
        {
            return;
        }

        var text = MacroEditorViewModel.SerializeSteps(_viewModel.SelectedSteps);
        await SetClipboardTextAsync(text);
    }

    /// <summary>Copies the selection, then removes it once the clipboard holds a copy.</summary>
    private async Task CutSelectionAsync()
    {
        if (_viewModel.SelectedSteps.Count == 0)
        {
            return;
        }

        var text = MacroEditorViewModel.SerializeSteps(_viewModel.SelectedSteps);
        if (!await SetClipboardTextAsync(text))
        {
            return;
        }

        _viewModel.DeleteSelectedCommand.Execute(null);
    }

    /// <summary>Pastes clipboard steps below the selection, ignoring unrelated text.</summary>
    private async Task PasteAsync()
    {
        string? text;
        try
        {
            text = Clipboard is null ? null : await Clipboard.TryGetTextAsync();
        }
        catch
        {
            // The clipboard may be locked by another process; the paste simply does nothing.
            return;
        }

        if (MacroEditorViewModel.DeserializeSteps(text) is { } steps)
        {
            _viewModel.PasteSteps(steps);
        }
    }

    private async Task<bool> SetClipboardTextAsync(string text)
    {
        if (Clipboard is null)
        {
            return false;
        }

        try
        {
            await Clipboard.SetTextAsync(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Everything held when a key was pressed: what the event itself carries, plus the modifiers
    /// this window saw go down, so a modifier pressed before the capture started still counts.
    /// </summary>
    private KeyModifiers Held(KeyEventArgs e)
    {
        var held = e.KeyModifiers;
        foreach (var modifier in _captureModifiers)
        {
            held |= HotkeyNameMapper.ModifierFlag(modifier);
        }

        return held;
    }

    /// <summary>
    /// The binding a key press makes: the modifiers held at that moment, then the key itself. Each
    /// modifier is written without its side — either Ctrl answers it — which is how a recorded
    /// combination is written too; a lone modifier, where the two sides are two different keys,
    /// keeps its side in <see cref="DescribeKey"/>.
    /// </summary>
    private static string Chord(Key key, KeyModifiers held)
        => HotkeyNameMapper.Chord(key, held);

    private static string DescribeKey(Key key) => HotkeyNameMapper.Name(key);

    /// <summary>Opens the magnifier, which follows the pointer and reads the pixel under it.</summary>
    private async void OnPickColorClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MacroEditorViewModel viewModel)
        {
            return;
        }

        var pick = await ColorPickerWindow.PickAsync(this, Layer);
        Activate();

        if (pick is not { } picked)
        {
            return;
        }

        // The place comes back with the colour rather than being read off the pointer afterwards:
        // what the magnifier was aimed at is where the colour is, and the pointer may have moved.
        viewModel.HexColor = picked.Colour.ToHex();
        viewModel.ColorPositionX = picked.At.X.ToString(CultureInfo.InvariantCulture);
        viewModel.ColorPositionY = picked.At.Y.ToString(CultureInfo.InvariantCulture);
        ShowPickStatus(Strings.Format("Editor.PickedColor", picked.Colour.ToHex()));
    }

    /// <summary>Captures the window a window trigger should watch.</summary>
    private async void OnPickTriggerWindow(object? sender, RoutedEventArgs e)
    {
        if (await WindowRuleWindow.PickAsync(this, Layer, _viewModel.WindowTarget) is { } rule)
        {
            _viewModel.UseWindowTarget(rule);
        }
    }

    /// <summary>Alt + X: the pointer's current screen position becomes the watched position.</summary>
    private void CaptureCursorPosition(MacroEditorViewModel viewModel)
    {
        var position = Layer.Screen.Cursor;
        viewModel.ColorPositionX = position.X.ToString(CultureInfo.InvariantCulture);
        viewModel.ColorPositionY = position.Y.ToString(CultureInfo.InvariantCulture);
        ShowPickStatus(Strings.Format("Editor.PickedPosition", position.X, position.Y));
    }

    /// <summary>
    /// Alt + C: the colour of the pixel under the pointer becomes the watched colour, and the
    /// position it was read from is kept alongside it, so the trigger can be checked straight away.
    /// </summary>
    private void CaptureCursorColour(MacroEditorViewModel viewModel)
    {
        var position = Layer.Screen.Cursor;
        PixelColor colour;
        try
        {
            colour = Layer.Screen.PixelAt(position.X, position.Y);
        }
        catch (Exception)
        {
            // The pointer can sit on a second monitor the primary screen does not cover.
            ShowPickStatus(Strings.Get("Editor.PickFailed"));
            return;
        }

        viewModel.HexColor = colour.ToHex();
        viewModel.ColorPositionX = position.X.ToString(CultureInfo.InvariantCulture);
        viewModel.ColorPositionY = position.Y.ToString(CultureInfo.InvariantCulture);
        ShowPickStatus(Strings.Format("Editor.PickedColor", colour.ToHex()));
    }

    /// <summary>Shows what the last pick caught, clearing itself so the panel stays tidy.</summary>
    private void ShowPickStatus(string message)
    {
        var status = this.FindControl<TextBlock>("PickStatus");
        if (status is null)
        {
            return;
        }

        status.Text = message;

        _pickTimer?.Stop();
        _pickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _pickTimer.Tick += (_, _) =>
        {
            _pickTimer?.Stop();
            _pickTimer = null;
            status.Text = string.Empty;
        };
        _pickTimer.Start();
    }

    /// <summary>Brings up the window a step is written in, on the place the editor is now.</summary>
    private async void OnAddStepRequested()
    {
        var actions = _viewModel.InsertChoices;
        if (actions is { Count: 1 })
        {
            await ShowActionEditor(null, actions, actions[0].Key, null);
            return;
        }

        RefreshPalette();
        ActionPalette.FocusSearch();
    }

    /// <summary>The same, preset on the "run another macro" step.</summary>
    private async void OnRunMacroRequested()
        => await ShowActionEditor(null, null, "control.runMacro", null);

    /// <summary>
    /// Whether any other macro of this project sends keys or clicks. Two macros that do, running at
    /// once, write their input through each other, and neither of them looks wrong on its own — so
    /// the check is handed this rather than left to guess it from the macro being edited.
    /// </summary>
    private bool OtherMacroSendsInput() => OtherMacroReaches(MacroRunner.SendsInput);

    /// <summary>
    /// Whether any other macro of this project uses the shared browser. There is one current page
    /// for the program, so two macros that work in it take turns even when they never send input.
    /// </summary>
    private bool OtherMacroUsesBrowser() => OtherMacroReaches(MacroRunner.UsesBrowser);

    /// <summary>
    /// True when any macro other than the one being edited does the thing, either itself or through
    /// a macro it calls. Calls are followed with the names already being walked, so a circle of
    /// macros is answered rather than followed forever.
    /// </summary>
    private bool OtherMacroReaches(Func<string, bool> direct)
    {
        var macros = _project.ToList();

        bool Walk(MacroStep step, HashSet<string> walking)
        {
            if (direct(step.Type))
            {
                return true;
            }

            foreach (var list in step.StepLists)
            {
                if (list.Steps.Any(child => Walk(child, walking)))
                {
                    return true;
                }
            }

            if (step.Type is not ("control.runMacro" or "control.startMacro"))
            {
                return false;
            }

            var name = step.Text("macro").Trim();
            var called = macros.FirstOrDefault(macro =>
                string.Equals(macro.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (called is null || !walking.Add(called.Name.Trim()))
            {
                return false;
            }

            try
            {
                return called.Steps.Any(child => Walk(child, walking));
            }
            finally
            {
                walking.Remove(called.Name.Trim());
            }
        }

        return macros
            .Where(macro => !ReferenceEquals(macro, _editing))
            .SelectMany(macro => macro.Steps)
            .Any(step => Walk(step, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Takes a macro lifted out of this one: it counts as part of the project from here on, so a
    /// later call can be pointed at it and the design-time check knows it is there.
    /// </summary>
    private void OnMacroLifted(MacroItem macro)
    {
        _lifted.Add(macro);
        _project.Add(macro);
        RefreshProjectCallables();
    }

    /// <summary>Writes one step in the single, draggable action window.</summary>
    private async Task<MacroStep?> ShowActionEditor(MacroStep? editing,
        IReadOnlyList<ActionDefinition>? actions, string? presetKey, string? macroName)
    {
        var editingPage = _viewModel;
        var dialog = new ActionEditorWindow(editing, actions, _viewModel.CollectVariables(),
            MacroCalls(), presetKey, _assetFolder, _viewModel.StepChoices(),
            editing?.Id ?? _viewModel.NewStepId())
        {
            Devices = Layer,
            Macros = BuildLibrary(),
            ShowPicker = false,
            PlaceStep = (existing, step) => existing is null
                ? _viewModel.PlacementRefusalForPending(step)
                : _viewModel.PlacementRefusalForReplacement(existing, step),
        };
        if (macroName is not null)
        {
            dialog.SetMacroCall(macroName);
        }

        var step = await dialog.ShowDialogOver<MacroStep?>(this);

        _viewModel = editingPage;
        DataContext = editingPage;
        if (step is null)
        {
            _pendingSubMacro = null;
            _viewModel.ClearPendingInsertion();
            return null;
        }

        if (editing is not null)
        {
            _viewModel.ReplaceStep(editing, step);
            return step;
        }

        var pending = _pendingSubMacro;
        _pendingSubMacro = null;
        _viewModel = editingPage;
        DataContext = editingPage;
        var refusal = _viewModel.AddStepHere(step);
        if (refusal is null
            && pending is not null
            && step.Type == "control.runMacro"
            && string.Equals(step.Text("macro").Trim(), pending.Editor.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            _documents.Add(pending.Page);
            _documentPages.Add(pending.Page);
            SettleDocumentIds();
            _rootViewModel.IsDirty = true;
        }

        RefreshProjectCallables();
        RefreshDocuments();
        KeepRecovery();
        if (refusal is not null)
        {
            _viewModel.ReportPlacementRefusal(refusal);
        }

        return step;
    }

    /// <summary>Puts the form's capture buttons back to their resting labels.</summary>
    private void ResetKeyCaptureLabel()
    {
        if (_keyCaptureParameter is { } parameter)
        {
            parameter.IsCapturingKey = false;
            _keyCaptureParameter = null;
        }

        if (_keyRowCapture is { } row)
        {
            row.IsCapturing = false;
            _keyRowCapture = null;
        }

    }

    /// <summary>Builds the interface editor the first time it is asked for.</summary>
    private void EnsureInterface()
    {
        var model = new MacroInterfaceViewModel(_viewModel);
        _interfaceView = new MacroInterfaceView { DataContext = model };
        MacroInterfaceHost.Content = _interfaceView;
    }
    /// <summary>Shows the recording controls over the step list.</summary>
    private void OnShowRecording(object? sender, RoutedEventArgs e)
    {
        RecordingPanel.IsVisible = true;
    }

    private void OnCloseRecording(object? sender, RoutedEventArgs e)
        => RecordingPanel.IsVisible = false;

    /// <summary>
    /// Puts the reason a save was refused in front of the person who asked for it. A close prompt
    /// can ask for a save from any page, so the page holding the name and declarations has to come
    /// back rather than leaving the attempt with no visible answer.
    /// </summary>
    private void OnSaveBlocked()
    {
        MacroSettingsDrawer.IsVisible = true;
        RecordingPanel.IsVisible = false;

        // The save may have been asked for by the way out from the notification area. That way out
        // waits for this editor to close, so an editor that has to stay and explain itself must
        // cancel it.
        AppTray.Current?.CalledOff();

        if (!_viewModel.HasNameProblem)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var name = this.FindControl<TextBox>("NameInput");
            name?.Focus();
            name?.SelectAll();
        });
    }

    /// <summary>
    /// Points the list at a step somebody asked to be shown: it is opened out of whatever block it
    /// sits in, scrolled to and selected. This is the answer to "which steps read this variable" —
    /// the dialog that asked keeps standing beside the list, so looking at the step is done beside
    /// filling the field rather than instead of it.
    /// </summary>
    private void LookAtStep(MacroStep step)
    {
        if (!_viewModel.Reveal(step))
        {
            return;
        }

        _viewModel.SetSelection([step]);
        _viewModel.RefreshSelection();

        if (_viewModel.Rows.FirstOrDefault(row => row.IsStep && ReferenceEquals(row.Step, step))
            is { } row)
        {
            _stepList?.ScrollIntoView(row);
        }
    }

    /// <summary>
    /// The macros a "run another macro" step can be pointed at, with what each of them declares:
    /// the dialog builds the call's form out of that, so a step calls a macro by the values that
    /// macro asks for rather than by whatever the author remembers to write.
    /// </summary>
    private IReadOnlyList<MacroCallable> MacroCalls()
    {
        var calls = new List<MacroCallable>();
        foreach (var macro in _project.Where(macro => !ReferenceEquals(macro, _editing)))
        {
            if (macro.Name.Length > 0
                && !calls.Any(call => string.Equals(call.Name, macro.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                calls.Add(macro.Callable);
            }
        }

        // Parent and child macros are offered as they stand, so a call can be pointed at unsaved
        // work without leaving the editor.
        foreach (var page in _documentPages)
        {
            var callable = page.Editor.BuildMacro().Callable;
            if (callable.Name.Length > 0
                && !calls.Any(call => string.Equals(call.Name, callable.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                calls.Add(callable);
            }
        }

        return calls;
    }

    /// <summary>
    /// The macros a run started here may call, with the macro being edited taken as it stands
    /// rather than as it was last saved.
    /// </summary>
    internal ProjectMacroLibrary BuildLibrary()
    {
        var entries = new List<(string Name, IReadOnlyList<ExecutableStep> Steps,
            MacroSignature Signature, bool ExclusiveInput)>();

        foreach (var macro in _project.Where(macro => !ReferenceEquals(macro, _editing)))
        {
            if (macro.Name.Length > 0)
            {
                entries.Add((macro.Name, macro.Steps.ToExecutable(), macro.Callable.Signature,
                    macro.ExclusiveInput));
            }
        }

        foreach (var page in _documentPages)
        {
            var edited = page.Editor.BuildMacro();
            if (edited.Name.Length > 0
                && !entries.Any(entry => string.Equals(entry.Name, edited.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add((edited.Name, page.Editor.Steps.ToExecutable(),
                    edited.Callable.Signature, edited.ExclusiveInput));
            }
        }

        return new ProjectMacroLibrary(entries);
    }

    /// <summary>
    /// Opens the run window on the steps as they stand, without saving first. It loads them and stops
    /// there, the way the card's own menu does: a run that starts on its own has already done
    /// something by the time anyone has read the first step, and the reason to open this window is
    /// usually to look at the steps, put a breakpoint in and then press 「运行」. What to do with the
    /// macro is the window's business from then on.
    ///
    /// One of these windows is open at a time, so pressing the button twice does not leave two of
    /// them behind: the one already open is closed first — which stops whatever it was running — and
    /// the steps as they now stand go into a fresh window.
    /// </summary>
    private void OnRunRequested()
    {
        if (_viewModel.Steps.Count == 0)
        {
            return;
        }

        _running?.Close();
        var window = new RunWindow(_viewModel.Steps, Layer, _viewModel.DelayScale, BuildLibrary(),
            macroName: _viewModel.Name,
            callable: _viewModel.BuildMacro().Callable);
        _running = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_running, window))
            {
                _running = null;
            }
        };

        window.ShowOver(this);
    }

    /// <summary>Opens the run-speed dialog and keeps whatever factor it was given.</summary>
    private async void OnAdjustDelaysRequested()
    {
        if (await DelayScaleWindow.ShowFor(this, _viewModel.DelayScale, _viewModel.Steps) is { } chosen)
        {
            _viewModel.SetDelayScale(chosen);
        }
    }

    /// <summary>Opens the same window on an existing step and swaps in the result.</summary>
    private async void OnEditStepRequested(MacroStep step)
        => await ShowActionEditor(step, null, null, null);

    /// <summary>Confirms the palette's "clear all steps", which cannot be undone.</summary>
    private async void OnClearRequested()
    {
        var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Editor.ClearTitle"),
            Strings.Format("Editor.ClearMessage", _viewModel.Steps.Count),
            Strings.Get("Editor.Clear"));

        if (choice == ConfirmChoice.Primary)
        {
            _viewModel.ClearStepsNow();
        }
    }

    /// <summary>
    /// Marks the macro the editor was opened on as not saved. Work recovered from a run that
    /// stopped without notice is unsaved by definition, so closing the editor has to ask about it
    /// rather than let it go without a word.
    /// </summary>
    internal void MarkUnsaved()
    {
        _mustStayUnsaved = true;
        _viewModel.IsDirty = true;
    }

    /// <summary>
    /// The macro the editor finished with, or null when it was let go. The editor is not a dialog —
    /// the main list steps aside while it is up rather than waiting on it — so what it ends with is
    /// read from here as it closes.
    /// </summary>
    internal MacroItem? Result { get; private set; }

    /// <summary>
    /// The macro and all of its child pages as they stood when Save succeeded. The main window
    /// commits this snapshot while the editor remains open; closing later must not repeat it.
    /// </summary>
    internal event Action<MacroItem>? Saved;

    /// <summary>
    /// Macros this session made out of stretches of the one being edited, minus the ones the macro
    /// no longer calls — a lift that was undone or whose call was written over. What is left is part
    /// of what the session produced, so the project takes it along with the edited macro.
    /// </summary>
    internal IReadOnlyList<MacroItem> LiftedMacros
        => [.. _lifted.Where(macro => _viewModel.Steps.Any(step => Calls(step, macro.Name)))];

    /// <summary>True when this step, or any step inside it, calls the named macro.</summary>
    private static bool Calls(MacroStep step, string name)
        => step.Type == "control.runMacro" && string.Equals(step.Text("macro").Trim(), name,
            StringComparison.OrdinalIgnoreCase)
           || step.StepLists.Any(list => list.Steps.Any(child => Calls(child, name)));

    /// <summary>
    /// True when the main list went into the notification area to make room for this editor, which
    /// is what says whether it comes back when the editor is done.
    /// </summary>
    internal bool SteppedAside { get; set; }

    /// <summary>
    /// Keeps the macro being written in the recovery snapshot while it has unsaved changes, and
    /// lets it go once it does not — it was saved into the list, or the user did not want it.
    /// </summary>
    private void SyncRecovery()
    {
        if (!IsAnyDirty)
        {
            StopRecovery();
            return;
        }

        // The first change is written straight away, so a macro that is pasted in and then lost
        // to a crash seconds later is not waiting on the clock to be kept.
        _editorInRecovery = true;
        _recovery.Now();
    }

    /// <summary>
    /// Keeps a change that arrived while the macro was already unsaved from going unwritten. The
    /// write waits for the changes to pause; what first made the macro unsaved was written already.
    /// </summary>
    private void KeepRecovery()
    {
        if (!IsAnyDirty)
        {
            return;
        }

        _editorInRecovery = true;
        _recovery.Changed();
    }

    private void WriteRecovery()
    {
        if (!IsAnyDirty)
        {
            return;
        }

        RecoveryStore.SaveEditor(BuildRootSnapshot(), _editing?.Name ?? string.Empty);
        _editorInRecovery = true;
    }

    private bool IsAnyDirty => _rootViewModel.IsDirty
        || _documentPages.Any(page => page.Editor.IsDirty);

    private MacroItem BuildRootSnapshot()
    {
        _rootViewModel.SubMacros.Clear();
        foreach (var page in _documentPages.Where(page => !page.IsCurrent))
        {
            _rootViewModel.SubMacros.Add(page.Editor.BuildMacro());
        }

        return _rootViewModel.BuildMacro();
    }

    private void StopRecovery()
    {
        _recovery.Stop();

        // Only take back what this editor wrote: a draft left by a run that stopped is not this
        // window's to drop, since nobody has answered for it yet.
        if (_editorInRecovery)
        {
            _editorInRecovery = false;
            RecoveryStore.ClearEditor();
        }
    }

    /// <summary>Which windows are framed right now, so that a check can count them.</summary>
    internal IReadOnlyCollection<long> FramedWindows => _triggerFrames.Framed;

    /// <summary>
    /// Puts a frame around each window the trigger's rule recognises, and takes down the ones that
    /// have dropped out of it. Nothing is framed while the editor is out of the way — a minimised
    /// editor with frames left standing would be saying something nobody is reading — and the frames
    /// go with the trigger pane, which is only on screen while the macro is started by a window.
    /// This program's own windows are left out, the editor included: the window a rule is written in
    /// is one the rule really can fire on, so the list says so, but a ring drawn at its own edge
    /// reads as the border of the window rather than as an answer.
    /// </summary>
    private void DrawTriggerFrames()
    {
        IReadOnlyList<FramePlace> places = _closing || !IsVisible || WindowState == WindowState.Minimized
            ? []
            : _viewModel.WindowPreview.Places;

        _triggerFrames.Show(AppWindows.Outside(places));
    }

    private void UpdateTitleBarButtons()
    {
        if (this.FindControl<Button>("PinButton") is { } pin)
        {
            pin.Classes.Set("pinned", Topmost);
            ToolTip.SetTip(pin, Strings.Get(Topmost ? "Common.Unpin" : "Common.Pin"));
        }

        if (this.FindControl<IconView>("PinIcon") is { } pinIcon)
        {
            pinIcon.Data = Topmost ? AppIcons.PinOff : AppIcons.Pin;
        }

        var maximized = WindowState == WindowState.Maximized;
        if (this.FindControl<Button>("MaximizeButton") is { } maximize)
        {
            ToolTip.SetTip(maximize, Strings.Get(maximized ? "Common.Restore" : "Common.Maximize"));
        }

        if (this.FindControl<IconView>("MaximizeIcon") is { } maximizeIcon)
        {
            maximizeIcon.Data = maximized ? AppIcons.Restore : AppIcons.Maximize;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateTitleBarButtons();
        }

        if (change.Property == WindowStateProperty || change.Property == IsVisibleProperty)
        {
            DrawTriggerFrames();
        }
    }

    /// <summary>Prompts for unsaved work before the editor is dismissed.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowClose || !IsAnyDirty)
        {
            _closing = true;
        }
        else
        {
            // The answer is given before the base call, because the base call is what raises the
            // Closing event and the way out reads the answer there. A handler that ran first and
            // found nothing would take this window for one that had already gone, and would stop
            // the program on top of the very question it is asking.
            e.Cancel = true;
            _ = PromptToSaveAsync();
        }

        base.OnClosing(e);
    }

    private async Task PromptToSaveAsync()
    {
        if (_prompting)
        {
            return;
        }

        _prompting = true;
        try
        {
            var choice = await ConfirmDialog.ShowAsync(this,
                Strings.Get("Editor.UnsavedTitle"),
                Strings.Get("Editor.UnsavedMessage"),
                Strings.Get("Editor.Save"),
                Strings.Get("Editor.Discard"));

            switch (choice)
            {
                case ConfirmChoice.Primary:
                    SaveAll(closeAfterSave: true);
                    break;
                case ConfirmChoice.Secondary:
                    _allowClose = true;
                    Close(null);
                    break;
                default:
                    // "Not now" is an answer as well, and it belongs to the exit the tray may have
                    // started: without saying so, that exit would still be waiting on this window
                    // and would stop the program the next time it closed for a reason of its own.
                    AppTray.Current?.CalledOff();
                    break;
            }
        }
        finally
        {
            _prompting = false;
        }
    }

    /// <summary>Puts the input hook up when recording starts and takes it down when it stops.</summary>
    private void OnRecordingChanged(bool recording)
    {
        if (recording)
        {
            StartRecorder();
        }
        else
        {
            StopRecorder();
        }
    }

    private void StartRecorder()
    {
        if (_recorder is not null)
        {
            return;
        }

        lock (_captureLock)
        {
            _captured.Clear();
        }

        // The recorder comes from the device layer, so a layer handed in by a check records what
        // the check plays into it rather than what a real keyboard is doing.
        var recorder = Layer.CreateRecorder();
        recorder.Captured += OnCaptured;
        recorder.StopRequested += OnRecorderStopRequested;
        recorder.IgnoreInjected = _viewModel.RecordIgnoreInjected;
        recorder.RecordController = _viewModel.RecordGamepadButtons;

        try
        {
            recorder.Start();
        }
        catch (Exception)
        {
            recorder.Captured -= OnCaptured;
            recorder.StopRequested -= OnRecorderStopRequested;
            recorder.Dispose();
            _viewModel.StopRecording();
            _ = ConfirmDialog.ShowAsync(this,
                Strings.Get("Editor.RecordActions"),
                Strings.Get("Editor.RecordFailed"),
                Strings.Get("Common.Ok"),
                showCancel: false);
            return;
        }

        _recorder = recorder;
        StartRecordTimer();

        // Out of the way, so the work being recorded is not itself recorded as clicks on the
        // editor. The shortcuts that end a recording still reach it from anywhere.
        WindowState = WindowState.Minimized;
    }

    private void StopRecorder(bool announceEmpty = true)
    {
        var recorder = _recorder;
        _recorder = null;

        _recordTimer?.Stop();
        _recordTimer = null;

        if (recorder is null)
        {
            return;
        }

        recorder.Captured -= OnCaptured;
        recorder.StopRequested -= OnRecorderStopRequested;
        recorder.Stop();
        recorder.Dispose();

        List<RecordedInput> captured;
        lock (_captureLock)
        {
            captured = [.. _captured];
            _captured.Clear();
        }

        IReadOnlyList<MacroStep> steps = captured.Count == 0
            ? []
            : RecordingTranslator.Translate(captured, RecordingChoices()).ToSteps();

        _viewModel.AppendSteps(steps);

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();

        // Nothing at all is worth explaining: input another program sends is left out on
        // purpose, so a recording over a remote session or through another tool looks empty.
        if (announceEmpty && !_closing && captured.Count == 0)
        {
            _ = ConfirmDialog.ShowAsync(this,
                Strings.Get("Editor.RecordActions"),
                Strings.Get("Editor.RecordEmpty"),
                Strings.Get("Common.Ok"),
                showCancel: false);
        }
    }

    private void OnCaptured(RecordedInput input)
    {
        lock (_captureLock)
        {
            _captured.Add(input);
        }
    }

    /// <summary>The hook's own thread asks for the recording to end on the interface thread.</summary>
    private void OnRecorderStopRequested()
        => Dispatcher.UIThread.Post(() => _viewModel.StopRecording());

    /// <summary>The recording options as the panel on the editor has them set.</summary>
    private RecordingOptions RecordingChoices() => new()
    {
        MouseMovement = _viewModel.RecordMouseMovement,
        MouseButtons = _viewModel.RecordMouseButtons,
        KeyboardKeys = _viewModel.RecordKeyboardKeys,
        GamepadButtons = _viewModel.RecordGamepadButtons,
        Delays = _viewModel.RecordDelays,
        IgnoreDelaysBelowMs = (int)(_viewModel.IgnoreDelaysBelowMs ?? 0),
        RelativeMovement = _viewModel.PositionCapture == MousePositionMode.SavePositionDifferences,
    };

    /// <summary>Keeps the captured count in the status line without touching the hook thread.</summary>
    private void StartRecordTimer()
    {
        _recordTimer?.Stop();
        _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _recordTimer.Tick += (_, _) =>
        {
            lock (_captureLock)
            {
                _viewModel.ReportRecordedEvents(_captured.Count);
            }
        };
        _recordTimer.Start();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 38)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>Opens or folds the picked block, which is what the arrow keys do in a tree.</summary>
    private void Fold(bool open)
    {
        if (_viewModel.SelectedSteps is [{ } step])
        {
            _viewModel.SetExpanded(step, open);
        }
    }

    /// <summary>Double-clicking a row opens it, which is what a list is expected to do.</summary>
    private void OnStepDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RowUnder(e.Source) is not { IsStep: true } row)
        {
            return;
        }

        e.Handled = true;
        OnEditStepRequested(row.Step);
    }

    /// <summary>Context-menu commands for the step list, so they are not keyboard-only.</summary>
    private void OnStepContextEdit(object? sender, RoutedEventArgs e)
        => _viewModel.EditSelectedCommand.Execute(null);

    private void OnStepContextCopy(object? sender, RoutedEventArgs e) => _ = CopySelectionAsync();

    private void OnStepContextCut(object? sender, RoutedEventArgs e) => _ = CutSelectionAsync();

    private void OnStepContextPaste(object? sender, RoutedEventArgs e) => _ = PasteAsync();

    private void OnStepContextDelete(object? sender, RoutedEventArgs e)
        => _viewModel.DeleteSelectedCommand.Execute(null);

    private void OnStepContextMoveUp(object? sender, RoutedEventArgs e)
        => _viewModel.MoveSelectedUpCommand.Execute(null);

    private void OnStepContextMoveDown(object? sender, RoutedEventArgs e)
        => _viewModel.MoveSelectedDownCommand.Execute(null);

    private void OnStepContextGroup(object? sender, RoutedEventArgs e)
        => _viewModel.GroupSelectedCommand.Execute(null);

    private void OnStepContextLift(object? sender, RoutedEventArgs e)
        => _viewModel.LiftSelectedCommand.Execute(null);

    private void OnStepContextToggleEnabled(object? sender, RoutedEventArgs e)
        => _viewModel.ToggleStepEnabledCommand.Execute(null);

    /// <summary>Remembers where a press landed, so a plain click never turns into a drag.</summary>
    private void OnStepPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var onButton = IsOnButton(e.Source);
        var row = onButton ? null : RowUnder(e.Source);

        // A right-click on a row that is not part of the selection picks that row first, so the
        // context menu acts on what was clicked rather than on something picked earlier.
        if (point.Properties.IsRightButtonPressed)
        {
            if (row is { IsStep: true } && !_viewModel.SelectedSteps.Contains(row.Step))
            {
                _viewModel.SetSelection([row.Step]);
                _viewModel.RefreshSelection();
            }

            _pressedRow = null;
            return;
        }

        var pressed = point.Properties.IsLeftButtonPressed;

        // A press on a button inside a row belongs to that button: folding a block open, or
        // asking for a step inside one, must not turn into picking the row up as a drag.
        _pressedRow = pressed ? row : null;

        // A press on the empty part of the list — the space after the last row — is the user
        // saying "none of these", so the selection goes, the way it does in any other list. The
        // scrollbar is not empty space.
        if (pressed && row is null && !onButton && !IsOnScrollbar(e.Source))
        {
            _viewModel.SetSelection([]);

            // The list keeps a highlight of its own, so clearing the editor alone leaves the row
            // looking picked: the list has to be told to let go as well.
            _viewModel.RefreshSelection();
        }

        _dragOrigin = e.GetPosition(this);
        _draggingSteps = false;
        _dropSlot = -1;
    }

    /// <summary>Starts the drag once the pointer has moved far enough, then tracks the drop row.</summary>
    private void OnStepPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedRow is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndStepDrag();
            return;
        }

        if (!_draggingSteps)
        {
            var moved = e.GetPosition(this);

            // A few pixels of slack, so a shaky click is still a click.
            if (Math.Abs(moved.X - _dragOrigin.X) < 4 && Math.Abs(moved.Y - _dragOrigin.Y) < 4)
            {
                return;
            }

            // Dragging a row that is already part of a multiple selection moves them all.
            if (_stepList is { } list && !_viewModel.SelectedSteps.Contains(_pressedRow.Step))
            {
                list.SelectedItem = _pressedRow;
            }

            _draggingSteps = true;
            e.Pointer.Capture(_stepList);
        }

        UpdateDropSlot(e);
    }

    /// <summary>Drops the block where the marker points.</summary>
    private void OnStepPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // A press that never turned into a drag belongs to whatever it landed on. Handing the
        // capture back here would take it off the row button that took it, and a button that loses
        // its capture on the way up never reports a click — which is how the fold arrow and the
        // "add a step" button came to do nothing at all.
        if (!_draggingSteps)
        {
            EndStepDrag();
            return;
        }

        // The state is read and cleared before the capture goes back, because losing the
        // capture asks for the drag to end and would wipe the drop row out from under us.
        var slot = _dropSlot;
        var into = _dropInto;
        var allowed = _dropAllowed;
        var refusal = _dropRefusal;
        EndStepDrag();
        e.Pointer.Capture(null);

        if (!allowed)
        {
            _viewModel.ReportPlacementRefusal(refusal);
            return;
        }

        if (into is not null)
        {
            _viewModel.MoveSelectionInto(into);
        }
        else if (slot >= 0)
        {
            _viewModel.MoveSelectionTo(slot);
        }
    }

    private void OnStepPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => EndStepDrag();

    private void EndStepDrag()
    {
        _pressedRow = null;
        _draggingSteps = false;
        _dropSlot = -1;
        _dropInto = null;
        _dropAllowed = true;
        _dropRefusal = string.Empty;

        if (_dropMarker is not null)
        {
            _dropMarker.IsVisible = false;
        }
    }

    /// <summary>Works out which row the block would land at, and moves the marker onto that seam.</summary>
    private void UpdateDropSlot(PointerEventArgs e)
        => UpdateDropSlot(e.GetPosition(_stepList), actionKey: null);

    private void UpdateDropSlot(Point point, string? actionKey = null)
    {
        if (_stepList is null)
        {
            return;
        }

        _dropAllowed = true;
        _dropRefusal = string.Empty;
        var slot = _viewModel.Rows.Count;
        var markerY = _stepList.Bounds.Height;
        var indent = 0d;
        var hintKey = _viewModel.Rows.Count == 0
            ? "Editor.DropIntoEmptyList"
            : "Editor.DropAtPosition";
        _dropInto = null;

        var visible = _stepList.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Select(item => (Item: item, Row: item.DataContext as StepRow))
            .Where(candidate => candidate.Row is { RowIndex: >= 0 })
            .Select(candidate => (
                Row: candidate.Row!,
                Top: candidate.Item.TranslatePoint(new Point(0, 0), _stepList)?.Y ?? 0,
                Height: candidate.Item.Bounds.Height))
            .OrderBy(candidate => candidate.Row.RowIndex)
            .ToList();

        if (visible.Count > 0)
        {
            var last = visible[^1];
            if (point.Y >= last.Top + last.Height)
            {
                _dropSlot = _viewModel.Rows.Count;
                _dropMarkerY = last.Top + last.Height;
                _dropMarkerIndent = 0;
                _dropHintKey = "Editor.DropAtPosition";
                EvaluateDrop(_dropSlot, _dropInto, actionKey);
                ShowDropMarker();
                return;
            }
        }

        foreach (var (row, top, itemHeight) in visible)
        {
            var bottom = top + itemHeight;
            var height = bottom - top;
            var inside = point.Y >= top && point.Y < bottom;

            // The middle of a block that runs one list of steps means "put it inside this block".
            // A block with several lists — an if, a try, a switch — leaves it to the row of the
            // list it is meant for, because "inside" would not say which one.
            if (row.CanFold && row.Step.StepLists.Count() == 1
                && point.Y >= top + (height / 3) && point.Y < bottom - (height / 3))
            {
                _dropInto = row.Step;
                slot = -1;
                hintKey = "Editor.DropAtBlockEnd";
                (markerY, indent) = EndOfBlock(row, bottom);
                break;
            }

            // A section title and its closing line stand for one whole list. Dropping anywhere
            // on either one means the top or end of that list, not the section before or after it.
            if (inside && !_draggingSteps && row.IsHead)
            {
                slot = row.RowIndex;
                markerY = top;
                indent = (row.Depth + 1) * 18;
                break;
            }

            if (inside && !_draggingSteps && row.IsFoot
                && point.Y < top + (height / 2))
            {
                slot = row.RowIndex;
                markerY = top;
                indent = (row.Depth + 1) * 18;
                break;
            }

            // Past the middle of a row means the block goes after it, not before.
            if (point.Y < top + ((bottom - top) / 2))
            {
                slot = row.RowIndex;
                markerY = top;
                break;
            }

            slot = row.RowIndex + 1;
            markerY = bottom;
        }

        _dropSlot = slot;
        _dropMarkerY = markerY;
        _dropMarkerIndent = indent;
        _dropHintKey = hintKey;
        EvaluateDrop(_dropSlot, _dropInto, actionKey);
        ShowDropMarker();
    }

    /// <summary>Asks the editor whether the seam under the pointer accepts the proposed step(s).</summary>
    private void EvaluateDrop(int slot, MacroStep? into, string? actionKey)
    {
        var refusal = actionKey is null
            ? _viewModel.MoveSelectionRefusalAt(slot, into)
            : _viewModel.PlacementRefusalAt(slot, into,
                [new MacroStep { Type = actionKey }]);
        _dropAllowed = refusal is null;
        _dropRefusal = refusal ?? string.Empty;
    }

    private void OnActionDragOver(object? sender, DragEventArgs e)
    {
        if (_stepList is null
            || ActionDragPayload.Read(e.DataTransfer) is not { } payload)
        {
            e.DragEffects = DragDropEffects.None;
            if (_dropMarker is not null)
            {
                _dropMarker.IsVisible = false;
            }

            return;
        }

        var actionKey = payload.Kind == ActionPaletteCardKind.Action
            ? payload.Key
            : "control.runMacro";
        UpdateDropSlot(e.GetPosition(_stepList), actionKey);
        e.DragEffects = _dropAllowed ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnActionDragLeave(object? sender, DragEventArgs e)
    {
        _dropSlot = -1;
        _dropInto = null;
        if (_dropMarker is not null)
        {
            _dropMarker.IsVisible = false;
        }
    }

    private async void OnActionDropped(object? sender, DragEventArgs e)
    {
        if (ActionDragPayload.Read(e.DataTransfer) is not { } payload)
        {
            return;
        }

        var actionKey = payload.Kind == ActionPaletteCardKind.Action
            ? payload.Key
            : "control.runMacro";
        UpdateDropSlot(e.GetPosition(_stepList!), actionKey);
        var slot = _dropSlot;
        var into = _dropInto;
        var allowed = _dropAllowed;
        var refusal = _dropRefusal;
        var editingPage = _viewModel;
        EndStepDrag();

        if (!allowed)
        {
            _viewModel.ReportPlacementRefusal(refusal);
            return;
        }

        _viewModel.PrepareInsertionAt(slot, into);
        if (!_viewModel.CanPlace(actionKey))
        {
            _viewModel.ClearPendingInsertion();
            _viewModel.ReportPlacementRefusal(
                Strings.Format("Editor.PlaceUnavailable", actionKey));
            return;
        }

        string? macroName = null;
        _pendingSubMacro = null;
        if (payload.Kind == ActionPaletteCardKind.SubMacro)
        {
            macroName = _documentPages
                .FirstOrDefault(page => !page.IsCurrent
                    && string.Equals(page.Editor.Name, payload.Key,
                        StringComparison.OrdinalIgnoreCase))
                ?.Editor.Name;
            if (macroName is null)
            {
                _viewModel.ClearPendingInsertion();
                return;
            }
        }
        else if (payload.Kind == ActionPaletteCardKind.PublicMacro)
        {
            macroName = StagePublicSubMacro(payload.Key, payload.Version);
            if (macroName is null)
            {
                _viewModel.ClearPendingInsertion();
                return;
            }
        }

        await ShowActionEditor(null, _viewModel.InsertChoices, actionKey, macroName);
        _viewModel = editingPage;
        DataContext = editingPage;
    }

    private string? StagePublicSubMacro(string publicId, int version)
    {
        var existing = _documentPages.FirstOrDefault(page => !page.IsCurrent
            && page.Editor.Origin is { } origin
            && string.Equals(origin.Id, publicId, StringComparison.Ordinal)
            && origin.Version == version);
        if (existing is not null)
        {
            return existing.Editor.Name;
        }

        var entry = _publicMacros.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, publicId, StringComparison.Ordinal)
            && candidate.Version == version);
        if (entry is null)
        {
            return null;
        }

        var copy = PublicMacroLibrary.CopyToProject(entry, _assetFolder);
        copy.Name = UniqueSubMacroName(entry.Macro.Name);
        var editor = new MacroEditorViewModel { IsSubMacro = true };
        if (_devices is { } devices)
        {
            editor.Windows = devices.Windows;
        }

        editor.Documents = _documents;
        editor.LoadFrom(copy);
        editor.SubMacros.Clear();
        editor.IsDirty = true;
        WatchChildEditor(editor);
        var page = new MacroDocumentPageViewModel(editor, isCurrent: false, copy);
        _pendingSubMacro = new PendingSubMacro(editor, page);
        return editor.Name;
    }

    /// <summary>
    /// Where the drop line goes for a step about to land inside a block: under the line that
    /// closes the block, indented one level in, which is the place the step will end up in.
    /// </summary>
    private (double Y, double Indent) EndOfBlock(StepRow block, double fallback)
    {
        var indent = (block.Depth + 1) * 18;
        if (_stepList is null)
        {
            return (fallback, indent);
        }

        foreach (var item in _stepList.GetVisualDescendants().OfType<ListBoxItem>())
        {
            if (item.DataContext is StepRow { IsFoot: true } foot
                && ReferenceEquals(foot.Step, block.Step))
            {
                var top = item.TranslatePoint(new Point(0, 0), _stepList)?.Y ?? fallback;
                return (top + item.Bounds.Height, indent);
            }
        }

        return (fallback, indent);
    }

    private void ShowDropMarker()
    {
        if (_dropLayer is null || _dropMarker is null)
        {
            return;
        }

        var empty = _viewModel.Rows.Count == 0;
        var height = empty
            ? Math.Max(64, _dropLayer.Bounds.Height)
            : Math.Max(30, _dropMarker.MinHeight);
        var highest = Math.Max(0, _dropLayer.Bounds.Height - height);
        _dropMarker.Height = height;
        Canvas.SetLeft(_dropMarker, _dropMarkerIndent);
        _dropMarker.Width = Math.Max(0, _dropLayer.Bounds.Width - _dropMarkerIndent);
        Canvas.SetTop(_dropMarker, empty
            ? 0
            : Math.Clamp(_dropMarkerY - (height / 2), 0, highest));
        _dropMarker.Classes.Set("Rejected", !_dropAllowed);
        if (_dropHint is not null)
        {
            _dropHint.Text = _dropRefusal.Length > 0
                ? _dropRefusal
                : Strings.Get(_dropHintKey);
        }

        _dropMarker.IsVisible = true;
    }

    /// <summary>The row a pointer event happened on, or <c>null</c> when it missed them all.</summary>
    private static StepRow? RowUnder(object? source)
        => source is Visual visual
            ? visual.FindAncestorOfType<ListBoxItem>(true)?.DataContext as StepRow
            : null;

    /// <summary>True when the event landed on a button, which handles its own presses.</summary>
    private static bool IsOnButton(object? source)
        => source is Visual visual && visual.FindAncestorOfType<Button>(true) is not null;

    /// <summary>True when the event landed on the list's scrollbar rather than on its rows.</summary>
    private static bool IsOnScrollbar(object? source)
        => source is Visual visual
            && visual.FindAncestorOfType<Avalonia.Controls.Primitives.ScrollBar>(true) is not null;
}
