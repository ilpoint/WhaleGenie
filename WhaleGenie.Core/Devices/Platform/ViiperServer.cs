namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The USB server the virtual keyboard, mouse and controller live in. It is inside this program
/// rather than beside it: libVIIPER carries the whole thing, starts it on a thread of its own, and
/// there is no other program to find, start, keep alive or close afterwards.
/// </summary>
/// <remarks>
/// One server for the whole program, brought up the first time anything needs driver-level input:
/// the run that asks for it last still has a server under it, and a machine that never asks for
/// driver-level input never has one. It is closed on the way out — and closed before a copy of the
/// program taking over with higher rights starts its own.
/// </remarks>
internal static class ViiperServer
{
    /// <summary>
    /// Where the server listens: this machine, on a port the system hands out.
    ///
    /// Loopback only, because the only thing that ever attaches to it is the client driver on this
    /// machine: a server on every interface would have Windows ask the user to let it through the
    /// firewall, for a connection that never comes from anywhere else. The port is not named
    /// because a user who still runs the old VIIPER server program sits on 3241, and a fixed port
    /// would mean the server here cannot come up at all in that case. Which port it ended up on is
    /// the library's own business: it is the library that attaches the devices, and it knows.
    /// </summary>
    internal const string Address = "127.0.0.1:0";

    private static readonly object Gate = new();

    /// <summary>The server this program started, and the only one it may stop again.</summary>
    private static ViiperUsbServer? _server;

    /// <summary>
    /// What the library is asked to do. A check hands in something that writes down what it was
    /// asked and answers as it likes, so what a run does with the server — when it comes up, what
    /// goes on its bus, when the devices come off again — can be read without a driver, a virtual
    /// device or a library on the machine.
    /// </summary>
    internal static IViiperNative Native { get; set; } = new NativeViiper();

    /// <summary>
    /// Whether the program is on its way out. A check sets it, because what it decides — whether a
    /// server is started at all any more — is worth reading without a program actually ending; the
    /// way out is the only thing that sets it for real.
    /// </summary>
    internal static bool Leaving { get; set; }

    /// <summary>
    /// The server, brought up if it is not there yet; null when it cannot be had, either because
    /// nothing could bring it up or because the program is on its way out and a server started now
    /// would be one nothing is left to close.
    /// </summary>
    internal static ViiperUsbServer? Ensure()
    {
        lock (Gate)
        {
            if (_server is not null)
            {
                return _server;
            }

            // A server started now would be one nothing is left to close.
            if (Leaving)
            {
                return null;
            }

            return _server = ViiperUsbServer.Open(Native);
        }
    }

    /// <summary>
    /// Stops this program from starting a server and closes the one it started: it is on its way
    /// out, and a server started in the last moment would be one nothing would be left to close.
    /// </summary>
    internal static void Stop()
    {
        // Before the lock, so a start that has not begun yet does not begin at all.
        Leaving = true;
        Close();
    }

    /// <summary>
    /// Closes the server this program started for a hand-over: the copy taking over starts its own,
    /// and one left over from this copy would be closed on the way out, right under the new one.
    /// This is not the end of the program, though — the hand-over can be called off, and then this
    /// copy is still the one running — so starting a server again stays allowed.
    /// </summary>
    internal static void HandOver() => Close();

    private static void Close()
    {
        ViiperUsbServer? server;
        lock (Gate)
        {
            server = _server;
            _server = null;
        }

        server?.Close();
    }
}
