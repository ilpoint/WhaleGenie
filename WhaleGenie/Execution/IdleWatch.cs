using System;
using System.Runtime.InteropServices;

namespace WhaleGenie.Execution;

/// <summary>How long the machine has gone without anybody touching the keyboard or the mouse.</summary>
/// <remarks>
/// Windows keeps the time of the last input for the whole desktop, so what counts is any input
/// anywhere, not only in WhaleGenie — which is what "the machine has been left alone" means.
/// </remarks>
internal static class IdleWatch
{
    /// <summary>How long the machine has been left alone, or zero when the answer is not known.</summary>
    public static TimeSpan Since()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            // An unknown answer reads as "busy", which is the safer way to be wrong: it cannot set
            // a macro off.
            return TimeSpan.Zero;
        }

        // Both tick counts are 32-bit and come round every seven weeks, so the subtraction is done
        // in the same width; the answer stays right across the wrap.
        var idle = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(idle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;

        /// <summary>The tick count when the last input arrived.</summary>
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
