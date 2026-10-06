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
using Viktor.Core.Devices;
using Viktor.Core.Devices.Platform;
using Viktor.Core.Execution;
using Viktor.Core.Recording;
using Viktor.Execution;
using Viktor.Localization;
using Viktor.Models;
using Viktor.Storage;
using Viktor.ViewModels;

namespace Viktor.Views;

public partial class MacroEditorWindow : Window
{
    private readonly MacroEditorViewModel _viewModel;
    private readonly IReadOnlyList<MacroItem> _project;

    /// <summary>Where a picture taken from the screen is put, so it travels with the package.</summary>
    private readonly string _assetFolder;

    private bool _allowClose;
    private bool _prompting;
    private bool _syncingSelection;
    private bool _closing;

    /// <summary>
    /// A key that was just taken as the trigger still sends its release, and a focused button
    /// treats Space on release as a click — which would arm the capture all over again.
    /// </summary>
    private bool _swallowCapturedKeyUp;

    /// <summary>The input hook, up only while a recording is in progress.</summary>
    private IInputRecorder? _recorder;

    /// <summary>What the hook has captured so far, in the order it happened.</summary>
    private readonly List<RecordedInput> _captured = [];
    private readonly object _captureLock = new();
    private DispatcherTimer? _recordTimer;

    /// <summary>Clears the "what the last pick caught" line a moment after it is shown.</summary>
    private DispatcherTimer? _pickTimer;

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
        string? packagePath = null)
    {
        InitializeComponent();

        _project = project ?? (existing is null ? [] : [existing]);
        _assetFolder = ImageAssets.FolderFor(packagePath);
        _viewModel = new MacroEditorViewModel();
        if (existing is not null)
        {
            _viewModel.LoadFrom(existing);
        }

        DataContext = _viewModel;
        Title = Strings.Get("Editor.Title");
        // Saving or cancelling takes the editor away, so the recording that ends with it has
        // nothing to report.
        _viewModel.CloseRequested += macro =>
        {
            _closing = true;
            Close(macro);
        };
        _viewModel.AddStepRequested += OnAddStepRequested;
        _viewModel.EditStepRequested += OnEditStepRequested;
        _viewModel.StepSettingsRequested += OnStepSettingsRequested;
        _viewModel.ClearRequested += OnClearRequested;
        _viewModel.CopyRequested += () => _ = CopySelectionAsync();
        _viewModel.CutRequested += () => _ = CutSelectionAsync();
        _viewModel.PasteRequested += () => _ = PasteAsync();
        _viewModel.RunRequested += OnRunRequested;
        _viewModel.AdjustDelaysRequested += OnAdjustDelaysRequested;
        _viewModel.RunMacroRequested += OnRunMacroRequested;
        _viewModel.RecordingChanged += OnRecordingChanged;

        // If the editor goes away by any route, the hook has to go with it. There is no point
        // telling the user an empty recording was empty once the editor is gone.
        Closed += (_, _) => StopRecorder(announceEmpty: false);

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
            stepList.SelectionChanged += (_, _) =>
            {
                if (_syncingSelection)
                {
                    return;
                }

                _viewModel.SetSelection(stepList.SelectedItems?.OfType<MacroStep>() ?? []);
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
                    foreach (var step in _viewModel.SelectedSteps)
                    {
                        selected.Add(step);
                    }
                }
                finally
                {
                    _syncingSelection = false;
                }
            };
        }

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
            ? (SharpHook.Data.MouseWheelScrollDirection.Horizontal,
                (short)(e.Delta.X < 0 ? 1 : -1))
            : (SharpHook.Data.MouseWheelScrollDirection.Vertical,
                (short)(e.Delta.Y >= 0 ? 1 : -1));

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
            PointerUpdateKind.LeftButtonPressed => SharpHook.Data.MouseButton.Button1,
            PointerUpdateKind.RightButtonPressed => SharpHook.Data.MouseButton.Button2,
            PointerUpdateKind.MiddleButtonPressed => SharpHook.Data.MouseButton.Button3,
            PointerUpdateKind.XButton1Pressed => SharpHook.Data.MouseButton.Button4,
            PointerUpdateKind.XButton2Pressed => SharpHook.Data.MouseButton.Button5,
            _ => SharpHook.Data.MouseButton.NoButton,
        };

        var name = KeyNames.MouseName(button);
        if (name.Length == 0)
        {
            return;
        }

        viewModel.CaptureKey(name);
        e.Handled = true;
    }

    /// <summary>
    /// Swallows the release that follows a captured press. A focused button reads Space on
    /// release as a click, which would arm the very capture the user just finished.
    /// </summary>
    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_swallowCapturedKeyUp)
        {
            return;
        }

        _swallowCapturedKeyUp = false;
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MacroEditorViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsCapturingKey)
        {
            viewModel.CaptureKey(DescribeKey(e.Key));
            _swallowCapturedKeyUp = true;
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
            case Key.F3:
                viewModel.StepSettingsSelectedCommand.Execute(null);
                break;
            case Key.Delete when !control:
                viewModel.DeleteSelectedCommand.Execute(null);
                break;
            case Key.D when control:
                viewModel.DuplicateSelectedCommand.Execute(null);
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

    /// <summary>Opens the add-action dialog and appends the step it returns.</summary>
    private async void OnAddStepRequested()
    {
        var dialog = new AddActionWindow(null, null, _viewModel.CollectVariables(), MacroNames(),
            null, _assetFolder);
        var step = await dialog.ShowDialog<MacroStep?>(this);

        if (step is not null)
        {
            _viewModel.AddStep(step);
        }
    }

    /// <summary>Opens the dialog on the "run another macro" step and appends what it returns.</summary>
    private async void OnRunMacroRequested()
    {
        var dialog = new AddActionWindow(null, null, _viewModel.CollectVariables(), MacroNames(),
            "control.runMacro", _assetFolder);
        var step = await dialog.ShowDialog<MacroStep?>(this);

        if (step is not null)
        {
            _viewModel.AddStep(step);
        }
    }

    /// <summary>The macro names a "run another macro" step can be pointed at.</summary>
    private IReadOnlyList<string> MacroNames()
    {
        var names = new List<string>();
        foreach (var macro in _project)
        {
            if (macro.Name.Length > 0 && !names.Contains(macro.Name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(macro.Name);
            }
        }

        var current = _viewModel.Name.Trim();
        if (current.Length > 0 && !names.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            names.Add(current);
        }

        return names;
    }

    /// <summary>
    /// The macros a run started here may call, with the macro being edited taken as it stands
    /// rather than as it was last saved.
    /// </summary>
    private ProjectMacroLibrary BuildLibrary()
    {
        var current = _viewModel.Name.Trim();
        var entries = new List<(string Name, IReadOnlyList<ExecutableStep> Steps)>();

        foreach (var macro in _project)
        {
            if (macro.Name.Length == 0
                || (current.Length > 0 && string.Equals(macro.Name, current, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            entries.Add((macro.Name, macro.Steps.ToExecutable()));
        }

        if (current.Length > 0)
        {
            entries.Add((current, _viewModel.Steps.ToExecutable()));
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

        new RunWindow(_viewModel.Steps, null, _viewModel.DelayScale, BuildLibrary()).Show(this);
    }

    /// <summary>Opens the run-speed dialog and keeps whatever factor it was given.</summary>
    private async void OnAdjustDelaysRequested()
    {
        if (await DelayScaleWindow.ShowFor(this, _viewModel.DelayScale, _viewModel.Steps) is { } chosen)
        {
            _viewModel.SetDelayScale(chosen);
        }
    }

    /// <summary>Opens the same dialog for an existing step and swaps in the result.</summary>
    private async void OnEditStepRequested(MacroStep step)
    {
        var dialog = new AddActionWindow(step, null, _viewModel.CollectVariables(), MacroNames(),
            null, _assetFolder);
        var edited = await dialog.ShowDialog<MacroStep?>(this);

        if (edited is not null)
        {
            _viewModel.ReplaceStep(step, edited);
        }
    }

    /// <summary>
    /// Opens the step-settings dialog for one step and keeps whatever it returns, so a step can
    /// be retried, given longer or made to carry on when it fails without leaving the editor.
    /// </summary>
    private async void OnStepSettingsRequested(MacroStep step)
    {
        if (await StepSettingsWindow.ShowFor(this, step.Meta) is { } settings)
        {
            _viewModel.ApplyStepSettings(step, settings);
        }
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

    /// <summary>Prompts for unsaved work before the editor is dismissed.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_allowClose || !_viewModel.IsDirty)
        {
            _closing = true;
            return;
        }

        e.Cancel = true;
        _ = PromptToSaveAsync();
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

        var recorder = new SharpHookRecorder();
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
}
