using System.Threading.Tasks;

namespace WhaleGenie.Core.Devices;

/// <summary>How far this machine is from taking input that looks like real hardware.</summary>
public enum DriverInputState
{
    /// <summary>The usbip-win2 driver is installed and the library the program carries is there.</summary>
    Ready,

    /// <summary>No usbip-win2 kernel driver, so there is no virtual USB device to send into.</summary>
    DriverMissing,

    /// <summary>
    /// The driver is there, but the library that brings the virtual USB server with it is not: the
    /// program was unpacked or copied without it, and nothing here can build one out of nothing.
    /// </summary>
    LibraryMissing,
}

/// <summary>
/// Driver-level input as a whole: what this machine still needs for it, and the two things the rest
/// of the program asks of it — a head start at startup, and being closed on the way out.
/// </summary>
/// <remarks>
/// It is an interface of its own because the one piece that is not ours — the kernel driver — has to
/// be named to the user, and the settings window should be able to say what is missing without
/// anything that reads the registry or opens a library: the names belong to whoever installs them,
/// so the window keeps the words and the engine keeps the looking.
/// </remarks>
public interface IDriverInput
{
    /// <summary>How this machine stands right now.</summary>
    DriverInputState State { get; }

    /// <summary>
    /// Whether the virtual keyboard and mouse are to be put on the machine when the program starts,
    /// which is what the settings window's switch asks for. It is a fact about this machine rather
    /// than about the interface — the choice is stored beside the program and handed over here, the
    /// same way the window hands over the way it reads the screen.
    /// </summary>
    bool AttachAtStart { get; set; }

    /// <summary>
    /// Gives driver-level input its head start when the switch above is on: the virtual keyboard and
    /// mouse go on the machine now rather than at the first macro step that needs them, so that step
    /// is not the one that waits. Best effort — a machine that cannot do it is told about it at that
    /// step instead, which is where the user is looking.
    /// </summary>
    Task WarmUp();

    /// <summary>
    /// Takes the virtual devices off the machine and closes the server this program runs in itself,
    /// which is the way out of the program.
    /// </summary>
    void Stop();

    /// <summary>
    /// Lets go of both without ending the program, which is what restarting with higher rights
    /// needs: the copy that takes over starts its own, and one left running behind it would be
    /// closed by this one on the way out.
    /// </summary>
    void HandOver();
}
