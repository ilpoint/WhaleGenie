using System;
using System.Collections.Generic;
using System.Threading;
using SharpHook.Data;


namespace WhaleGenie.Platform.Windows;

// Making the device, preparing it, and where the pointer is.
public sealed partial class ViiperInputDevice : IInputDevice, IGamepadDevice, IDisposable
{
    /// <summary>Puts the virtual keyboard and mouse on this machine the first time a step needs them.</summary>
    public ViiperInputDevice(IScreenDevice screen)
        : this(() => ViiperLink.Open(screen))
    {
    }

    /// <summary>
    /// The same, with the way to the machine handed in, so a check can watch what the device sends
    /// without any of those devices existing.
    /// </summary>
    public ViiperInputDevice(Func<IViiperLink> connect) => _connect = connect;

    /// <summary>
    /// Puts the keyboard and mouse on the machine now rather than at the first report, which is what
    /// the settings window's switch asks for when the program starts: waiting for the machine to
    /// notice a device that has just been plugged in is the one part of driver-level input a user
    /// sees, and paying for it here costs nobody anything.
    /// </summary>
    public void Prepare()
    {
        lock (_gate)
        {
            _ = Link();
        }
    }

    public ScreenPoint Cursor
    {
        get
        {
            lock (_gate)
            {
                return Pointer();
            }
        }
    }
}
