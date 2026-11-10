using System;
using System.Collections.Generic;
using System.Threading;
using SharpHook.Data;


namespace WhaleGenie.Core.Devices.Platform;

// The link to the driver: waking it, seeking a point and the step it moves by.
public sealed partial class ViiperInputDevice : IInputDevice, IGamepadDevice, IDisposable
{
    /// <summary>
    /// The keyboard and mouse, opened and woken the first time a step — or the warm-up, when the
    /// settings window asked for one — needs them. Opening them is what fails when the driver is
    /// missing or the library will not start, so it happens here rather than when this device is
    /// made, and the run reports it on the step that asked for driver-level input.
    /// </summary>
    private IViiperLink Link()
    {
        if (_link is not null)
        {
            return _link;
        }

        var link = _connect();
        _link = link;

        try
        {
            Wake();
            return link;
        }
        catch
        {
            _link = null;
            link.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Waits for the virtual keyboard and mouse to be devices the machine is really listening to,
    /// and gives up when they never are.
    ///
    /// A device that has just been put on the machine is not one the machine polls yet: everything
    /// sent in the first quarter of a second goes nowhere, so a macro's first move would silently
    /// not happen. Nothing can be read back from a keyboard, but the pointer can be asked to move
    /// and put back, which is what this does — both ways round, in case one of them is off the edge
    /// of the screen, and wait as long as a device takes to be noticed. A device the machine never
    /// notices never moves at all, which is what a device that was never attached to this machine
    /// rather than put on its bus looks like.
    /// </summary>
    private void Wake()
    {
        var at = Pointer();
        foreach (var (dx, dy) in new[] { (Nudge, 0), (-Nudge, 0), (0, Nudge), (0, -Nudge) })
        {
            if (Seek(new ScreenPoint(at.X + dx, at.Y + dy)) && Seek(at))
            {
                return;
            }
        }

        throw new DeviceActionException(RunKeys.NoDriverAttached);
    }

    /// <summary>
    /// Puts the pointer on a point, saying whether it got there.
    ///
    /// A report can go nowhere while the machine is still waking up to a device that has just been
    /// plugged in, and a pointer whose speed the machine boosts moves further than it was told, so
    /// what is left to cover is measured and sent again rather than assumed. Each try after one
    /// that went too far sends less of what remains, which settles rather than swinging wider.
    ///
    /// A point the machine will not allow — past the edge of the screen, say — is not a failure of
    /// the step: the pointer is left as close as the machine lets it be and the macro carries on,
    /// which is what the same move does when it is sent through the front device.
    /// </summary>
    private bool Seek(ScreenPoint to)
    {
        var share = 1.0;
        var at = Pointer();
        for (var attempt = 0; attempt < SeekTries; attempt++)
        {
            var dx = to.X - at.X;
            var dy = to.Y - at.Y;
            if (Math.Abs(dx) <= Arrived && Math.Abs(dy) <= Arrived)
            {
                return true;
            }

            Push(Step(dx, share), Step(dy, share));
            Sleep(SettleMs);

            var now = Pointer();
            if (Math.Abs(to.X - now.X) > Math.Abs(dx) || Math.Abs(to.Y - now.Y) > Math.Abs(dy))
            {
                share /= 2;
            }

            at = now;
        }

        return false;
    }

    /// <summary>How much of the distance left to cover to send, and never nothing at all.</summary>
    private static int Step(int left, double share)
    {
        var step = (int)Math.Round(left * share, MidpointRounding.AwayFromZero);
        return step != 0 ? step : Math.Sign(left);
    }
}
