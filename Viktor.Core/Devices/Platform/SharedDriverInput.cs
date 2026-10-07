namespace Viktor.Core.Devices.Platform;

/// <summary>
/// The virtual keyboard and mouse this program sends driver-level input through.
///
/// The machine answers only the pair that got to it first: a second pair arrives attached and is
/// then ignored, and stays that way until the first one goes away. So the whole program takes
/// shares in one pair rather than each run putting on its own, and the pair leaves the machine only
/// when the last share is given back — otherwise a run that opened its own device layer would take
/// a working keyboard and mouse away from a macro that is still using them.
/// </summary>
internal static class SharedDriverInput
{
    private static readonly object Pool = new();

    private static ViiperInputDevice? _device;

    private static int _shares;

    /// <summary>Takes a share of the virtual keyboard and mouse.</summary>
    public static ViiperInputDevice Take()
    {
        lock (Pool)
        {
            _device ??= new ViiperInputDevice();
            _shares++;
            return _device;
        }
    }

    /// <summary>
    /// Gives back a share taken with <see cref="Take"/>. The one who gives back the last share takes
    /// the devices off the machine.
    /// </summary>
    public static void Give()
    {
        ViiperInputDevice? leaving;
        lock (Pool)
        {
            // A share given back that was never taken would take the pair off a run still using it.
            if (_shares == 0)
            {
                return;
            }

            if (--_shares > 0)
            {
                return;
            }

            leaving = _device;
            _device = null;
        }

        leaving?.Dispose();
    }
}
