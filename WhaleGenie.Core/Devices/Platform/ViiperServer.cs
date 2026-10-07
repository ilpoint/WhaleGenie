using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The VIIPER server that the virtual keyboard and mouse live in.
///
/// It belongs to another project, but unlike the usbip-win2 driver — which is installed once and
/// belongs to the machine — it is only a program. That is what makes "start the server first"
/// something WhaleGenie can do instead of asking the user for: a macro that needs driver-level
/// input works whether or not anybody launched the server by hand. A server that was already there
/// is left alone, here and at the end: whoever started it owns it.
/// </summary>
public static class ViiperServer
{
    /// <summary>
    /// The flag a client on this machine needs. Without it the devices go on the bus and never
    /// reach the desktop, so every launch carries it rather than depending on how the user ran it.
    /// </summary>
    public const string AutoAttach = "--api.auto-attach-local-client";

    /// <summary>
    /// How long a start waits for the server to answer. The program is a few megabytes and starts a
    /// USB bus, so this is generous; giving up only means the run reports what is missing.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(8);

    /// <summary>How often the start looks whether the server is up yet.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(150);

    private static readonly object Gate = new();

    /// <summary>The server this program started, and the only one it may stop again.</summary>
    private static Process? _started;

    /// <summary>
    /// Whether a server is answering. A check hands in an answer of its own, so what happens when
    /// one is or is not there can be read without a server on the machine.
    /// </summary>
    internal static Func<bool> Answering { get; set; } = DriverInput.IsServerAnswering;

    /// <summary>
    /// How the program is put on the machine and waited for. A check hands in something that records
    /// what would have been started instead of starting it.
    /// </summary>
    internal static Func<string, bool> Launch { get; set; } = StartProcess;

    /// <summary>
    /// Where viiper.exe is, as the settings window was told. WhaleGenie does not go looking for it:
    /// the user unzips that project's download wherever they like and points at the program once.
    /// Null means nobody has said, which is the case until the settings window is used.
    /// </summary>
    public static string? Executable { get; set; }

    /// <summary>
    /// Makes sure a server is answering, starting the one at <see cref="Executable"/> when nothing
    /// is. False means driver-level input has no server to talk to and nothing could be started.
    /// </summary>
    public static bool Ensure()
    {
        if (Answering())
        {
            return true;
        }

        lock (Gate)
        {
            // The look above and this one are both outside the lock, so two runs asking at the same
            // moment do not both start a server: the second one finds the first one's.
            if (Answering())
            {
                return true;
            }

            if (Executable is not { Length: > 0 } path || !File.Exists(path))
            {
                return false;
            }

            return Launch(path);
        }
    }

    /// <summary>
    /// Stops the server this program started. A server it did not start is left running, and so is
    /// a server that is answering after ours was closed: something else is using it.
    /// </summary>
    public static void Stop()
    {
        Process? started;
        lock (Gate)
        {
            started = _started;
            _started = null;
        }

        if (started is null)
        {
            return;
        }

        try
        {
            if (!started.HasExited)
            {
                started.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // A server that is already gone, or that will not go, is not something to report on the
            // way out of the program.
        }
        finally
        {
            started.Dispose();
        }
    }

    /// <summary>Puts the server on the machine and waits for it to answer. Called under the lock.</summary>
    private static bool StartProcess(string path)
    {
        Process process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = AutoAttach,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
        }
        catch
        {
            // A file that is not a program, or one Windows refuses to run, is the same answer as no
            // server: the run reports what is missing.
            return false;
        }

        var waiting = Stopwatch.StartNew();
        while (waiting.Elapsed < Patience)
        {
            if (Answering())
            {
                _started = process;
                return true;
            }

            if (process.HasExited)
            {
                break;
            }

            Thread.Sleep(Interval);
        }

        // A server that never answered is closed again: leaving a program nobody can talk to running
        // behind the user's back is worse than reporting the failure.
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // As above: closing it is best effort.
        }

        process.Dispose();
        return false;
    }
}
