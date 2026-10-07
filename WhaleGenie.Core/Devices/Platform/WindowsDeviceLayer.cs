using System;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>The devices a Windows desktop app can use, all of them real.</summary>
public sealed class WindowsDeviceLayer : IDeviceLayer, IDisposable
{
    private readonly Lazy<IInputDevice> _input = new(() => new SharpHookInputDevice());

    private readonly Lazy<IInputRouter> _inputs;

    private readonly Lazy<IScreenDevice> _screen = new(() => new WindowsScreenDevice());

    private readonly Lazy<IVisionDevice> _vision = new(() => new OpenCvVisionDevice());

    private readonly Lazy<IOcrDevice> _ocr = new(() => new PaddleOcrDevice());

    private readonly Lazy<FlaUiDevice> _ui;

    private readonly Lazy<IFileDevice> _files = new(() => new LocalFileDevice());

    private readonly Lazy<IClipboardDevice> _clipboard = new(() => new WindowsClipboardDevice());

    private readonly Lazy<IProcessDevice> _processes = new(() => new WindowsProcessDevice());

    private readonly Lazy<ISystemDevice> _system = new(() => new WindowsSystemDevice());

    private readonly Lazy<IWindowDevice> _windows = new(() => new WindowsWindowDevice());

    private readonly Lazy<IBrowserDevice> _browser = new(() => new PlaywrightBrowserDevice());

    public WindowsDeviceLayer()
    {
        _inputs = new Lazy<IInputRouter>(() => new WindowsInputRouter(_input.Value));

        // UI Automation borrows the mouse for the buttons it has no click of its own for.
        _ui = new Lazy<FlaUiDevice>(() => new FlaUiDevice(_input.Value));
    }

    public IInputDevice Input => _input.Value;

    /// <summary>Input sent the way each step asks: in front, to one window, or through a driver.</summary>
    public IInputRouter Inputs => _inputs.Value;

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
    }
}
