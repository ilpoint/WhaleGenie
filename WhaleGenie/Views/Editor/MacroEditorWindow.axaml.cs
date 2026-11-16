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
    private readonly MacroDocumentView _document;
    private readonly MacroEditorSession _session;

    /// <summary>The project's shared values, handed in by the window this was opened from.</summary>
    private readonly VariableSession _variables;
    private readonly MacroRecordingView _recording;
    private readonly MacroSettingsView _settings;

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

    /// <summary>The colour and window looks a trigger can take without running anything.</summary>
    private readonly EditorPickers _pickers;

    /// <summary>
    /// The frames standing around the windows a window trigger recognises right now, which answer
    /// "which ones" without waiting for the trigger to fire.
    /// </summary>
    private readonly TriggerFramePreview _frames = new();

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

    /// <summary>Keeps the macro being written as a draft while it has unsaved changes.</summary>
    private readonly EditorRecovery _recovery;

    /// <summary>What the editor is doing when a key, button or notch is meant as a binding.</summary>
    private readonly EditorBindingCapture _capture;

    /// <summary>What a recording does to this window and to the page it started from.</summary>
    private readonly EditorRecording _recordingRun;

    /// <summary>The step tree, kept because the drag handlers need it after construction.</summary>
    private TreeView? _stepList;

    private MacroInterfaceView? _interfaceView;

    /// <summary>
    /// A control the window reaches for by name, on its own frame or on one of the two views it
    /// shows. Splitting the settings drawer and the document column into views of their own left
    /// their names in a scope of their own; this keeps one lookup for the code that draws them and
    /// for the checks that read them.
    /// </summary>
    internal T? FindControl<T>(string name) where T : Control =>
        FindOn<T>(this, name) ?? _settings?.Find<T>(name)
        ?? FindOn<T>(_document, name) ?? FindOn<T>(_recording, name);

    private static T? FindOn<T>(Control? scope, string name) where T : Control =>
        scope is null ? null : NameScope.GetNameScope(scope)?.Find<T>(name) as T;

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
        bool callableOnly = false, MacroView? newView = null, PersistedVariables? persisted = null,
        VariableSession? variables = null)
    {
        InitializeComponent();
        _settings = this.FindControl<MacroSettingsView>("MacroSettings")!;
        _document = this.FindControl<MacroDocumentView>("MacroDocument")!;
        _recording = this.FindControl<MacroRecordingView>("MacroRecording")!;

        _session = new MacroEditorSession(existing,
            [.. project ?? (existing is null ? [] : [existing])],
            ImageAssets.FolderFor(packagePath));
        _devices = devices;
        _capture = new EditorBindingCapture(this,
            () => DataContext as MacroEditorViewModel, devices);
        _pickers = new EditorPickers(this, () => Layer);
        _recordingRun = new EditorRecording(this, _session, () => _viewModel, () => Layer);
        _variables = variables ?? new VariableSession();
        _rootViewModel = new MacroEditorViewModel
        {
            Persisted = persisted ?? new PersistedVariables(),
            Variables = _variables,
        };
        _rootViewModel.SystemReader = new SystemVariableReader(Layer);
        _rootViewModel.IsSubMacro = callableOnly;
        _viewModel = _rootViewModel;
        _rootViewModel.Documents = _session.Documents;
        _recovery = new EditorRecovery(() => IsAnyDirty, BuildRootSnapshot,
            () => _session.Editing?.Name ?? string.Empty);

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

        _documentPages = new EditorDocumentPages(_session, _rootViewModel, _variables, _devices,
            WatchChildEditor);
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
                    _capture.Reset();
                }

                _capture.Watch(_viewModel.IsCapturingKey);
            }

            if (args.PropertyName == nameof(MacroEditorViewModel.IsDirty))
            {
                _recovery.FirstChange();
            }
        };

        // A change that arrives while the macro is already unsaved reaches nothing above: the
        // dirty flag does not move for it, so the draft would be left holding the state it had
        // before that change.
        _viewModel.Changed += (_, _) =>
        {
            _settings.Find<TextBlock>("SavedNotice")!.IsVisible = false;
            _settings.Find<TextBlock>("CommitRejectedNotice")!.IsVisible = false;
            RefreshDocuments();
            _recovery.Keep();
        };

        // If the editor goes away by any route, the hook has to go with it. There is no point
        // telling the user an empty recording was empty once the editor is gone.
        Closed += (_, _) => StopRecorder(announceEmpty: false);
        Closed += (_, _) => _hotstringKeyboard?.Close();

        // And the reading of the controller, which has nothing left to tell once the window is gone.
        Closed += (_, _) => _capture.Watch(false);

        // And so does the recovery draft: once this window is gone, its macro either belongs to
        // the list or was let go, and the next run must not be offered it back.
        Closed += (_, _) => _recovery.Stop();

        // And the frames it put on the screen: a window that is gone has nothing left to explain
        // them, and they would go on standing over the windows they were drawn around.
        Closed += (_, _) =>
        {
            _closing = true;
            _frames.Dispose();
        };

        // The keys pressed while a macro is being written or a shortcut bound must not set one off.
        MacroTriggerGate.Enter();
        Closed += (_, _) => MacroTriggerGate.Exit();

        _variables.GlobalsChanged += OnGlobalsChanged;
        _variables.Values.Changed += OnSharedVariableChanged;
        Closed += (_, _) =>
        {
            _variables.GlobalsChanged -= OnGlobalsChanged;
            _variables.Values.Changed -= OnSharedVariableChanged;
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

        var stepList = _document.FindControl<TreeView>("StepList")!;
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

        _dropLayer = _document.DropLayer;
        _dropMarker = _document.DropMarker;
        _dropHint = _document.DropHintText;

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

}
