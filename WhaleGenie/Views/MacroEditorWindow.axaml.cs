using System;
using System.Collections.Generic;
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
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Recording;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MacroEditorWindow : Window
{
    private readonly MacroEditorViewModel _viewModel;
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
    /// opened from. Null when nobody handed one in, which is how a check opens the editor: the
    /// dialogs then make their own.
    /// </summary>
    private readonly IDeviceLayer? _devices;

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

    /// <summary>What the hook has captured so far, in the order it happened.</summary>
    private readonly List<RecordedInput> _captured = [];
    private readonly object _captureLock = new();
    private DispatcherTimer? _recordTimer;

    /// <summary>Clears the "what the last pick caught" line a moment after it is shown.</summary>
    private DispatcherTimer? _pickTimer;

    /// <summary>The step list, kept because the drag handlers need it after construction.</summary>
    private ListBox? _stepList;

    /// <summary>
    /// The window steps are written in, kept while the editor is open. A macro is a run of steps,
    /// and it floats above the editor so the list can be read and picked from while writing one.
    /// </summary>
    private AddActionWindow? _addWindow;

    /// <summary>Layer the drag marker is drawn on, over the step list.</summary>
    private Canvas? _dropLayer;
    private Border? _dropMarker;

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
        string? packagePath = null, IDeviceLayer? devices = null)
    {
        InitializeComponent();

        _project = [.. project ?? (existing is null ? [] : [existing])];
        _editing = existing;
        _assetFolder = ImageAssets.FolderFor(packagePath);
        _devices = devices;
        _viewModel = new MacroEditorViewModel();
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
            _viewModel.LoadFrom(existing);
        }

        // The other macros a call may point at, so a run-another-macro step that no longer names
        // one is marked while it is being read rather than when the trigger fires.
        _viewModel.SetProjectMacros(_project
            .Where(macro => !ReferenceEquals(macro, _editing))
            .Select(macro => macro.Callable),
            OtherMacroSendsInput());

        DataContext = _viewModel;
        Title = Strings.Get("Editor.Title");
        // Saving or cancelling takes the editor away, so the recording that ends with it has
        // nothing to report.
        _viewModel.CloseRequested += macro =>
        {
            _closing = true;
            Result = macro;
            Close(macro);
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
        _viewModel.MacroLifted += OnMacroLifted;
        _viewModel.RecordingChanged += OnRecordingChanged;

        // A macro that has not been saved must not be lost to a crash, so it is written out as
        // it changes. This window is the only place it can be kept: it is not in the list yet.
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MacroEditorViewModel.IsCapturingKey) && _viewModel.IsCapturingKey)
            {
                // A capture starts with nothing held: whatever the last one took is over by now.
                _captureModifiers.Clear();
                _swallowReleases.Clear();
            }

            if (args.PropertyName == nameof(MacroEditorViewModel.IsDirty))
            {
                SyncRecovery();
            }
        };

        // A change that arrives while the macro is already unsaved reaches nothing above: the
        // dirty flag does not move for it, so the draft would be left holding the state it had
        // before that change.
        _viewModel.Changed += (_, _) => KeepRecovery();

        // If the editor goes away by any route, the hook has to go with it. There is no point
        // telling the user an empty recording was empty once the editor is gone.
        Closed += (_, _) => StopRecorder(announceEmpty: false);

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

        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        if (minimizeButton is not null)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
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
                _viewModel.RefreshSelection();
            };

            // The list drops its selection when rows move, so the view model asks for it back.
            _viewModel.SelectionRefreshRequested += () =>
            {
                if (stepList.SelectedItems is not { } selected)
                {
                    return;
                }

                _syncingSelection = true;
                try
                {
                    selected.Clear();

                    // The rows are rebuilt on every change, so the highlights have to be put on
                    // the rows as they are now rather than on the ones that were picked.
                    var wanted = _viewModel.SelectedSteps;
                    foreach (var row in _viewModel.Rows.Where(row => row.IsStep && wanted.Contains(row.Step)))
                    {
                        selected.Add(row);
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
        }

        _dropLayer = this.FindControl<Canvas>("DropLayer");
        _dropMarker = this.FindControl<Border>("DropMarker");

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

    /// <summary>Takes one notch of the wheel as the trigger while the editor is waiting for one.</summary>
    private void OnPreviewPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MacroEditorViewModel viewModel || !viewModel.IsCapturingKey)
        {
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
            && IsModifier(e.Key))
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

        if (viewModel.IsCapturingKey)
        {
            if (IsModifier(e.Key))
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

            viewModel.CaptureKey(Chord(e.Key, Held(e)));
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
            case Key.F5:
                viewModel.RunCommand.Execute(null);
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

    /// <summary>The modifiers a combination is written with, in the order a binding reads them.</summary>
    private static readonly (KeyModifiers Flag, string Name)[] ChordModifiers =
    [
        (KeyModifiers.Control, "Ctrl"),
        (KeyModifiers.Shift, "Shift"),
        (KeyModifiers.Alt, "Alt"),
        (KeyModifiers.Meta, "Win"),
    ];

    /// <summary>Whether the key is one of the eight halves of Ctrl, Shift, Alt and Win.</summary>
    private static bool IsModifier(Key key) => ModifierFlag(key) != KeyModifiers.None;

    /// <summary>The group a modifier key belongs to; None for anything that is not one.</summary>
    private static KeyModifiers ModifierFlag(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
        Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
        Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
        Key.LWin or Key.RWin => KeyModifiers.Meta,
        _ => KeyModifiers.None,
    };

    /// <summary>
    /// Everything held when a key was pressed: what the event itself carries, plus the modifiers
    /// this window saw go down, so a modifier pressed before the capture started still counts.
    /// </summary>
    private KeyModifiers Held(KeyEventArgs e)
    {
        var held = e.KeyModifiers;
        foreach (var modifier in _captureModifiers)
        {
            held |= ModifierFlag(modifier);
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
    {
        var parts = new List<string>(ChordModifiers.Length + 1);
        foreach (var (flag, name) in ChordModifiers)
        {
            if (held.HasFlag(flag))
            {
                parts.Add(name);
            }
        }

        parts.Add(DescribeKey(key));
        return string.Join("+", parts);
    }

    private static string DescribeKey(Key key) => key switch
    {
        // The two halves of a modifier are separate keys, so a binding keeps the side it saw.
        Key.LeftCtrl => "左Ctrl",
        Key.RightCtrl => "右Ctrl",
        Key.LeftShift => "左Shift",
        Key.RightShift => "右Shift",
        Key.LeftAlt => "左Alt",
        Key.RightAlt => "右Alt",
        Key.LWin => "左Win",
        Key.RWin => "右Win",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.Space => "Space",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        _ => key.ToString(),
    };

    /// <summary>Opens the magnifier, which follows the pointer and reads the pixel under it.</summary>
    private async void OnPickColorClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MacroEditorViewModel viewModel)
        {
            return;
        }

        var colour = await ColorPickerWindow.PickAsync(this);
        Activate();

        if (colour is not { } picked)
        {
            return;
        }

        var point = WindowsScreenDevice.CursorPosition();
        viewModel.HexColor = picked.ToHex();
        viewModel.ColorPositionX = point.X.ToString(CultureInfo.InvariantCulture);
        viewModel.ColorPositionY = point.Y.ToString(CultureInfo.InvariantCulture);
        ShowPickStatus(Strings.Format("Editor.PickedColor", picked.ToHex()));
    }

    /// <summary>Alt + X: the pointer's current screen position becomes the watched position.</summary>
    private void CaptureCursorPosition(MacroEditorViewModel viewModel)
    {
        var position = WindowsScreenDevice.CursorPosition();
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
        var position = WindowsScreenDevice.CursorPosition();
        PixelColor colour;
        try
        {
            colour = new WindowsScreenDevice().PixelAt(position.X, position.Y);
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
    private void OnAddStepRequested() => ShowAddAction(null, _viewModel.InsertChoices, null);

    /// <summary>The same, preset on the "run another macro" step.</summary>
    private void OnRunMacroRequested() => ShowAddAction(null, null, "control.runMacro");

    /// <summary>
    /// Whether any other macro of this project sends keys or clicks. Two macros that do, running at
    /// once, write their input through each other, and neither of them looks wrong on its own — so
    /// the check is handed this rather than left to guess it from the macro being edited.
    /// </summary>
    private bool OtherMacroSendsInput()
        => _project
            .Where(macro => !ReferenceEquals(macro, _editing))
            .SelectMany(macro => macro.Steps)
            .Any(SendsInput);

    private static bool SendsInput(MacroStep step)
        => MacroRunner.SendsInput(step.Type)
            || step.StepLists.SelectMany(list => list.Steps).Any(SendsInput);

    /// <summary>
    /// Takes a macro lifted out of this one: it counts as part of the project from here on, so a
    /// later call can be pointed at it and the design-time check knows it is there.
    /// </summary>
    private void OnMacroLifted(MacroItem macro)
    {
        _lifted.Add(macro);
        _project.Add(macro);
        _viewModel.SetProjectMacros(_project
            .Where(candidate => !ReferenceEquals(candidate, _editing))
            .Select(candidate => candidate.Callable),
            OtherMacroSendsInput());
    }

    /// <summary>
    /// Opens the window a step is written in, or points the one already up at what the editor is
    /// working with now. It stays open from one step to the next, so every call here is also what
    /// tells it where a step would go and what that place takes.
    /// </summary>
    private void ShowAddAction(MacroStep? editing, IReadOnlyList<ActionDefinition>? actions,
        string? presetKey)
    {
        if (_addWindow is not { } window)
        {
            window = new AddActionWindow(editing, actions, _viewModel.CollectVariables(), MacroCalls(),
                presetKey, _assetFolder, _viewModel.StepChoices(), _viewModel.NewStepId())
            {
                StaysOpen = true,
                Macros = BuildLibrary(),
                MintStepId = _viewModel.NewStepId,
                PlaceStep = TakeStep,
                LookAtStep = LookAtStep,
            };

            // The layer the whole program shares, when the editor was given one: the trials this
            // window sends run on the same devices as everything else, models and all.
            if (_devices is { } shared)
            {
                window.Devices = shared;
            }

            // The window belongs to the editor, so it goes when the editor does; the field is
            // cleared with it so a later step opens a fresh one rather than a closed window.
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_addWindow, window))
                {
                    _addWindow = null;
                }
            };

            _addWindow = window;
            window.ShowOver(this);
            return;
        }

        window.Retarget(actions, _viewModel.CollectVariables(), MacroCalls(), _viewModel.StepChoices(),
            _assetFolder);
        window.Macros = BuildLibrary();

        if (editing is null)
        {
            window.BeginStep(presetKey);
        }
        else
        {
            window.EditStep(editing);
        }

        // An owned window has no taskbar button of its own, so asking for a step is what says the
        // window is wanted again: one that was put away comes back rather than staying out of reach.
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
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
    /// Puts a step the window handed over where it belongs, and answers what to say when the place
    /// picked in the list does not take that kind of step. The window is told what that place does
    /// take before anything is said, so the next attempt is made from a form it can fill in.
    /// </summary>
    private string? TakeStep(MacroStep? editing, MacroStep built)
    {
        if (editing is not null)
        {
            _viewModel.ReplaceStep(editing, built);
            return null;
        }

        if (_viewModel.AddStepHere(built) is not { } refusal)
        {
            return null;
        }

        _addWindow?.Retarget(_viewModel.InsertChoices, _viewModel.CollectVariables(), MacroCalls(),
            _viewModel.StepChoices(), _assetFolder);
        return refusal;
    }

    /// <summary>
    /// The macros a "run another macro" step can be pointed at, with what each of them declares:
    /// the dialog builds the call's form out of that, so a step calls a macro by the values that
    /// macro asks for rather than by whatever the author remembers to write.
    /// </summary>
    private IReadOnlyList<MacroCallable> MacroCalls()
    {
        var calls = new List<MacroCallable>();
        foreach (var macro in _project)
        {
            if (macro.Name.Length > 0
                && !calls.Any(call => string.Equals(call.Name, macro.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                calls.Add(macro.Callable);
            }
        }

        // The macro being edited is offered as it stands, so a call can be pointed at the very
        // macro it is being written in without saving first.
        var current = _viewModel.Name.Trim();
        if (current.Length > 0
            && !calls.Any(call => string.Equals(call.Name, current, StringComparison.OrdinalIgnoreCase)))
        {
            calls.Add(_viewModel.BuildMacro().Callable);
        }

        return calls;
    }

    /// <summary>
    /// The macros a run started here may call, with the macro being edited taken as it stands
    /// rather than as it was last saved.
    /// </summary>
    private ProjectMacroLibrary BuildLibrary()
    {
        var current = _viewModel.Name.Trim();
        var entries = new List<(string Name, IReadOnlyList<ExecutableStep> Steps,
            MacroSignature Signature)>();

        foreach (var macro in _project)
        {
            if (macro.Name.Length == 0
                || (current.Length > 0 && string.Equals(macro.Name, current, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            entries.Add((macro.Name, macro.Steps.ToExecutable(), macro.Callable.Signature));
        }

        if (current.Length > 0)
        {
            var edited = _viewModel.BuildMacro();
            entries.Add((current, _viewModel.Steps.ToExecutable(), edited.Callable.Signature));
        }

        return new ProjectMacroLibrary(entries);
    }

    /// <summary>Opens the run window on the steps as they stand, without saving first.</summary>
    private void OnRunRequested()
    {
        if (_viewModel.Steps.Count == 0)
        {
            return;
        }

        new RunWindow(_viewModel.Steps, _devices, _viewModel.DelayScale, BuildLibrary(),
            macroName: _editing?.Name ?? string.Empty).ShowOver(this);
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
    private void OnEditStepRequested(MacroStep step) => ShowAddAction(step, null, null);

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
    internal void MarkUnsaved() => _viewModel.IsDirty = true;

    /// <summary>
    /// The macro the editor finished with, or null when it was let go. The editor is not a dialog —
    /// the main list steps aside while it is up rather than waiting on it — so what it ends with is
    /// read from here as it closes.
    /// </summary>
    internal MacroItem? Result { get; private set; }

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
        if (!_viewModel.IsDirty)
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
        if (!_viewModel.IsDirty)
        {
            return;
        }

        _editorInRecovery = true;
        _recovery.Changed();
    }

    private void WriteRecovery()
    {
        if (!_viewModel.IsDirty)
        {
            return;
        }

        RecoveryStore.SaveEditor(_viewModel.BuildMacro(), _editing?.Name ?? string.Empty);
        _editorInRecovery = true;
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
    /// </summary>
    private void DrawTriggerFrames()
    {
        IReadOnlyList<FramePlace> places = _closing || !IsVisible || WindowState == WindowState.Minimized
            ? []
            : _viewModel.WindowPreview.Places;

        _triggerFrames.Show(places);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty || change.Property == IsVisibleProperty)
        {
            DrawTriggerFrames();
        }
    }

    /// <summary>Prompts for unsaved work before the editor is dismissed.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowClose || !_viewModel.IsDirty)
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
                    _viewModel.SaveCommand.Execute(null);
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

        var recorder = new InputHookRecorder();
        recorder.Captured += OnCaptured;
        recorder.StopRequested += OnRecorderStopRequested;
        recorder.IgnoreInjected = _viewModel.RecordIgnoreInjected;

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

    /// <summary>Remembers where a press landed, so a plain click never turns into a drag.</summary>
    private void OnStepPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        var onButton = IsOnButton(e.Source);
        var row = onButton ? null : RowUnder(e.Source);

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
        EndStepDrag();
        e.Pointer.Capture(null);

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

        if (_dropMarker is not null)
        {
            _dropMarker.IsVisible = false;
        }
    }

    /// <summary>Works out which row the block would land at, and moves the marker onto that seam.</summary>
    private void UpdateDropSlot(PointerEventArgs e)
    {
        if (_stepList is null)
        {
            return;
        }

        var point = e.GetPosition(_stepList);
        var slot = _viewModel.Rows.Count;
        var markerY = _stepList.Bounds.Height;
        var indent = 0d;
        _dropInto = null;

        foreach (var item in _stepList.GetVisualDescendants().OfType<ListBoxItem>())
        {
            if (item.DataContext is not StepRow { RowIndex: >= 0 } row)
            {
                continue;
            }

            var top = item.TranslatePoint(new Point(0, 0), _stepList)?.Y ?? 0;
            var bottom = top + item.Bounds.Height;
            var height = bottom - top;

            // The middle of a block that runs one list of steps means "put it inside this block".
            // A block with several lists — an if, a try, a switch — leaves it to the row of the
            // list it is meant for, because "inside" would not say which one.
            if (row.CanFold && row.Step.StepLists.Count() == 1
                && point.Y >= top + (height / 3) && point.Y < bottom - (height / 3))
            {
                _dropInto = row.Step;
                slot = -1;
                (markerY, indent) = EndOfBlock(row, bottom);
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
        ShowDropMarker();
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

        var highest = Math.Max(0, _dropLayer.Bounds.Height - 2);
        Canvas.SetLeft(_dropMarker, _dropMarkerIndent);
        _dropMarker.Width = Math.Max(0, _dropLayer.Bounds.Width - _dropMarkerIndent);
        Canvas.SetTop(_dropMarker, Math.Clamp(_dropMarkerY, 0, highest));
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
