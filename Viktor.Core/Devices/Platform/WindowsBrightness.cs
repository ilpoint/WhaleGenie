using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// The brightness of the screens, through the monitor control API that talks to a screen over its
/// video cable (DDC/CI). This is the way that works for a desktop's own monitor; a laptop's built-in
/// panel often answers only Windows' own brightness control instead, and says nothing here.
/// </summary>
internal static class WindowsBrightness
{
    /// <summary>
    /// The brightness of the first screen that will say, from 0 to 100. A machine whose screens
    /// cannot be asked — a laptop panel, a monitor on a cable that does not carry the setting — has
    /// no answer, which is worth saying rather than reporting zero.
    /// </summary>
    public static int Current()
    {
        var answer = -1;
        Each(screen =>
        {
            if (answer < 0 && GetMonitorBrightness(screen, out var min, out var now, out var max))
            {
                answer = Percent(now, min, max);
            }
        });

        return answer >= 0 ? answer : throw new DeviceUnavailableException("an adjustable screen");
    }

    /// <summary>
    /// Turns every screen that will take it to a brightness from 0 to 100. Screens that cannot be
    /// asked are left alone: a machine with two monitors, one of which answers, is a machine whose
    /// brightness can be set.
    /// </summary>
    public static void Set(int percent)
    {
        var worked = false;
        Each(screen =>
        {
            if (!GetMonitorBrightness(screen, out var min, out _, out var max))
            {
                return;
            }

            worked |= SetMonitorBrightness(screen, (uint)Wanted(percent, min, max));
        });

        if (!worked)
        {
            throw new DeviceActionException("Run.BrightnessRefused");
        }
    }

    /// <summary>
    /// Runs something for every screen of every monitor, and lets them go afterwards. A screen handle
    /// is only good until it is destroyed, so nothing from inside may be kept.
    /// </summary>
    private static void Each(Action<IntPtr> work)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("an adjustable screen");
        }

        try
        {
            foreach (var monitor in Monitors())
            {
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
                {
                    continue;
                }

                var screens = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, screens))
                {
                    continue;
                }

                try
                {
                    foreach (var screen in screens)
                    {
                        work(screen.Handle);
                    }
                }
                finally
                {
                    _ = DestroyPhysicalMonitors(count, screens);
                }
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new DeviceUnavailableException("an adjustable screen");
        }
    }

    private static List<IntPtr> Monitors()
    {
        var found = new List<IntPtr>();
        _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr monitor, IntPtr _, ref Rect rect, IntPtr _) =>
            {
                found.Add(monitor);
                return true;
            },
            IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// A screen's own numbers turned into the nought-to-a-hundred a person thinks in. Screens do not
    /// all start at zero or stop at a hundred, which is why the range is read rather than assumed.
    /// </summary>
    private static int Percent(uint value, uint min, uint max)
        => max <= min ? 0 : (int)Math.Round((value - min) * 100.0 / (max - min));

    private static int Wanted(int percent, uint min, uint max)
        => max <= min ? (int)min : (int)(min + (Math.Clamp(percent, 0, 100) * (max - min) / 100.0));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool MonitorCallback(IntPtr monitor, IntPtr context, ref Rect rect,
        IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr context, IntPtr clip,
        MonitorCallback callback, IntPtr data);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor,
        out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count,
        [Out] PhysicalMonitor[] screens);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] screens);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr screen, out uint min, out uint now,
        out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr screen, uint wanted);
}
