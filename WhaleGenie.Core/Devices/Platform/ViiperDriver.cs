using System;
using System.Threading.Tasks;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Driver-level input as a whole, which is the shape the device layer holds: what is behind it is
/// the machine's own registry, the program's one virtual keyboard and mouse, and the server they
/// run on — none of which is made again per window or per run.
/// </summary>
internal sealed class ViiperDriver(Func<IScreenDevice> screen) : IDriverInput
{
    public DriverInputState State => DriverInput.Check();

    public bool AttachAtStart { get; set; }

    /// <summary>
    /// Puts the virtual keyboard and mouse on the machine before anybody needs them, when the
    /// settings window was told to.
    ///
    /// That wait is the one thing about driver-level input a user sees: the first move of a macro
    /// that types or clicks would sit there doing nothing while the devices are noticed by the
    /// machine, and look broken. It runs on a thread of its own, and a machine that cannot do it is
    /// not told about it here — the step that needs driver-level input says what is missing, which
    /// is where the user is looking.
    /// </summary>
    public Task WarmUp() => Task.Run(() =>
    {
        if (!AttachAtStart)
        {
            return;
        }

        try
        {
            SharedDriverInput.Take(screen()).Prepare();
        }
        catch
        {
            // Best effort, as above: nothing is waiting for this.
        }
    });

    public void Stop()
    {
        SharedDriverInput.Release();
        ViiperServer.Stop();
    }

    public void HandOver()
    {
        SharedDriverInput.Release();
        ViiperServer.HandOver();
    }
}
