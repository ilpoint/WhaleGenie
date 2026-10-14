using System.Threading.Tasks;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The driver and the server as one object, which is the shape the interface holds: what is behind
/// it is two static stories — one that looks at the machine and one that owns the server the
/// program started — and neither of them is made again per window.
/// </summary>
internal sealed class ViiperDriver : IDriverInput
{
    public DriverInputState State => DriverInput.Check();

    public bool IsServerAnswering() => DriverInput.IsServerAnswering();

    public string? Executable
    {
        get => ViiperServer.Executable;
        set => ViiperServer.Executable = value;
    }

    public Task WarmUp() => ViiperServer.WarmUp();

    public void Stop() => ViiperServer.Stop();

    public void HandOver() => ViiperServer.HandOver();
}
