using System;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>The devices a Windows desktop app can use, all of them real.</summary>
public sealed class WindowsDeviceLayer : IDeviceLayer, IDisposable
{
    private readonly Lazy<IInputDevice> _input;

    private readonly Lazy<IInputRouter> _inputs;

    private readonly Lazy<ViiperInputDevice> _gamepad;

    private readonly Lazy<IScreenDevice> _screen = new(() => new WindowsScreenDevice());

    private readonly Lazy<IVisionDevice> _vision = new(() => new OpenCvVisionDevice());

    private readonly Lazy<IOcrDevice> _ocr = new(() => new PaddleOcrDevice());

    private readonly Lazy<FlaUiDevice> _ui;

    private readonly Lazy<IFileDevice> _files = new(() => new LocalFileDevice());

    private readonly Lazy<IClipboardDevice> _clipboard = new(() => new WindowsClipboardDevice());

    private readonly Lazy<IProcessDevice> _processes = new(() => new WindowsProcessDevice());

    private readonly Lazy<IScriptDevice> _scripts;

    private readonly Lazy<ISystemDevice> _system = new(() => new WindowsSystemDevice());

    private readonly Lazy<IWindowDevice> _windows = new(() => new WindowsWindowDevice());

    private readonly Lazy<IBrowserDevice> _browser = new(() => new PlaywrightBrowserDevice());

    private readonly Lazy<IWindowEvents> _windowEvents = new(() => new WindowsWindowEvents());

    private readonly Lazy<IGamepadReading> _gamepadReading = new(() => new XInputGamepadReading());

    private readonly Lazy<IDriverInput> _driver;

    public WindowsDeviceLayer()
    {
        // The pointer is read from the screen: one device answers where it is, whether a step is
        // about to send input or a picker is following it.
        _input = new Lazy<IInputDevice>(() => new SharpHookInputDevice(_screen.Value));
        _gamepad = new Lazy<ViiperInputDevice>(() => SharedDriverInput.Take(_screen.Value));
        _inputs = new Lazy<IInputRouter>(() => new WindowsInputRouter(_input.Value, _screen.Value));
        _driver = new Lazy<IDriverInput>(() => new ViiperDriver(() => _screen.Value));

        // UI Automation borrows the mouse for the buttons it has no click of its own for.
        _ui = new Lazy<FlaUiDevice>(() => new FlaUiDevice(_input.Value));

        // The package steps start their programs through the same device every other step does, so
        // a long download is timed and its output read the way a command line's is.
        _scripts = new Lazy<IScriptDevice>(() => new ScriptDevice(Processes));
    }

    public IInputDevice Input => _input.Value;

    /// <summary>
    /// The one hook the process installs, shared with every other part of the program: the machine
    /// does not hand a global hook over cleanly twice, so this is not a device to make again. It is
    /// not disposed with the layer for the same reason — it belongs to the process, and the parts
    /// that use it unsubscribe when they are done.
    /// </summary>
    public IInputHook Hook => GlobalInputHook.Shared;

    /// <summary>
    /// A recorder over that hook, made for one recording at a time. The controller is read rather than
    /// listened to, so the recorder is handed this machine's own reading of it.
    /// </summary>
    public IInputRecorder CreateRecorder() => new InputHookRecorder(GamepadReading);

    /// <summary>What the machine says when the windows change, which is a listener rather than a device.</summary>
    public IWindowEvents WindowEvents => _windowEvents.Value;

    /// <summary>The controller in the person's hands, which holds nothing and is read where it is wanted.</summary>
    public IGamepadReading GamepadReading => _gamepadReading.Value;

    /// <summary>The driver and the server behind driver-level input, which stand beside the pair they carry.</summary>
    public IDriverInput Driver => _driver.Value;

    /// <summary>Input sent the way each step asks: in front, to one window, or through a driver.</summary>
    public IInputRouter Inputs => _inputs.Value;

    /// <summary>
    /// The virtual controller a macro drives. It is one of the devices of the program's virtual
    /// keyboard and mouse rather than a connection of its own, so a controller and a click are the
    /// same hardware on the same bus.
    /// </summary>
    public IGamepadDevice Gamepad => _gamepad.Value;

    /// <summary>
    /// How this machine reads the screen, which the settings window keeps up to date: the answers
    /// are the machine's rather than a step's, so they are kept beside the devices rather than in
    /// the screen device, and a running macro picks up a change at its next look.
    /// </summary>
    public ICaptureChoice Capture => CaptureChoice.Current;

    public IScreenDevice Screen => _screen.Value;

    /// <summary>Image matching, which needs the reference picture to be readable.</summary>
    public IVisionDevice Vision => _vision.Value;

    /// <summary>Text recognition, which loads its models the first time a macro reads text.</summary>
    public IOcrDevice Ocr => _ocr.Value;

    public IUiDevice Ui => _ui.Value;

    /// <summary>Files, which need no Windows API of their own.</summary>
    public IFileDevice Files => _files.Value;

    /// <summary>The clipboard, reached through the Win32 calls so no window has to be in front.</summary>
    public IClipboardDevice Clipboard => _clipboard.Value;

    /// <summary>Other programs, which hold no resources of their own until they are used.</summary>
    public IProcessDevice Processes => _processes.Value;

    /// <summary>Scripts, which run in processes of their own and keep their packages beside the program.</summary>
    public IScriptDevice Scripts => _scripts.Value;

    /// <summary>Facts about this machine, which are read on demand.</summary>
    public ISystemDevice System => _system.Value;

    /// <summary>Open windows, reached through the Win32 calls.</summary>
    public IWindowDevice Windows => _windows.Value;

    /// <summary>A browser driven over its own automation protocol, started when a macro asks.</summary>
    public IBrowserDevice Browser => _browser.Value;

    /// <summary>
    /// Lets go of what was opened. The keyboard, mouse and screen hold nothing, so only the
    /// few that do — UI Automation's connection, the loaded text recognition models, the programs
    /// this run started, and the virtual keyboard and mouse a step may have put on the machine —
    /// are worth releasing, and only if they were ever opened.
    /// </summary>
    public void Dispose()
    {
        if (_inputs.IsValueCreated && _inputs.Value is IDisposable inputs)
        {
            inputs.Dispose();
        }

        // Reading a window through graphics capture opens a graphics device of its own; a run that
        // only ever read the desktop has nothing here.
        if (_screen.IsValueCreated && _screen.Value is IDisposable screen)
        {
            screen.Dispose();
        }

        if (_ui.IsValueCreated)
        {
            _ui.Value.Dispose();
        }

        if (_ocr.IsValueCreated && _ocr.Value is IDisposable ocr)
        {
            ocr.Dispose();
        }

        // The programs this run started are held open so their exit codes stay readable.
        if (_processes.IsValueCreated && _processes.Value is IDisposable processes)
        {
            processes.Dispose();
        }

        // A browser left open would keep a window on screen after the macro has finished.
        if (_browser.IsValueCreated && _browser.Value is IDisposable browser)
        {
            browser.Dispose();
        }

        // The listener that watches for windows changing is an event hook of its own.
        if (_windowEvents.IsValueCreated)
        {
            _windowEvents.Value.Dispose();
        }
    }
}
