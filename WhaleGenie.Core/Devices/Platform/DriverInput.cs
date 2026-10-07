using System;
using System.Net;
using System.Net.Sockets;
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
    public static bool IsServerAnswering()
    {
        // The connect is kept off this thread so the window that asked never waits on a server that
        // is not there, and the timeout is what turns a refusal into a quick "no" instead of
        // Windows' own minute-long connect wait.
        var probe = Task.Run(() =>
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(IPAddress.Parse(ServerHost), ServerPort);
                return true;
            }
            catch
            {
                return false;
            }
        });

        return probe.Wait(ProbeTimeout) && probe.Result;
    }
}
