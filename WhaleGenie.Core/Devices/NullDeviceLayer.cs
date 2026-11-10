using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Devices;

// A device layer that has nothing behind it, for a run that only walks the macro.
/// <summary>
/// A device layer with nothing behind it. Every call is refused, which is what lets the
/// engine be tested, and lets a macro run on a machine with no devices attached.
/// </summary>
public sealed class NullDeviceLayer : IDeviceLayer
{
    /// <summary>The shared instance, which holds no state.</summary>
    public static NullDeviceLayer Instance { get; } = new();

    private readonly Refusal _refusal = new();

    /// <summary>
    /// A hook that refuses every subscription, the way the rest of this layer refuses every call:
    /// a machine with no devices has nothing to say about the keyboard either.
    /// </summary>
    public IInputHook Hook => _refusal;

    public IInputRecorder CreateRecorder() => _refusal;

    /// <summary>
    /// A machine that says nothing about its windows, which is an answer rather than a refusal:
    /// the caller keeps looking on its own, exactly as it would on a Windows too old to say.
    /// </summary>
    public IWindowEvents WindowEvents { get; } = new NoWindowEvents();

    /// <summary>
    /// No controller to read, which is an answer rather than a refusal: a trigger bound to a
    /// controller button is simply never answered, the way it is on a machine with nothing plugged in.
    /// </summary>
    public IGamepadReading GamepadReading { get; } = new NoGamepadReading();

    public IDriverInput Driver => _refusal;

    public IInputDevice Input => _refusal;

    /// <summary>This layer has no second way to send input, so every route is refused alike.</summary>
    public IInputRouter Inputs { get; } = new SingleInputRouter(new Refusal());

    /// <summary>Nothing here can be a controller.</summary>
    public IGamepadDevice Gamepad => _refusal;

    /// <summary>
    /// The automatic choice, which is what the engine takes to mean "read it the way this machine
    /// would read it anyway": a layer with no devices has no other answer to give.
    /// </summary>
    public ICaptureChoice Capture { get; } = CaptureChoice.Current;

    public IScreenDevice Screen => _refusal;

    public IVisionDevice Vision => _refusal;

    public IOcrDevice Ocr => _refusal;

    public IDetectorDevice Detector => _refusal;

    public IUiDevice Ui => _refusal;

    public IFileDevice Files => _refusal;

    public IClipboardDevice Clipboard => _refusal;

    public IProcessDevice Processes => _refusal;

    public IScriptDevice Scripts => _refusal;

    public ISystemDevice System => _refusal;

    public IWindowDevice Windows => _refusal;

    public IBrowserDevice Browser => _refusal;

    /// <summary>Answers every request with "not available", naming what was asked for.</summary>
    private sealed class Refusal
        : IInputDevice, IGamepadDevice, IScreenDevice, IVisionDevice, IOcrDevice, IDetectorDevice,
          IUiDevice, IFileDevice, IClipboardDevice, IProcessDevice, IScriptDevice, ISystemDevice,
          IWindowDevice, IBrowserDevice, IInputHook, IInputRecorder, IDriverInput
    {
        // Named through the interface because the input device already has a KeyDown and a KeyUp
        // that send a key rather than report one.
        event Action<KeyHookEvent>? IInputHook.KeyDown
        {
            add => throw Missing("the keyboard");
            remove => throw Missing("the keyboard");
        }

        event Action<KeyHookEvent>? IInputHook.KeyUp
        {
            add => throw Missing("the keyboard");
            remove => throw Missing("the keyboard");
        }

        event Action<MouseHookEvent>? IInputHook.MouseDown
        {
            add => throw Missing("the mouse");
            remove => throw Missing("the mouse");
        }

        event Action<MouseHookEvent>? IInputHook.MouseUp
        {
            add => throw Missing("the mouse");
            remove => throw Missing("the mouse");
        }

        event Action<MouseMovedHookEvent>? IInputHook.Moved
        {
            add => throw Missing("the mouse");
            remove => throw Missing("the mouse");
        }

        event Action<MouseMovedRelativeHookEvent>? IInputHook.MovedRelative
        {
            add => throw Missing("the mouse");
            remove => throw Missing("the mouse");
        }

        event Action<WheelHookEvent>? IInputHook.Wheel
        {
            add => throw Missing("the mouse");
            remove => throw Missing("the mouse");
        }

        public bool IsRunning => false;

        public bool IgnoreInjected { get; set; }

        /// <summary>
        /// A machine with no recorder records no controller either; the switch is answered rather than
        /// refused because getting a recorder at all is what is refused, before this is ever asked.
        /// </summary>
        public bool RecordController { get; set; } = true;

        /// <summary>The same for the shortcut that ends a recording: asked for, never acted on here.</summary>
        public string StopShortcut { get; set; } = string.Empty;

        event Action<RecordedInput>? IInputRecorder.Captured
        {
            add => throw Missing("a recorder");
            remove => throw Missing("a recorder");
        }

        event Action? IInputRecorder.StopRequested
        {
            add => throw Missing("a recorder");
            remove => throw Missing("a recorder");
        }

        public void Start() => throw Missing("a recorder");

        public void Stop() => throw Missing("a recorder");

        public void Dispose() => throw Missing("a recorder");

        public DriverInputState State => throw Missing("driver-level input");

        public bool AttachAtStart
        {
            get => throw Missing("driver-level input");
            set => throw Missing("driver-level input");
        }

        public System.Threading.Tasks.Task WarmUp() => throw Missing("driver-level input");

        public void HandOver() => throw Missing("driver-level input");

        public void Connect() => throw Missing("a controller");

        public void Button(string button, bool down) => throw Missing("a controller");

        public void Stick(string stick, int x, int y) => throw Missing("a controller");

        public void Trigger(string trigger, int amount) => throw Missing("a controller");

        public void ReleaseAll() => throw Missing("a controller");

        public bool Ready(string browser) => throw Missing("a browser");

        public string InstallHint => throw Missing("a browser");

        public bool IsOpen => throw Missing("a browser");

        public void Open(string browser, string url, bool headless, string profile)
            => throw Missing("a browser");

        public void GoTo(string url, int timeoutMs) => throw Missing("a browser");

        public string Url => throw Missing("a browser");

        public string Click(PageTarget target, ClickWay how, bool force, int timeoutMs)
            => throw Missing("a browser");

        public string Fill(PageTarget target, string text, FillWay how, int intervalMs, int timeoutMs)
            => throw Missing("a browser");

        public string Text(PageTarget? target, PagePart part, string attribute, int timeoutMs)
            => throw Missing("a browser");

        public IReadOnlyList<string> ReadAll(PageTarget target, PagePart part, string attribute,
            int maxItems, int timeoutMs) => throw Missing("a browser");

        public PageTable ReadTable(PageTarget target, int maxRows, int timeoutMs)
            => throw Missing("a browser");

        public PageInfo ReadPage(int timeoutMs) => throw Missing("a browser");

        public bool Exists(PageTarget target, ElementWait state) => throw Missing("a browser");

        public void WaitForUrl(string match, string how, int timeoutMs,
            TextCompareOptions options) => throw Missing("a browser");

        public void WaitForLoad(PageLoad state, int timeoutMs) => throw Missing("a browser");

        public void Scroll(PageTarget? target, ScrollWay how, int timeoutMs)
            => throw Missing("a browser");

        public DownloadResult Download(PageTarget? target, string folder, int timeoutMs)
            => throw Missing("a browser");

        public void ArmDialog(bool accept, string text) => throw Missing("a browser");

        public string? TakeDialog() => throw Missing("a browser");

        public string Evaluate(string script, int timeoutMs) => throw Missing("a browser");

        public void SwitchTab(TabChoice choice, int index, string match) => throw Missing("a browser");

        public void CloseTab() => throw Missing("a browser");

        public PickResult Pick(string hint, int timeoutMs) => throw Missing("a browser");

        public HighlightResult Highlight(PageTarget target) => throw Missing("a browser");

        public string Select(PageTarget target, string value, bool byIndex, int timeoutMs)
            => throw Missing("a browser");

        public string Check(PageTarget target, bool wanted, int timeoutMs) => throw Missing("a browser");

        public string Hover(PageTarget target, int timeoutMs) => throw Missing("a browser");

        public string Press(PageTarget? target, string key, int timeoutMs) => throw Missing("a browser");

        public string? WaitFor(PageTarget target, ElementWait state, int timeoutMs)
            => throw Missing("a browser");

        public string Upload(PageTarget target, IReadOnlyList<string> paths, int timeoutMs)
            => throw Missing("a browser");

        public void Close() => throw Missing("a browser");

        public string BaseFolder => throw Missing("files");

        public string Resolve(string path) => throw Missing("files");

        public bool Exists(string path) => throw Missing("files");

        public string ReadText(string path, string encoding) => throw Missing("files");

        public void WriteText(string path, string text, bool append, string encoding)
            => throw Missing("files");

        public bool HasOpenLine(string path, string encoding) => throw Missing("files");

        public byte[] ReadBytes(string path) => throw Missing("files");

        public void WriteBytes(string path, byte[] bytes) => throw Missing("files");

        public Stream OpenRead(string path) => throw Missing("files");

        public void Delete(string path, bool toRecycleBin) => throw Missing("files");

        public void Copy(string from, string to) => throw Missing("files");

        public void Move(string from, string to) => throw Missing("files");

        public void CreateFolder(string path) => throw Missing("files");

        public void DeleteFolder(string path, bool recurse) => throw Missing("files");

        public void Unzip(string from, string folder, bool overwrite) => throw Missing("files");

        public void Zip(string folder, string to, bool includeTopFolder) => throw Missing("files");

        public IReadOnlyList<FileEntry> List(string folder, IReadOnlyList<string> patterns, bool recurse,
            int depth, ListKind kind = ListKind.Files) => throw Missing("files");

        public int ChangeCount => throw Missing("the clipboard");

        public bool HasText => throw Missing("the clipboard");

        public ImageFrame? ReadImage() => throw Missing("the clipboard");

        public string ReadText() => throw Missing("the clipboard");

        public void WriteText(string text) => throw Missing("the clipboard");

        public void WriteImage(ImageFrame image) => throw Missing("the clipboard");

        public IReadOnlyList<string> ReadFiles() => throw Missing("the clipboard");

        public void WriteFiles(IReadOnlyList<string> paths) => throw Missing("the clipboard");

        public void Clear() => throw Missing("the clipboard");

        public int Start(StartRequest request) => throw Missing("other programs");

        public IReadOnlyList<int> Find(string value, string compare, TextCompareOptions options)
            => throw Missing("other programs");

        public IReadOnlyList<string> List() => throw Missing("other programs");

        public bool HasExited(int id) => throw Missing("other programs");

        public int? ExitCode(int id) => throw Missing("other programs");


        public bool StopById(int id, bool force) => throw Missing("other programs");

        public CommandResult Run(CommandRequest request) => throw Missing("command lines");

        public ScriptResult RunPython(PythonScript script, IScriptHost host, Action<string> onOutput,
            Action<string> onError, System.Threading.CancellationToken token)
            => throw Missing("Python scripts");

        public ScriptResult InstallPip(PipRequest request, Action<string> onOutput,
            Action<string> onError, System.Threading.CancellationToken token)
            => throw Missing("Python packages");

        public ScriptResult RunCSharp(CSharpScript script, IScriptHost host, Action<string> onOutput,
            Action<string> onError, System.Threading.CancellationToken token)
            => throw Missing("C# scripts");

        public ScriptResult RunAutoHotkey(AutoHotkeyScript script, IScriptHost host,
            Action<string> onOutput, Action<string> onError,
            System.Threading.CancellationToken token)
            => throw Missing("AutoHotkey scripts");

        public ScriptResult InstallNuGet(NuGetRequest request, Action<string> onOutput,
            Action<string> onError, System.Threading.CancellationToken token)
            => throw Missing("NuGet packages");

        public ProcessDetails? Details(int id) => throw Missing("other programs");

        public string Info(string field) => throw Missing("system information");

        public string Environment(string name) => throw Missing("environment variables");

        public string SetEnvironment(string name, string value, EnvironmentScope scope)
            => throw Missing("environment variables");

        public string ReadRegistry(RegistryBranch branch, string path, string valueName)
            => throw Missing("the registry");

        public string WriteRegistry(RegistryBranch branch, string path, string valueName,
            RegistryValueType type, string data) => throw Missing("the registry");

        public void DeleteRegistry(RegistryBranch branch, string path, string valueName)
            => throw Missing("the registry");

        public void Power(PowerAction action, int graceSeconds) => throw Missing("power");

        public int Volume() => throw Missing("the sound card");

        public void SetVolume(int percent) => throw Missing("the sound card");

        public bool IsMuted() => throw Missing("the sound card");

        public void SetMuted(bool muted) => throw Missing("the sound card");

        public void PlaySound(SoundKind kind) => throw Missing("the sound card");

        public void PlaySoundFile(string path) => throw Missing("the sound card");

        public void Notify(string title, string text, NotificationKind kind)
            => throw Missing("somewhere to show notifications");

        public string InputMethod() => throw Missing("a keyboard layout");

        public IReadOnlyList<string> InputMethods() => throw Missing("a keyboard layout");

        public string? SwitchInputMethod(string layout) => throw Missing("a keyboard layout");

        public int Brightness() => throw Missing("an adjustable screen");

        public void SetBrightness(int percent) => throw Missing("an adjustable screen");

        // Named through the interface because the process device already has a List and a Find
        // that take no such argument.
        IReadOnlyList<WindowInfo> IWindowDevice.List() => throw Missing("windows");

        IReadOnlyList<WindowInfo> IWindowDevice.Filter(string value, WindowMatch match,
            string compare, bool everyWhenEmpty, TextCompareOptions options)
            => throw Missing("windows");

        WindowInfo? IWindowDevice.Find(string value, WindowMatch match, string compare,
            TextCompareOptions options)
            => throw Missing("windows");

        string IWindowDevice.ProcessOf(long handle) => throw Missing("windows");

        int IWindowDevice.ProcessIdOf(long handle) => throw Missing("windows");

        string IWindowDevice.ClassOf(long handle) => throw Missing("windows");

        bool IWindowDevice.Activate(long handle) => throw Missing("windows");

        bool IWindowDevice.Minimize(long handle) => throw Missing("windows");

        bool IWindowDevice.Maximize(long handle) => throw Missing("windows");

        bool IWindowDevice.Restore(long handle) => throw Missing("windows");

        bool IWindowDevice.Close(long handle) => throw Missing("windows");

        bool IWindowDevice.Move(long handle, int x, int y, int width, int height)
            => throw Missing("windows");

        ScreenPoint IWindowDevice.ClientOrigin(long handle) => throw Missing("windows");

        ScreenSize IWindowDevice.ClientSize(long handle) => throw Missing("windows");

        public ScreenPoint Cursor => throw Missing("the pointer position");

        public ScreenLayout Desktop => throw Missing("the screen");

        public void KeyPress(string key, int holdMs) => throw Missing("the keyboard");

        public void KeyDown(string key) => throw Missing("the keyboard");

        public void KeyUp(string key) => throw Missing("the keyboard");

        public void Hotkey(IReadOnlyList<string> keys, int holdMs) => throw Missing("the keyboard");

        public void TypeText(string text, int intervalMs) => throw Missing("the keyboard");

        public void MoveMouse(int x, int y, int durationMs) => throw Missing("the mouse");

        public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
            => throw Missing("the mouse");

        public void MoveMouseRelative(int dx, int dy, int durationMs) => throw Missing("the mouse");

        public void MouseDown(string button, int x, int y) => throw Missing("the mouse");

        public void MouseUp(string button, int x, int y) => throw Missing("the mouse");

        public void Click(string button, int x, int y, int clicks, int intervalMs) => throw Missing("the mouse");

        public void Scroll(string direction, int delta, int x, int y) => throw Missing("the mouse");

        public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps)
            => throw Missing("the mouse");

        public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
            => throw Missing("the mouse");

        public PixelColor PixelAt(int x, int y) => throw Missing("the screen");

        public ImageFrame Capture(int x, int y, int width, int height) => throw Missing("the screen");

        public ScreenShot Capture(ScreenCaptureRequest request) => throw Missing("the screen");

        public ImageFrame? Load(string path) => throw Missing("image matching");

        public IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle,
            VisionQuery query) => throw Missing("image matching");

        public IReadOnlyList<TextSpan> Recognize(ImageFrame frame) => throw Missing("text recognition");

        public IReadOnlyList<DetectionHit> Detect(string modelPath, ImageFrame frame,
            DetectionQuery query) => throw Missing("object detection");

        public bool Exists(UiQuery query, int timeoutMs) => throw Missing("UI Automation");

        public UiElementInfo? ElementAt(int x, int y) => throw Missing("UI Automation");

        public bool Click(UiQuery query, string button) => throw Missing("UI Automation");

        public bool Invoke(UiQuery query) => throw Missing("UI Automation");

        public UiElementFacts? Facts(UiQuery query) => throw Missing("UI Automation");

        public bool FocusWindow(UiQuery query) => throw Missing("UI Automation");

        public bool SetText(UiQuery query, string text, bool clearFirst) => throw Missing("UI Automation");

        public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit)
            => throw Missing("UI Automation");

        public bool Select(UiQuery query, string text, int itemIndex) => throw Missing("UI Automation");

        public bool SetChecked(UiQuery query, bool? state) => throw Missing("UI Automation");

        public bool SetExpanded(UiQuery query, string action) => throw Missing("UI Automation");

        public bool ScrollIntoView(UiQuery query) => throw Missing("UI Automation");

        public UiTable ReadTable(UiQuery query, int limit) => throw Missing("UI Automation");

        private static DeviceUnavailableException Missing(string capability) => new(capability);
    }
}
