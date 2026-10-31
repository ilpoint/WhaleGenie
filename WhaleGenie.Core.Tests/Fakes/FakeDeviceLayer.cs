using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The whole machine as the engine sees it: one layer of devices, each of them pretended. What the
/// layer itself does is compose them and answer the questions that belong to no single device —
/// how input is routed, that there is no hook, no recorder and no driver-level input.
/// </summary>
internal sealed class FakeDeviceLayer : IDeviceLayer
{
    /// <summary>
    /// What every device here shares: the one log of calls, and the two switches that make a device
    /// refuse. The devices below hold it rather than the layer, because a device used on its own
    /// needs the same two answers.
    /// </summary>
    public FakeDesk Desk { get; } = new();

    /// <summary>The pretend disk. Whatever the layer is asked about files, this device answers.</summary>
    public FakeFiles Files { get; }

    /// <summary>The pretend clipboard.</summary>
    public FakeClipboard Clipboard { get; }

    /// <summary>The pretend list of running programs.</summary>
    public FakeProcesses Processes { get; }

    /// <summary>The pretend machine: its name, its environment and its settings.</summary>
    public FakeSystem System { get; }

    /// <summary>The pretend screen: what it shows, where the pointer is, what was read of it.</summary>
    public FakeScreen Screen { get; }

    /// <summary>The pretend vision: what a reference picture reads as, and where it turns up.</summary>
    public FakeVision Vision { get; }

    /// <summary>What recognition reads off a picture, and which pictures it was handed.</summary>
    public FakeOcr Ocr { get; }

    /// <summary>The pretend detection model, and every look the engine asked of it.</summary>
    public FakeDetector Detector { get; }

    /// <summary>The pretend keyboard and mouse, with the routes and paths the engine asked for.</summary>
    public FakeInput Input { get; }

    /// <summary>The pretend controller.</summary>
    public FakeGamepad Gamepad { get; }

    /// <summary>The pretend window list, and the commands that act on one of them.</summary>
    public FakeWindow Window { get; }

    /// <summary>The pretend UI Automation tree.</summary>
    public FakeUi Ui { get; }

    /// <summary>The pretend browser.</summary>
    public FakeBrowser Browser { get; }

    /// <summary>The pretend script hosts.</summary>
    public FakeScript Script { get; }

    /// <summary>Every call the engine made to any device, in the order it made them.</summary>
    public List<string> Calls => Desk.Calls;

    private readonly RecordingRouter _router;

    private readonly IFileDevice _files;

    public FakeDeviceLayer(IFileDevice? files = null)
    {
        Files = new FakeFiles(Desk);
        Clipboard = new FakeClipboard(Desk);
        Processes = new FakeProcesses(Desk);
        System = new FakeSystem(Desk);
        Screen = new FakeScreen(Desk);
        Vision = new FakeVision(Desk);
        Ocr = new FakeOcr(Desk);
        Detector = new FakeDetector(Desk);
        Input = new FakeInput(Desk, Screen);
        Gamepad = new FakeGamepad(Desk);
        Window = new FakeWindow(Desk);
        Ui = new FakeUi(Desk);
        Browser = new FakeBrowser(Desk);
        Script = new FakeScript(Desk);
        _router = new RecordingRouter(Input);
        _files = files ?? Files;
    }

    IInputRouter IDeviceLayer.Inputs => _router;

    /// <summary>When set, every call is refused this way instead of being recorded.</summary>
    public System.Exception? Refusal
    {
        get => Desk.Refusal;
        set => Desk.Refusal = value;
    }

    /// <summary>When set, every call reports the device as missing.</summary>
    public bool Unavailable
    {
        get => Desk.Unavailable;
        set => Desk.Unavailable = value;
    }

    IInputDevice IDeviceLayer.Input => Input;

    IGamepadDevice IDeviceLayer.Gamepad => Gamepad;

    /// <summary>How this machine has been told to read the screen.</summary>
    ICaptureChoice IDeviceLayer.Capture => Screen.Ways;

    IScreenDevice IDeviceLayer.Screen => Screen;

    /// <summary>
    /// A hook, a recorder and driver-level input are not something a macro's steps reach for, so
    /// this layer answers the way a machine with no devices would: it refuses. A check that needs
    /// one of them drives it through a layer of its own.
    /// </summary>
    internal TestInputHook HookEvents { get; } = new();

    IInputHook IDeviceLayer.Hook => HookEvents;

    IInputRecorder IDeviceLayer.CreateRecorder() => NullDeviceLayer.Instance.CreateRecorder();

    IWindowEvents IDeviceLayer.WindowEvents => new NoWindowEvents();

    /// <summary>No controller is in a macro's steps, so this layer answers as a machine with none.</summary>
    IGamepadReading IDeviceLayer.GamepadReading => new NoGamepadReading();

    IDriverInput IDeviceLayer.Driver => NullDeviceLayer.Instance.Driver;

    IVisionDevice IDeviceLayer.Vision => Vision;

    IDetectorDevice IDeviceLayer.Detector => Detector;

    IOcrDevice IDeviceLayer.Ocr => Ocr;

    IUiDevice IDeviceLayer.Ui => Ui;

    /// <summary>
    /// The files on disk. Normally the fake keeps them in memory; a check that has to walk over a
    /// real file hands the real device in here and the rest of the layer stays pretended.
    /// </summary>
    IFileDevice IDeviceLayer.Files => _files;

    IClipboardDevice IDeviceLayer.Clipboard => Clipboard;

    IProcessDevice IDeviceLayer.Processes => Processes;

    IScriptDevice IDeviceLayer.Scripts => Script;

    ISystemDevice IDeviceLayer.System => System;

    IWindowDevice IDeviceLayer.Windows => Window;

    IBrowserDevice IDeviceLayer.Browser => Browser;

    /// <summary>
    /// Notes which route input was asked to take, then hands back the device that answers it, so
    /// the call still lands in <see cref="FakeDesk.Calls"/> whatever way it was routed.
    /// </summary>
    private sealed class RecordingRouter(FakeInput owner) : IInputRouter
    {
        public IInputDevice For(InputRoute route)
        {
            owner.Routes.Add(route);
            return owner;
        }
    }
}
