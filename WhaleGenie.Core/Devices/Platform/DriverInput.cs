using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WhaleGenie.Core.Devices.Platform;

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
/// Driver-level input rests on two pieces WhaleGenie neither ships nor installs by itself: the
/// usbip-win2 kernel driver and a running VIIPER server. Both belong to their own projects and both
/// need the user's say-so before they go on the machine, so all this does is look — the settings
/// window turns that look into "install this", and <see cref="ViiperServer"/> starts the server.
/// </summary>
public static class DriverInput
{
    /// <summary>
    /// The service the usbip-win2 driver registers, which is what "installed" means. The version is
    /// the installer's business and is deliberately not written down here, where it would only be
    /// wrong the day after that project released again.
    /// </summary>
    private const string DriverService = @"SYSTEM\CurrentControlSet\Services\usbip2_ude";

    /// <summary>Where the VIIPER server answers: this machine, and only this machine.</summary>
    public const string ServerHost = "127.0.0.1";

    /// <summary>The port the VIIPER server answers its clients on.</summary>
    public const int ServerPort = 3242;

    /// <summary>How long a probe waits for an answer before it counts as nothing being there.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(400);

    private static readonly object AskingGate = new();

    /// <summary>The question that is out, or null when none is. Read and written under the gate.</summary>
    private static Task<bool>? _asking;

    /// <summary>
    /// How the machine is asked whether the server is there. A check hands in its own answer, so what
    /// a burst of questions does — how often the machine is really asked, and what each caller reads
    /// in between — can be read without a server on the machine.
    /// </summary>
    internal static Func<bool> Probe
    {
        get => _probe;
        set
        {
            lock (AskingGate)
            {
                _probe = value;

                // The question that is out was asked of the machine that was there a moment ago, so
                // it is not the answer to the one this hands in.
                _asking = null;
            }
        }
    }

    private static Func<bool> _probe = Connect;

    /// <summary>Looks at this machine and says which piece driver-level input is still missing.</summary>
    public static DriverInputState Check()
    {
        if (!DriverInstalled())
        {
            return DriverInputState.DriverMissing;
        }

        return IsServerAnswering() ? DriverInputState.Ready : DriverInputState.ServerMissing;
    }

    private static bool DriverInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var service = Registry.LocalMachine.OpenSubKey(DriverService);
            return service is not null;
        }
        catch
        {
            // A key that cannot be read is a driver that cannot be counted on.
            return false;
        }
    }

    /// <summary>
    /// Whether something is answering where the VIIPER server should be. Whoever asks is usually
    /// about to start one of its own, so this is what keeps a second server off the machine.
    /// </summary>
    public static bool IsServerAnswering() => IsServerAnswering(ProbeTimeout);

    /// <summary>
    /// The same question, with whoever asked naming how long they are willing to wait for it. A
    /// busy machine can take longer to get to the question than the question itself takes, and a
    /// caller reading what came back has to be given that room rather than read "nothing there"
    /// out of a held-up thread pool.
    /// </summary>
    internal static bool IsServerAnswering(TimeSpan patience)
    {
        var asking = Ask();
        return asking.Wait(patience) && asking.Result;
    }

    /// <summary>
    /// The question that is out, or a new one when none is. One at a time: a window that opens reads
    /// two lines, a run looks before it starts and again while it waits for a server, and every one
    /// of those asks the same thing — the caller that arrives while a question is out waits on that
    /// one instead of making a connection of its own. A question that has already been answered is
    /// not reused, so what acts on the answer — starting a server — still sees one that came up a
    /// moment ago.
    /// </summary>
    private static Task<bool> Ask()
    {
        lock (AskingGate)
        {
            if (_asking is null || _asking.IsCompleted)
            {
                _asking = Task.Run(_probe);
            }

            return _asking;
        }
    }

    /// <summary>
    /// One connection to where the server answers, called off when it outlasts
    /// <see cref="ProbeTimeout"/>. A machine with nothing listening spends about two seconds before
    /// it refuses, and a connect that cannot be called off spends them on the thread it was started
    /// on: a handful of those emptied the thread pool, and work with nothing to do with driver-level
    /// input waited behind them.
    /// </summary>
    private static bool Connect()
    {
        try
        {
            using var client = new TcpClient();
            using var giveUp = new CancellationTokenSource(ProbeTimeout);
            client.ConnectAsync(IPAddress.Parse(ServerHost), ServerPort, giveUp.Token)
                .GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            // A refusal, a call-off and a machine with no server answer the same question.
            return false;
        }
    }
}
