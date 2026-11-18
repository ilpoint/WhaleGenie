namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The virtual keyboard and mouse this program sends driver-level input through.
///
/// The machine answers only the pair that got to it first: a second pair arrives attached and is
/// then ignored, and stays that way until the first one goes away. So the whole program takes
/// shares in one pair rather than each run putting on its own — and the pair stays on the machine
/// once it is there, rather than being taken off at the end of every run and put back on by the
/// next one, which is a wait the first driver-level step of every run would pay for.
/// </summary>
internal static class SharedDriverInput
{
    private static readonly object Pool = new();

    private static ViiperInputDevice? _device;

    /// <summary>
    /// The pair, taken on the first ask. The screen is the one the pair reads the pointer from,
    /// which every caller here is the same layer's.
    /// </summary>
    public static ViiperInputDevice Take(IScreenDevice screen)
    {
        lock (Pool)
        {
            return _device ??= new ViiperInputDevice(screen);
        }
    }

    /// <summary>
    /// Lets the pair go: it comes off the machine, and whoever asks next gets a fresh one. This is
    /// for the moments when this program is done with driver-level input — the way out, and handing
    /// over to a copy of the program that runs with higher rights, which has to be the one the
    /// machine answers.
    /// </summary>
    public static void Release()
    {
        ViiperInputDevice? leaving;
        lock (Pool)
        {
            leaving = _device;
            _device = null;
        }

        leaving?.Dispose();
    }
}
