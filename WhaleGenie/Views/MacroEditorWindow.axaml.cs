using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MacroEditorWindow : AppWindow
{
    /// <summary>What this editor is working on: the project, the macro, the lifted ones, the pages.</summary>
    private readonly MacroEditorSession _session;

    private readonly MacroEditorViewModel _rootViewModel;
    private MacroEditorViewModel _viewModel;
    private readonly ActionPaletteViewModel _palette = new();
    private IReadOnlyList<PublicMacroEntry> _publicMacros = [];
    private string? _publicMacroLoadError;
    private bool _switchingDocuments;
    private MacroDocumentPageViewModel? _pageBeforeTabClose;

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

    /// <summary>
    /// The controller, read while the editor is waiting for a binding. Windows has no word for a
    /// controller button going down, so this is a reading on a clock; what it was holding at the
    /// reading before is kept here, which is the only way a press can be told from a hold.
    /// </summary>
    private readonly GamepadButtonWatch _padHeld = new();

    /// <summary>The reading of the controller while the editor waits, or null when it is not waiting.</summary>
    private DispatcherTimer? _padTimer;

    /// <summary>What the hook has captured so far, in the order it happened.</summary>
    private DispatcherTimer? _recordTimer;

    /// <summary>Clears the "what the last pick caught" line a moment after it is shown.</summary>
    private DispatcherTimer? _pickTimer;

    /// <summary>The step tree, kept because the drag handlers need it after construction.</summary>
    private TreeView? _stepList;

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

    /// <summary>The list the dragged steps would land in, and where in it.</summary>
    private IList<MacroStep>? _dropList;
    private int _dropIndex = -1;

    /// <summary>
    /// True while the add-action window is open, so a double-click that lands twice does not stack
    /// two forms on top of each other. The window is modal; a second one would sit on the first.
    /// </summary>
    private bool _actionEditorOpen;

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
        bool callableOnly = false, MacroView? newView = null)
    {
        InitializeComponent();

        _session = new MacroEditorSession(existing,
            [.. project ?? (existing is null ? [] : [existing])],
            ImageAssets.FolderFor(packagePath));
        _devices = devices;
        _rootViewModel = new MacroEditorViewModel();
        _rootViewModel.SystemReader = new SystemVariableReader(Layer);
        _rootViewModel.IsSubMacro = callableOnly;
        _viewModel = _rootViewModel;
        _rootViewModel.Documents = _session.Documents;
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
            _rootViewModel.UseDefaultName(_session.Project.Select(macro => macro.Name));
            if (newView is { } view && !callableOnly)
            {
                _rootViewModel.PrepareNewTrigger(view);
            }
        }

        BuildDocumentPages();

        _viewModel.LoadProjectTriggerSettings(triggers ?? new ProjectTriggers(), _session.Project);
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

        // 标题栏归库，内容伸进标题栏：那一行里放鲸鱼、标题与置顶钮，整条都能拖（`AppWindow` 负责拖）。
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = 40;
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
        ActionPalette.CardActivated += OnCardActivated;
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

        UpdateTitleBarButtons();
        Strings.Current.LanguageChanged += UpdateTitleBarButtons;
        Closed += (_, _) => Strings.Current.LanguageChanged -= UpdateTitleBarButtons;

        var stepList = this.FindControl<TreeView>("StepList");
        if (stepList is not null)
        {
            _stepList = stepList;

            stepList.SelectionChanged += (_, _) =>
            {
                // Rebuilding the nodes makes the tree let go of its selection for a moment; that
                // is the editor's own doing, not the user unpicking the steps.
                if (_syncingSelection || _viewModel.IsRebuildingRows)
                {
                    return;
                }

                // A section title and a block's closing line stand for the block itself, so
                // picking one picks the block. The highlight is then put back on the block's own
                // row, because that is the row everything acts on.
                var rows = stepList.SelectedItems?.OfType<StepRow>().ToList() ?? [];
                _viewModel.SetSelection(rows.Select(row => row.Step).Distinct());
                RefreshPalette();

                // Ctrl+A selects every node at once, including each block's section titles and
                // closing line. Clearing that selection and putting it back on the step rows keeps
                // the editor's own idea of what is picked and the tree's in one place.
                var pickedSteps = rows
                    .Where(row => row.IsStep)
                    .Select(row => row.Step)
                    .ToHashSet();
                if (rows.Any(row => !row.IsStep && !pickedSteps.Contains(row.Step)))
                {
                    _viewModel.RefreshSelection();
                }
            };

            // The tree drops its selection when nodes move, so the view model asks for it back.
            _viewModel.SelectionRefreshRequested += () =>
            {
                _syncingSelection = true;
                try
                {
                    // The nodes are rebuilt on every change, so the highlights have to be put on
                    // the nodes as they are now rather than on the ones that were picked.
                    var wanted = _viewModel.SelectedSteps.ToHashSet();
                    var picked = _viewModel.Rows
                        .Where(row => row.IsStep && wanted.Contains(row.Step))
                        .Cast<object>()
                        .ToList();
                    SyncSelection(stepList, picked);
                }
                finally
                {
                    _syncingSelection = false;
                }
            };

            // Tunnelled: a row handles the press itself, so a drag has to be seen on the way
            // down to the row rather than after it. A double click is heard on the way back up,
            // and even after the tree has had its say, so opening a step cannot be swallowed by
            // the row's own handling.
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

    /// <summary>
    /// Selects every step the list is showing. The list is asked for one bulk change and the
    /// editor takes the same rows from it, so a long macro is not scrolled one row at a time.
    /// </summary>

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

    /// <summary>Brings up the window a step is written in, on the place the editor is now.</summary>
    private async void OnAddStepRequested()
    {
        var actions = _viewModel.AddableActions;
        if (actions.Count == 1)
        {
            await ShowActionEditor(null, actions, actions[0].Key, null);
            return;
        }

        RefreshPalette();
        ActionPalette.FocusSearch();
    }

    /// <summary>
    /// 双击动作卡：把这张动作交给和拖放同一个表单，保存后由编辑器放到当前落点（有选中就放在它
    /// 后面，空列表就是末尾）。拖放要真的按住拖过去，键盘与自动化都做不来，双击是它的等价物。
    /// </summary>
    private async void OnCardActivated(ActionPaletteCardViewModel card)
    {
        if (_actionEditorOpen || card.Kind != ActionPaletteCardKind.Action)
        {
            return;
        }

        var actions = _viewModel.AddableActions;
        if (!actions.Any(action => string.Equals(action.Key, card.Key, StringComparison.Ordinal)))
        {
            // 当前落点不收这种步骤，拖放也会被拒绝：不必开一个注定存不下的表单。
            return;
        }

        _actionEditorOpen = true;
        try
        {
            await ShowActionEditor(null, actions, card.Key, null);
        }
        finally
        {
            _actionEditorOpen = false;
        }
    }

    /// <summary>The same, preset on the "run another macro" step.</summary>
    private async void OnRunMacroRequested()
        => await ShowActionEditor(null, ActionCatalog.ForKeys(["control.runMacro"]), "control.runMacro", null);

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
        var macros = _session.Project.ToList();

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

            if (step.Type is not "control.runMacro")
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
            .Where(macro => !ReferenceEquals(macro, _session.Editing))
            .SelectMany(macro => macro.Steps)
            .Any(step => Walk(step, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Takes a macro lifted out of this one: it counts as part of the project from here on, so a
    /// later call can be pointed at it and the design-time check knows it is there.
    /// </summary>
    private void OnMacroLifted(MacroItem macro)
    {
        _session.Lifted.Add(macro);
        _session.Project.Add(macro);
        RefreshProjectCallables();
    }

    /// <summary>Writes one step in the single, draggable action window.</summary>
    private async Task<MacroStep?> ShowActionEditor(MacroStep? editing,
        IReadOnlyList<ActionDefinition> actions, string? presetKey, string? macroName)
    {
        var editingPage = _viewModel;

        // 窗口那几格现在可以"沿用上一次"：这里把本宏用过的窗口收成一张表交给表单，并告诉它落点
        // 之上有没有人指定过窗口——有的话，新写的这一步默认就跟着上面那个走，不必再绑一次。
        var windowRules = WindowRuleChoices.Of(_viewModel.Steps);
        var windowAbove = WindowRuleChoices
            .NearestAbove(_viewModel.Steps, _viewModel.SelectedSteps.LastOrDefault()?.Id);
        var follows = editing is null && windowAbove.Length > 0;

        var dialog = new ActionEditorWindow(editing, actions, _viewModel.CollectVariables(),
            MacroCalls(), presetKey, _session.AssetFolder, _viewModel.StepChoices(),
            editing?.Id ?? _viewModel.NewStepId(), windowRules, follows, windowAbove)
        {
            Devices = Layer,
            Macros = BuildLibrary(),
            PlaceStep = (existing, step) => existing is null
                ? _viewModel.PlacementRefusalForPending(step)
                : _viewModel.PlacementRefusalForReplacement(existing, step),
            NextStepId = taken => _viewModel.NewStepId(taken),
        };
        if (macroName is not null)
        {
            dialog.SetMacroCall(macroName);
        }

        // The modal loop is what keeps the editor behind the window out of reach; it is started and
        // let go, because its own answer is not the step. A picker hides this window while it is
        // out, and Avalonia answers a hidden modal window at once with nothing — so what is waited
        // on is the window itself, which says what it was closed with.
        _ = dialog.ShowDialogOver<MacroStep?>(this);
        var step = await dialog.Answer;

        _viewModel = editingPage;
        DataContext = editingPage;
        if (step is null)
        {
            _session.Pending = null;
            _viewModel.ClearPendingInsertion();
            return null;
        }

        if (editing is not null)
        {
            _viewModel.ReplaceStep(editing, step);
            return step;
        }

        var pending = _session.Pending;
        _session.Pending = null;
        _viewModel = editingPage;
        DataContext = editingPage;
        var refusal = _viewModel.AddStepHere(step);
        if (refusal is null
            && pending is not null
            && step.Type == "control.runMacro"
            && string.Equals(step.Text("macro").Trim(), pending.Editor.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            _session.Documents.Add(pending.Page);
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

        // The reason is written on one of the two pages, so the drawer opens the one that holds it:
        // a declaration problem is on the interface page, everything else on the first.
        _viewModel.ShowSettingsPage(_viewModel.PageThatBlocksSaving);

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
            // The tree does not follow the selection (see the comment on `StepList`), so the row
            // a step was revealed on is brought into view once the containers exist.
            Dispatcher.UIThread.Post(() =>
            {
                var container = _stepList?.GetVisualDescendants()
                    .OfType<TreeViewItem>()
                    .FirstOrDefault(item => ReferenceEquals(item.DataContext, row));
                container?.BringIntoView();
            });
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
        foreach (var macro in _session.Project.Where(macro => !ReferenceEquals(macro, _session.Editing)))
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

        // And the submacros of this document by the names a run looks them up by, whether or not a
        // page is open for them: that is the list the 宏 field beside a call step shows.
        foreach (var callable in CallableMacros.Callables(DocumentSubMacros()))
        {
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
            MacroSignature Signature)>();

        foreach (var macro in _session.Project.Where(macro => !ReferenceEquals(macro, _session.Editing)))
        {
            if (macro.Name.Length > 0)
            {
                entries.Add((macro.Name, macro.Steps.ToExecutable(), macro.Callable.Signature));
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
                    edited.Callable.Signature));
            }
        }

        // The submacros of the document as it stands, under the names a call writes: pressing 运行
        // here has to reach the same macros the saved file would.
        foreach (var entry in CallableMacros.Entries(DocumentSubMacros()))
        {
            if (!entries.Any(known => string.Equals(known.Name, entry.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(entry);
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
        var macro = _viewModel.BuildMacro();
        var window = new RunWindow(_viewModel.Steps, Layer, _viewModel.DelayScale, BuildLibrary(),
            macroName: _viewModel.Name,
            callable: macro.Callable,
            inputRate: macro.InputRate);
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

    /// <summary>
    /// Opens the same window on an existing step and swaps in the result. The catalogue it is
    /// handed includes the actions no longer offered, so a step written before one was retired
    /// still opens.
    /// </summary>
    private async void OnEditStepRequested(MacroStep step)
    {
        // The list the form picks from has to hold the step being edited. A branch of a switch is
        // hidden from the palette — it is only ever written through its switch — and a form that
        // cannot find it would quietly edit it as whichever action came first.
        var actions = ActionCatalog.PlaceableActions;
        if (ActionCatalog.Find(step.Type) is { } definition
            && !actions.Any(action => action.Key == definition.Key))
        {
            actions = [.. actions, definition];
        }

        await ShowActionEditor(step, actions, null, null);
    }

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

    /// <summary>Puts the input hook up when recording starts and takes it down when it stops.</summary>

    /// <summary>Opens or folds the picked block, which is what the arrow keys do in a tree.</summary>

    /// <summary>Remembers where a press landed, so a plain click never turns into a drag.</summary>
    private void OnStepPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var onButton = IsOnButton(e.Source);

        // A text box a row carries owns its own presses: clicking into it is putting the caret
        // somewhere, not picking the row up as a drag or clearing the selection around it.
        var onText = IsOnText(e.Source);
        var row = onButton || onText ? null : RowUnder(e.Source);

        // The second press of a double click opens the row it landed on. Doing it here rather than
        // on the double-tap keeps it independent of what selection did with the first click, and
        // marking the press handled is also what stops the tree from folding the block that holds
        // the row: opening a step must not close the block it was found in.
        if (e.ClickCount == 2 && row is { } twice && (twice.IsStep || twice.IsCase)
            && OnRowContent(e.Source))
        {
            e.Handled = true;
            _pressedRow = null;
            Dispatcher.UIThread.Post(() => OnEditStepRequested(twice.Step));
            return;
        }

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
        if (pressed && row is null && !onButton && !onText && !IsOnScrollbar(e.Source))
        {
            _viewModel.SetSelection([]);

            // The list keeps a highlight of its own, so clearing the editor alone leaves the row
            // looking picked: the list has to be told to let go as well.
            _viewModel.RefreshSelection();
        }

        _dragOrigin = e.GetPosition(this);
        _draggingSteps = false;
        _dropList = null;
        _dropIndex = -1;
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
        // capture asks for the drag to end and would wipe the drop seam out from under us.
        var list = _dropList;
        var index = _dropIndex;
        var allowed = _dropAllowed;
        var refusal = _dropRefusal;
        EndStepDrag();
        e.Pointer.Capture(null);

        if (!allowed)
        {
            _viewModel.ReportPlacementRefusal(refusal);
            return;
        }

        if (list is not null && index >= 0)
        {
            _viewModel.MoveSelectionTo(list, index);
        }
    }

    private void OnStepPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => EndStepDrag();

    private void EndStepDrag()
    {
        _pressedRow = null;
        _draggingSteps = false;
        _dropList = null;
        _dropIndex = -1;
        _dropAllowed = true;
        _dropRefusal = string.Empty;

        if (_dropMarker is not null)
        {
            _dropMarker.IsVisible = false;
        }
    }

    /// <summary>Works out where the dragged block would land, and moves the marker onto that seam.</summary>
    private void UpdateDropSlot(PointerEventArgs e)
        => UpdateDropAt(e.GetPosition(_stepList), actionKey: null);

    /// <summary>
    /// The same question asked without a pointer, answered with what the marker would say: which
    /// list the steps land in, at what position, and whether the drop is allowed. A drag cannot be
    /// driven from a check, and the seams a tree offers are worth one.
    /// </summary>
    internal (IList<MacroStep>? Into, int Index, bool Allowed) DropTargetAt(Point point, string? actionKey)
    {
        UpdateDropAt(point, actionKey);
        return (_dropList, _dropIndex, _dropAllowed);
    }

    /// <summary>
    /// A tree answers "where does this go" with a list and a position rather than with a row
    /// number: the same place in the document can be reached by dropping above the step below it
    /// or below the step above it, and the list is what both answers have in common.
    /// </summary>
    private void UpdateDropAt(Point point, string? actionKey = null)
    {
        if (_stepList is null)
        {
            return;
        }

        _dropAllowed = true;
        _dropRefusal = string.Empty;
        _dropList = null;
        _dropIndex = -1;
        _dropMarkerY = 0;
        _dropMarkerIndent = 0;
        _dropHintKey = "Editor.DropAtPosition";

        var boxes = HeaderBoxes();

        // Nothing on screen: the macro is empty, so the only seam is the one at its top.
        if (boxes.Count == 0)
        {
            Place(_viewModel.Steps, 0, 0, 0, "Editor.DropIntoEmptyList", actionKey);
            return;
        }

        // The top edge of the first row is the start of the macro: a block that is first still has
        // a place in front of it, and a few pixels there are what says so.
        if (point.Y < boxes[0].Top + 6)
        {
            Place(_viewModel.Steps, 0, boxes[0].Top, 0, "Editor.DropBeforeStep", actionKey);
            return;
        }

        // The space below the last row is the end of the macro: a pointer down there is past
        // everything, so that is what it means rather than "after whichever block happened to be
        // last".
        var last = boxes[^1];
        if (point.Y >= last.Top + last.Height)
        {
            Place(_viewModel.Steps, _viewModel.Steps.Count, last.Top + last.Height, 0,
                "Editor.DropAfterStep", actionKey);
            return;
        }

        // The gaps between rows belong to the row above them, so a pointer never falls into a
        // height no row answers for.
        var at = 0;
        for (var candidate = 0; candidate < boxes.Count; candidate++)
        {
            if (point.Y >= boxes[candidate].Top)
            {
                at = candidate;
            }
        }

        var box = boxes[at];
        var row = box.Row;
        var top = box.Top;
        var bottom = top + box.Height;

        // The foot of a switch's branch table is the end of the branch list: a branch dragged onto
        // it is saying "put it last", which is the same thing the row itself offers to do.
        if (row.IsAddRow && row.List is { } table)
        {
            Place(table.Steps, table.Steps.Count, bottom, row.Depth * 16,
                table.Steps.Count == 0 ? "Editor.DropIntoEmptyList" : "Editor.DropAtBlockEnd",
                actionKey);
            return;
        }

        // A body row is one list, and the whole of it means the end of that list: a pointer over
        // "body steps" is saying "put it in there", and the end is where a person's next step
        // goes. Landing it at the top is what made an empty body feel like it swallowed the drop.
        if (row.IsSection && row.List is { } section)
        {
            Place(section.Steps, section.Steps.Count, BottomOf(row, boxes), (row.Depth + 1) * 16,
                section.Steps.Count == 0 ? "Editor.DropIntoEmptyList" : "Editor.DropAtBlockEnd",
                actionKey);
            return;
        }

        // A block that runs one list is the same promise one level up: the whole row means "into
        // it, at the end", whether it is open or folded. A block that runs several lists leaves
        // "inside" to its body rows, because "inside" there would not say which.
        if (row.IsBlock && row.Step.StepLists.Count() == 1 && row.Body is { } body)
        {
            var tail = row.Children.FirstOrDefault(child => child.IsSection);
            Place(body.Steps, body.Steps.Count,
                tail is null ? bottom : BottomOf(tail, boxes), (row.Depth + 1) * 16,
                body.Steps.Count == 0 ? "Editor.DropIntoEmptyList" : "Editor.DropAtBlockEnd",
                actionKey);
            return;
        }

        var before = point.Y < top + (box.Height / 2);
        var (list, index) = before ? Before(row) : After(row);
        Place(list, index, before ? top : bottom, row.Depth * 16,
            row.IsBlock ? (before ? "Editor.DropBeforeBlock" : "Editor.DropAfterBlock")
                        : (before ? "Editor.DropBeforeStep" : "Editor.DropAfterStep"),
            actionKey);
    }

    /// <summary>Records the seam under the pointer and draws the marker on it.</summary>
    private void Place(IList<MacroStep> list, int index, double y, double indent,
        string hintKey, string? actionKey)
    {
        _dropList = list;
        _dropIndex = Math.Clamp(index, 0, list.Count);
        _dropMarkerY = y;
        _dropMarkerIndent = indent;
        _dropHintKey = hintKey;
        EvaluateDrop(list, _dropIndex, actionKey);
        ShowDropMarker();
    }

    /// <summary>The list a row sits in, which is the macro's own list for a step at the top.</summary>
    private IList<MacroStep> StepsOf(StepRow row)
        => (IList<MacroStep>?)row.List?.Steps ?? _viewModel.Steps;

    /// <summary>The seam in front of a row: its own list, at its own place.</summary>
    private (IList<MacroStep> List, int Index) Before(StepRow row)
        => (StepsOf(row), Math.Max(0, row.Index));

    /// <summary>
    /// The seam behind a row. Behind a block that is the next step of the list it sits in rather
    /// than the end of its body: the body's end is the lower half of the block's own row.
    /// </summary>
    private (IList<MacroStep> List, int Index) After(StepRow row)
    {
        var list = StepsOf(row);
        return (list, Math.Clamp(row.Index + 1, 0, list.Count));
    }

    /// <summary>Where the last row inside a section is drawn, so "at the end of it" can be shown.</summary>
    private static double BottomOf(StepRow section, IReadOnlyList<RowBox> boxes)
    {
        var lastIndex = LastRowIndex(section);
        var last = boxes.LastOrDefault(box => box.Row.RowIndex <= lastIndex);
        return last.Row is null ? 0 : last.Top + last.Height;
    }

    private static int LastRowIndex(StepRow row)
        => row.Children.Count == 0 ? row.RowIndex : Math.Max(row.RowIndex, LastRowIndex(row.Children[^1]));

    /// <summary>The row headers on screen, top to bottom, as the tree actually laid them out.</summary>
    private List<RowBox> HeaderBoxes()
        => _stepList is null
            ? []
            : [.. _stepList.GetVisualDescendants()
                .OfType<Panel>()
                .Where(panel => panel.Name == "RowHeader")
                .Select(panel => (
                    Panel: panel,
                    Row: panel.DataContext as StepRow,
                    Top: panel.TranslatePoint(new Point(0, 0), _stepList)?.Y ?? 0))
                .Where(candidate => candidate.Row is { } && candidate.Panel.Bounds.Height > 0)
                .Select(candidate => new RowBox(
                    candidate.Row!, candidate.Top, candidate.Panel.Bounds.Height))
                .OrderBy(box => box.Top)];

    /// <summary>One row header: what it stands for, where it is drawn, and how tall it is.</summary>
    private readonly record struct RowBox(StepRow Row, double Top, double Height)
    {
        /// <summary>
        /// The first entry of the default value answers "no row here". A struct cannot be null, so
        /// the one thing a caller checks is that it is looking at a real row.
        /// </summary>
        public bool Exists => Row is not null;
    }

    /// <summary>Asks the editor whether the seam under the pointer accepts the proposed step(s).</summary>
    private void EvaluateDrop(IList<MacroStep> list, int index, string? actionKey)
    {
        var refusal = actionKey is null
            ? _viewModel.MoveSelectionRefusalAt(list, index)
            : _viewModel.PlacementRefusalAt(list, index, [new MacroStep { Type = actionKey }]);
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
        UpdateDropAt(e.GetPosition(_stepList), actionKey);
        e.DragEffects = _dropAllowed ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnActionDragLeave(object? sender, DragEventArgs e)
    {
        _dropList = null;
        _dropIndex = -1;
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
        UpdateDropAt(e.GetPosition(_stepList!), actionKey);
        var list = _dropList;
        var index = _dropIndex;
        var allowed = _dropAllowed;
        var refusal = _dropRefusal;
        var editingPage = _viewModel;
        EndStepDrag();

        if (!allowed)
        {
            _viewModel.ReportPlacementRefusal(refusal);
            return;
        }

        if (list is null || index < 0)
        {
            return;
        }

        _viewModel.PrepareInsertionAt(list, index);
        if (!_viewModel.CanPlace(actionKey))
        {
            _viewModel.ClearPendingInsertion();
            _viewModel.ReportPlacementRefusal(
                Strings.Format("Editor.PlaceUnavailable", actionKey));
            return;
        }

        string? macroName = null;
        _session.Pending = null;
        if (payload.Kind == ActionPaletteCardKind.SubMacro)
        {
            // A submacro no longer has to be open in a page of its own to be called: it is in the
            // document, and the dock that offered it is the document's own list.
            var sub = DocumentSubMacros().FirstOrDefault(macro =>
                string.Equals(macro.Name, payload.Key, StringComparison.OrdinalIgnoreCase));
            macroName = sub is null ? null : CallNameFor(sub);
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

        await ShowActionEditor(null, _viewModel.AddableActions, actionKey, macroName);
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

        var copy = PublicMacroLibrary.CopyToProject(entry, _session.AssetFolder);
        copy.Name = UniqueSubMacroName(entry.Macro.Name);
        var editor = new MacroEditorViewModel { IsSubMacro = true };
        if (_devices is { } devices)
        {
            editor.Windows = devices.Windows;
        }

        editor.Documents = _session.Documents;
        editor.LoadFrom(copy);
        editor.SubMacros.Clear();
        editor.IsDirty = true;
        WatchChildEditor(editor);
        var page = new MacroDocumentPageViewModel(editor, isCurrent: false, copy);
        _session.Pending = new PendingSubMacro(editor, page);
        return editor.Name;
    }

    private void ShowDropMarker()
    {
        if (_dropLayer is null || _dropMarker is null)
        {
            return;
        }

        var empty = _viewModel.Roots.Count == 0;
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

    /// <summary>
    /// Puts the tree's own highlight on exactly the nodes the editor has picked. The tree keeps
    /// a highlight per node and the editor keeps one per step, and this is the one place the two
    /// are made to agree; without it, a rebuild would leave a row looking picked that is not.
    /// </summary>
    private static void SyncSelection(TreeView tree, IReadOnlyList<object> wanted)
    {
        if (tree.SelectedItems is not { } selection)
        {
            return;
        }

        for (var index = selection.Count - 1; index >= 0; index--)
        {
            if (selection[index] is not { } item || !wanted.Contains(item))
            {
                selection.RemoveAt(index);
            }
        }

        foreach (var item in wanted)
        {
            if (!selection.Contains(item))
            {
                selection.Add(item);
            }
        }
    }

    /// <summary>The row a pointer event happened on, or <c>null</c> when it missed them all.</summary>
    private static StepRow? RowUnder(object? source)
        => source is Visual visual
            ? visual.FindAncestorOfType<TreeViewItem>(true)?.DataContext as StepRow
            : null;

    /// <summary>True when the event landed on a button, which handles its own presses.</summary>
    private static bool IsOnButton(object? source)
        => source is Visual visual && visual.FindAncestorOfType<Button>(true) is not null;

    /// <summary>True when the event landed in a text box, which owns its own presses and keys.</summary>
    private static bool IsOnText(object? source)
        => source is Visual visual && visual.FindAncestorOfType<TextBox>(true) is not null;

    /// <summary>True when the event landed on the list's scrollbar rather than on its rows.</summary>
    private static bool IsOnScrollbar(object? source)
        => source is Visual visual
            && visual.FindAncestorOfType<Avalonia.Controls.Primitives.ScrollBar>(true) is not null;
}
