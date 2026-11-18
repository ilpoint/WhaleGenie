using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class ActionEditorView : UserControl
{
    /// <summary>
    /// How long reading the writing off an element takes while a row is being filled in. It is a
    /// question about a page that is already open, so it waits the way the "test" button waits
    /// rather than the half minute a step of its own would take.
    /// </summary>
    private const int PickReadMs = 3000;

    /// <summary>
    /// 菜单里直接列出多少个打开的窗口。再多的就不再一条条排，改成一个「更多」打开列表：一张
    /// 长得要滚的菜单，比列表窗口更难找。
    /// </summary>
    private const int OpenWindowLimit = 12;


    /// <summary>The window this view is standing in, which owns its pickers and dialogs.</summary>
    private Window? HostWindow => TopLevel.GetTopLevel(this) as Window;

    /// <summary>The host's window state, which decides whether the frames it draws are worth drawing.</summary>
    private WindowState HostState => HostWindow?.WindowState ?? WindowState.Normal;

    /// <summary>The file pickers belong to the top level the view is in.</summary>
    private IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)?.StorageProvider
        ?? throw new InvalidOperationException("The form is not attached to a window.");

    /// <summary>Chooses the action whose fields the form shows.</summary>
    internal void SelectAction(string key) => (DataContext as AddActionViewModel)?.SelectAction(key);
    /// <summary>Raised when the form is finished with a step, or with nothing when it was cleared.</summary>
    internal event Action<MacroStep?>? Finished;

    /// <summary>Whether the dialog has been dismissed, which can happen while a page is open.</summary>
    private bool _closed;

    /// <summary>The clock that takes a "ran it" line away again, while one is running.</summary>
    private DispatcherTimer? _runTimer;

    /// <summary>
    /// The keyboard drawn on screen, kept while the dialog is up. A combination is built a key at a
    /// time, so the window stays open and takes the next cap instead of being opened again for it.
    /// </summary>
    private VirtualKeyboardWindow? _keyPad;

    /// <summary>Where the keyboard is writing at the moment: the field or the row that asked for it.</summary>
    private Action<string>? _keyTarget;

    /// <summary>The field waiting for a key pressed on the keyboard, or null when none is.</summary>
    private StepParameterViewModel? _capturingField;

    /// <summary>The press of a run being recorded into, or null when none is.</summary>
    private KeyRowViewModel? _capturingRow;

    /// <summary>The window the capture is heard on, kept so that its handlers come off again.</summary>
    private TopLevel? _captureHost;

    /// <summary>True while something on the form is waiting for a key from the keyboard.</summary>
    internal bool IsCapturingKey => _capturingField is not null || _capturingRow is not null;

    /// <summary>
    /// The modifiers held since the capture began. A modifier that is still down when the capture
    /// starts never comes back as a key press, so what is held has to be remembered here.
    /// </summary>
    private readonly List<Key> _captureModifiers = [];

    /// <summary>
    /// The keys whose release is swallowed after a captured press. A focused button reads Space or
    /// Enter as a click when the key comes up, which would arm the capture all over again.
    /// </summary>
    private readonly List<Key> _swallowReleases = [];

    /// <summary>
    /// The frames standing around the windows the preview lists. They go up and come down with the
    /// list, and with the dialog: a frame outlives the list it belongs to only as an overlay the
    /// user has to get rid of.
    /// </summary>
    private readonly ScreenFrames _previewFrames = new();

    /// <summary>The frame around the window a "try looking" is reading, while it reads it.</summary>
    private readonly ScreenFrames _trialFrames = new();

    /// <summary>The windows the preview has framed on the screen right now, which is what the list
    /// beside the rule is worth looking at: kept where a check can count them.</summary>
    internal IReadOnlyCollection<long> FramedWindows => _previewFrames.Framed;

    /// <summary>The list whose frames are up, so that a dialog moving on to another action stops
    /// watching the one it had.</summary>
    private WindowPreviewViewModel? _watched;

    /// <summary>The step the form was opened on for editing, or null while writing a new one.</summary>
    private MacroStep? _editingStep;

    private IDeviceLayer? _devices;

    /// <summary>
    /// The XAML previewer's entry, and only that: the form is built holding no action and nothing
    /// to choose from, because which action it is for and where that step goes is the editor's to
    /// say. The program never builds the view this way — every window hands it the action.
    /// </summary>
    /// <remarks>
    /// This exists because a XAML file reached without a public parameterless constructor is
    /// reported as unreachable through the runtime loader (AVLN3001). The form is only ever built
    /// by <see cref="InitializeComponent"/> with an action in hand, so the parameterless path is
    /// for the previewer alone.
    /// </remarks>
    public ActionEditorView()
        : this(null, [], null, null, null, null)
    {
    }

    /// <summary>
    /// The devices the test button sends through, and the ones the pickers work over: the layer the
    /// editor handed in, or the layer the whole program shares. A check puts its own here, which is
    /// how a trial is driven without a real keyboard, mouse or screen being touched.
    /// </summary>
    internal IDeviceLayer Devices
    {
        get => _devices ??= SharedDevices.Current;
        set => _devices = value;
    }

    /// <summary>
    /// The macros a run started here may call, handed in by the editor. A step that calls another
    /// macro is run the same way it will be in a run, rather than failing for want of a library the
    /// dialog was never given.
    /// </summary>
    internal IMacroLibrary? Macros { get; set; }

    /// <summary>
    /// Where a step written here goes. The editor takes it because only the editor knows what is
    /// picked in the list and what that place accepts; it answers null once the step is in the
    /// list, or what to say when the place would not take it.
    /// </summary>
    internal Func<MacroStep?, MacroStep, string?>? PlaceStep { get; set; }

    /// <summary>
    /// Where a step written in this form asks for a name nothing in the macro is using yet. The
    /// editor is the one that keeps those names, so it answers; a form opened with nothing behind
    /// it leaves this null and the step it builds goes unnamed until the editor names it.
    /// </summary>
    internal Func<ISet<string>, string>? NextStepId
    {
        get => Form?.NextStepId;
        set
        {
            if (Form is { } form)
            {
                form.NextStepId = value;
            }
        }
    }

    /// <summary>
    /// What to do with a step somebody asked to be shown while filling a field in — the reverse
    /// lookup of a variable: which steps read it, and where each of them is. The editor is the only
    /// one that has a step list, so it answers this; null where there is nothing to show it in.
    /// </summary>
    internal Action<MacroStep>? LookAtStep { get; set; }

    /// <summary>The form this window is driving.</summary>
    private AddActionViewModel? Form => DataContext as AddActionViewModel;

    /// <summary>
    /// Opens the dialog, optionally preloaded with a step being edited. <paramref name="actions"/>
    /// is what the place a step is going accepts, which only the editor knows.
    /// </summary>
    public ActionEditorView(MacroStep? existing, IReadOnlyList<ActionDefinition> actions,
        IReadOnlyList<VariableChoice>? variables = null, IReadOnlyList<MacroCallable>? macros = null,
        string? presetKey = null, string? assetFolder = null,
        IReadOnlyList<ActionParameterOption>? steps = null, string? newStepId = null,
        IReadOnlyList<WindowRuleChoice>? windowRules = null, bool followsWindowAbove = false,
        string? windowAbove = null, PersistedVariables? persisted = null,
        VariableSession? session = null, IReadOnlyCollection<string>? loopNames = null)
    {
        InitializeComponent();

        var viewModel = new AddActionViewModel(actions, variables, macros, steps, newStepId,
            windowRules, followsWindowAbove, windowAbove, loopNames)
        {
            Persisted = persisted ?? new PersistedVariables(),
            Variables = session ?? new VariableSession(),
        };
        viewModel.AssetFolder = assetFolder ?? string.Empty;

        // Picking a position has to know where the window a step is anchored to sits right now,
        // so what the pointer is over is stored the way the engine will read it back. The same goes
        // for a step anchored to a control, which needs the control's own rectangle.
        viewModel.Windows = Devices.Windows;
        viewModel.Ui = Devices.Ui;
        viewModel.Clipboard = Devices.Clipboard;

        if (existing is not null)
        {
            _editingStep = existing;
            viewModel.LoadFrom(existing);
        }
        else if (!string.IsNullOrEmpty(presetKey))
        {
            viewModel.SelectAction(presetKey);
        }

        DataContext = viewModel;
        viewModel.PropertyChanged += OnFormChanged;
        WatchPreview();
        viewModel.CloseRequested += OnFormFinished;

        // Tunnelling so Escape cancels before any focused editor consumes it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // A macro's name is picked rather than typed: there is nothing to remember it by, and no
        // button beside that box to ask for the list. Every other name box is a value box — the
        // names are offered while typing, and the button beside it opens the whole list — so
        // clicking into one of those must not cover the form with every name the macro knows.
        AddHandler(PointerPressedEvent, OnMacroFieldPressed, RoutingStrategies.Tunnel);
    }

    private void OnMacroFieldPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual visual
            && visual.FindAncestorOfType<AutoCompleteBox>(true) is
            { DataContext: StepParameterViewModel { IsMacro: true, Text.Length: 0 } } picker
            && picker.ItemsSource is { } items
            && items.Cast<object>().Any())
        {
            picker.IsDropDownOpen = true;
        }
    }

    /// <summary>
    /// Listens for the keys of a capture on the window the form stands in rather than on the form:
    /// a key press is raised at whatever holds the focus, and tunnelling down from the window is
    /// what catches it wherever in the form that is. Handled events are taken as well, because a
    /// focused button reads Space or Enter as a click of its own on the way.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _captureHost = TopLevel.GetTopLevel(this);
        _captureHost?.AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _captureHost?.AddHandler(KeyUpEvent, OnCaptureKeyUp, RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    /// <summary>
    /// Opens at a size the desktop can hold. The four pages are read comfortably in a thousand
    /// pixels of width, but a 1366-wide laptop has less than that to give once the window is kept
    /// clear of its edges — so the size wanted is trimmed to the room there is rather than the
    /// dialog hanging off the screen. And it stands beside the window it was opened from wherever
    /// the desktop has room for it: a step is written with the list it is going into in sight, not
    /// with that list covered up.
    /// </summary>
    /// <summary>
    /// A dialog put away for a moment — the same one that stands aside while a picker is open —
    /// takes its frames with it rather than leaving them over whatever is being picked.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            DrawPreviewFrames();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 38)
        {
            HostWindow?.BeginMoveDrag(e);
        }
    }

    /// <summary>Noted so a pick that outlives the dialog does not try to report back to it.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _closed = true;
        EndKeyCapture();

        if (_captureHost is { } host)
        {
            host.RemoveHandler(KeyDownEvent, OnCaptureKeyDown);
            host.RemoveHandler(KeyUpEvent, OnCaptureKeyUp);
            _captureHost = null;
        }

        // The frames belong to this dialog: a list that stayed framed after the window it was read
        // in went away would be an overlay with nothing left to explain it.
        Form?.PropertyChanged -= OnFormChanged;
        _watched?.PropertyChanged -= OnPreviewChanged;
        _watched = null;
        _previewFrames.Dispose();
        _trialFrames.Dispose();

        // The layer is not the dialog's to let go of: it is the one the whole program shares, so
        // disposing it here would take the loaded models with it. Letting go of the reference is
        // all that is needed, and a trial's controller goes when the program does.
        _devices = null;

        base.OnDetachedFromVisualTree(e);
    }
}
