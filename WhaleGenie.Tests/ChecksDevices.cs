using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Tests;

/// <summary>
/// A desktop with whatever windows a check asks for, compared the way the real device compares
/// them: the list has to answer the same question, or the check would only be checking itself.
/// </summary>
internal sealed class WindowsForChecks : IWindowDevice
{
    private readonly Dictionary<long, (string Process, string Class)> _facts = [];

    /// <summary>The windows on this desktop, frontmost first.</summary>
    public List<WindowInfo> Open { get; } = [];

    public WindowsForChecks Add(long handle, string title, string process, string className)
    {
        Open.Add(new WindowInfo(handle, title, new ScreenPoint(0, 0), new ScreenSize(100, 100),
            false, false));
        _facts[handle] = (process, className);
        return this;
    }

    /// <summary>Moves one of the windows somewhere, which is what a frame is drawn around.</summary>
    public WindowsForChecks Place(long handle, int x, int y, int width, int height)
    {
        var at = Open.FindIndex(window => window.Handle == handle);
        Open[at] = Open[at] with
        {
            Location = new ScreenPoint(x, y),
            Size = new ScreenSize(width, height),
        };
        return this;
    }

    /// <summary>Shrinks one of the windows, which takes it off the screen.</summary>
    public WindowsForChecks Shrink(long handle)
    {
        var at = Open.FindIndex(window => window.Handle == handle);
        Open[at] = Open[at] with { Minimized = true };
        return this;
    }

    public IReadOnlyList<WindowInfo> List() => [.. Open];

    public IReadOnlyList<WindowInfo> Filter(string value, WindowMatch match, WindowCompare compare,
        bool everyWhenEmpty = false)
    {
        var wanted = (value ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return everyWhenEmpty || Open.Count == 0 ? [.. Open] : [Open[0]];
        }

        return [.. Open.Where(window => Answers(window, wanted, match, compare))];
    }

    public WindowInfo? Find(string value, WindowMatch match, WindowCompare compare)
        => Filter(value, match, compare).FirstOrDefault();

    public string ProcessOf(long handle)
        => _facts.TryGetValue(handle, out var facts) ? facts.Process : string.Empty;

    public string ClassOf(long handle)
        => _facts.TryGetValue(handle, out var facts) ? facts.Class : string.Empty;

    private bool Answers(WindowInfo window, string wanted, WindowMatch match, WindowCompare compare)
    {
        var text = match switch
        {
            WindowMatch.Process => ProcessOf(window.Handle),
            WindowMatch.ClassName => ClassOf(window.Handle),
            _ => window.Title,
        };

        return compare switch
        {
            WindowCompare.StartsWith => text.StartsWith(wanted, StringComparison.OrdinalIgnoreCase),
            WindowCompare.Regex => Regex.IsMatch(text, wanted, RegexOptions.IgnoreCase),
            _ => text.Contains(wanted, StringComparison.OrdinalIgnoreCase),
        };
    }

    public bool Activate(long handle) => true;

    public bool Minimize(long handle) => true;

    public bool Maximize(long handle) => true;

    public bool Restore(long handle) => true;

    public bool Close(long handle) => true;

    public bool Move(long handle, int x, int y, int width, int height) => true;

    public ScreenPoint ClientOrigin(long handle) => new(0, 0);

    public ScreenSize ClientSize(long handle) => new(0, 0);
}

/// <summary>
/// A device layer holding the windows a check put there and refusing everything else the way a
/// machine with no devices would, so a window can be listed, framed or waited for without a real
/// desktop being read.
/// </summary>
internal sealed class ChecksDevices : IDeviceLayer
{
    public IWindowDevice Windows { get; set; } = NullDeviceLayer.Instance.Windows;

    /// <summary>
    /// A hook a check can drive, so a trigger bound to a key or a button can be answered without a
    /// real keyboard being pressed. The rest of the watching devices answer the way a machine with
    /// nothing to say does, which leaves the caller looking on its own.
    /// </summary>
    public IInputHook Hook { get; set; } = NullDeviceLayer.Instance.Hook;

    /// <summary>The recorder the editor is handed, when what is being checked is recording.</summary>
    public IInputRecorder? Recorder { get; set; }

    public IInputRecorder CreateRecorder() => Recorder ?? NullDeviceLayer.Instance.CreateRecorder();

    public IWindowEvents WindowEvents { get; set; } = new NoWindowEvents();

    /// <summary>
    /// The controller a check can play, so a binding taken from it, a trigger bound to one of its
    /// buttons and a recording of one all work with no controller plugged in. Left alone it answers
    /// the way a machine with nothing plugged in does.
    /// </summary>
    public IGamepadReading GamepadReading { get; set; } = new NoGamepadReading();

    public IDriverInput Driver => NullDeviceLayer.Instance.Driver;

    public IInputDevice Input { get; set; } = NullDeviceLayer.Instance.Input;

    public IInputRouter Inputs => NullDeviceLayer.Instance.Inputs;

    public IGamepadDevice Gamepad => NullDeviceLayer.Instance.Gamepad;

    public ICaptureChoice Capture => NullDeviceLayer.Instance.Capture;

    public IScreenDevice Screen { get; set; } = NullDeviceLayer.Instance.Screen;

    public IVisionDevice Vision => NullDeviceLayer.Instance.Vision;

    /// <summary>The reading of writing a check puts on the machine, when what is being checked is
    /// what a step read off the screen.</summary>
    public IOcrDevice Ocr { get; set; } = NullDeviceLayer.Instance.Ocr;

    public IUiDevice Ui { get; set; } = NullDeviceLayer.Instance.Ui;

    public IFileDevice Files => NullDeviceLayer.Instance.Files;

    /// <summary>The clipboard a check can look at, when what is being checked is text being copied.</summary>
    public IClipboardDevice Clipboard { get; set; } = NullDeviceLayer.Instance.Clipboard;

    public IProcessDevice Processes => NullDeviceLayer.Instance.Processes;

    public ISystemDevice System => NullDeviceLayer.Instance.System;

    public IBrowserDevice Browser => NullDeviceLayer.Instance.Browser;
}

/// <summary>
/// A controller a check plays by hand: what is "held" is whatever the check last set, so a trigger
/// bound to a controller button, a binding taken from one, and a recording of one can all be
/// checked with no controller plugged in and nothing pressed.
/// </summary>
internal sealed class FakeGamepad : IGamepadReading
{
    /// <summary>True unless a check is checking what a machine with nothing plugged in does.</summary>
    public bool Connected { get; set; } = true;

    /// <summary>The buttons the person is holding, which the check sets between readings.</summary>
    public GamepadButtons Down { get; set; }

    public GamepadButtons Held() => Connected ? Down : GamepadButtons.None;
}
