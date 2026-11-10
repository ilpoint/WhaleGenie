using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Devices;

// Every device a macro can reach, gathered into the one contract the engine takes.
/// <summary>Everything a macro can do to the machine, in one place.</summary>
public interface IDeviceLayer
{
    /// <summary>
    /// The keyboard and the mouse as this machine reports them, which is what the triggers, the
    /// pickers and the recorder listen to. It is here rather than inside the devices because the
    /// machine allows one global hook per process: whatever needs the events subscribes to this
    /// one while it works, and a layer handed in by a check can carry a hook that is not the real
    /// one.
    /// </summary>
    IInputHook Hook { get; }

    /// <summary>
    /// A recorder for this machine's input, one per recording. Recording is not something a macro
    /// does while it runs, so it is made on demand rather than held: a recorder that outlived its
    /// recording would go on taking subscriptions for a keyboard nobody is writing down.
    /// </summary>
    IInputRecorder CreateRecorder();

    /// <summary>
    /// The word that something about the windows changed, which is how a window that appears and
    /// goes again between two looks is still noticed. A machine with nothing to say answers with
    /// one whose <see cref="IWindowEvents.Available"/> is false, and the caller goes on looking.
    /// </summary>
    IWindowEvents WindowEvents { get; }

    /// <summary>
    /// The controller in the person's hands, read rather than listened to. It is here beside the
    /// hook for the same reason the hook is: a trigger bound to a controller button and a recording
    /// of one both read it, and a layer handed in by a check carries a controller the check can
    /// play rather than the one on the machine.
    /// </summary>
    IGamepadReading GamepadReading { get; }

    /// <summary>
    /// Driver-level input, which rests on two pieces of other projects: a kernel driver that is
    /// installed once, and a server that has to be running. Holding it here is what lets the window
    /// that explains it be driven without either one being on the machine.
    /// </summary>
    IDriverInput Driver { get; }

    IInputDevice Input { get; }

    /// <summary>
    /// The input devices a step can choose between. A layer with only one way in answers with
    /// that one whatever the step asks for.
    /// </summary>
    IInputRouter Inputs { get; }

    /// <summary>
    /// The virtual controller a macro drives, which is a device of its own rather than another way
    /// of sending keyboard and mouse input. A layer with no controller refuses every step that
    /// asks for one.
    /// </summary>
    IGamepadDevice Gamepad { get; }

    /// <summary>
    /// How this machine reads a picture of the screen. It is beside the devices rather than inside
    /// the screen device because it is a choice about this machine that every device layer has an
    /// answer to, rather than a way of reading pixels that a device may or may not offer.
    /// </summary>
    ICaptureChoice Capture { get; }

    /// <summary>
    /// How often this machine is willing to hand over a picture, and how long one already handed
    /// over stays worth reading again. Here beside the capture choice and for the same reason: it
    /// is an answer about the machine and not a way of reading pixels, and a layer handed in by a
    /// check can hold a policy the check sets instead of the one the settings window stores.
    /// </summary>
    IFramePolicy Frames => FramePolicy.Current;

    IScreenDevice Screen { get; }

    IVisionDevice Vision { get; }

    IOcrDevice Ocr { get; }

    /// <summary>
    /// Looking for things in a picture with a model the macro brought along. A layer with no model
    /// behind it refuses every step that asks for one.
    /// </summary>
    IDetectorDevice Detector { get; }

    IUiDevice Ui { get; }

    IFileDevice Files { get; }

    IClipboardDevice Clipboard { get; }

    IProcessDevice Processes { get; }

    /// <summary>
    /// Scripts of the languages this program runs itself, and the packages they ask for. A script
    /// runs in a process of its own with a way back into the macro, so it is a device like the
    /// browser is rather than something the engine does by hand.
    /// </summary>
    IScriptDevice Scripts { get; }

    ISystemDevice System { get; }

    IWindowDevice Windows { get; }

    /// <summary>The browser a macro drives, when the actions for it are used.</summary>
    IBrowserDevice Browser { get; }
}
