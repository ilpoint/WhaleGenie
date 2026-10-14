using System.Threading.Tasks;

namespace WhaleGenie.Core.Devices;

/// <summary>How far this machine is from taking input that looks like real hardware.</summary>
public enum DriverInputState
{
    /// <summary>The kernel driver is installed and a VIIPER server is answering.</summary>
    Ready,

    /// <summary>No usbip-win2 kernel driver, so there is no virtual USB device to send into.</summary>
    DriverMissing,

    /// <summary>The driver is there, but nothing is answering where the VIIPER server should be.</summary>
    ServerMissing,
}

/// <summary>
/// The two pieces driver-level input rests on: the usbip-win2 kernel driver and a running VIIPER
/// server, neither of which WhaleGenie ships or installs by itself.
/// </summary>
/// <remarks>
/// It is an interface of its own because both pieces belong to other projects: the settings window
/// has to say what is missing and point at where to get it, and it should be able to say that
/// without anything that watches the registry, holds a socket open or starts a program. The names
/// of those projects belong to whoever installs them, so the window keeps the words and the engine
/// keeps the looking.
/// </remarks>
public interface IDriverInput
{
    /// <summary>How this machine stands right now.</summary>
    DriverInputState State { get; }

    /// <summary>
    /// Whether the server is answering at the moment. Asked separately from <see cref="State"/>
    /// because a server that was started by hand a moment ago may still be coming up, and one the
    /// program started itself is waited for rather than reported missing.
    /// </summary>
    bool IsServerAnswering();

    /// <summary>
    /// The server program the user chose, or null when they have not chosen one. It is a fact about
    /// this machine rather than about the interface, so it is stored here and read back from here.
    /// </summary>
    string? Executable { get; set; }

    /// <summary>Starts the server if it is not up yet, so the first driver-level step does not wait.</summary>
    Task WarmUp();

    /// <summary>
    /// Closes a server this program started. One that was already there was never ours, and is left
    /// running for whoever started it.
    /// </summary>
    void Stop();

    /// <summary>
    /// Lets go of the server without ending the program, which is what restarting with higher
    /// rights needs: the copy that takes over starts its own, and one left running behind it would
    /// be closed by this one on the way out.
    /// </summary>
    void HandOver();
}
