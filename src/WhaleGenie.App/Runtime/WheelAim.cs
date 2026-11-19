using System;
using System.Threading;

namespace WhaleGenie.Runtime;

/// <summary>
/// The last pointer position the hook saw, and how many of those positions the wheel actually drew.
/// </summary>
/// <remarks>
/// The hook runs on the thread that delivers system input and has to answer at once. Queueing one
/// interface callback per movement let a fast pointer build a backlog the interface could never
/// drain, which reads as the wheel dragging behind the cursor before the program gives up. Writing
/// the position here costs two stores; the window takes one reading per frame, so the work is
/// bounded by the frame rate rather than by the mouse.
/// </remarks>
public sealed class WheelAim
{
    private long _x;
    private long _y;
    private long _written;
    private long _read;
    private long _taken;

    /// <summary>Records a position. Safe to call from the hook thread; it never blocks.</summary>
    public void Move(double x, double y)
    {
        Volatile.Write(ref _x, BitConverter.DoubleToInt64Bits(x));
        Volatile.Write(ref _y, BitConverter.DoubleToInt64Bits(y));
        Interlocked.Increment(ref _written);
    }

    /// <summary>
    /// Reads the newest position once. False when nothing was written since the last reading, which
    /// is what keeps a pointer that is resting still from redrawing the wheel every frame.
    /// </summary>
    public bool Take(out double x, out double y)
    {
        var written = Volatile.Read(ref _written);
        x = BitConverter.Int64BitsToDouble(Volatile.Read(ref _x));
        y = BitConverter.Int64BitsToDouble(Volatile.Read(ref _y));
        if (written == Volatile.Read(ref _read))
        {
            return false;
        }

        // A move that lands between the reading and this write belongs to the next frame: the
        // counter it raised is still ahead of what was noted here, so nothing is lost.
        Volatile.Write(ref _read, written);
        Interlocked.Increment(ref _taken);
        return true;
    }

    /// <summary>
    /// Forgets what has not been read yet, so a wheel that has closed does not answer a move that
    /// happened while it was open.
    /// </summary>
    public void Clear()
    {
        Volatile.Write(ref _read, Volatile.Read(ref _written));
        Volatile.Write(ref _x, BitConverter.DoubleToInt64Bits(0));
        Volatile.Write(ref _y, BitConverter.DoubleToInt64Bits(0));
    }

    /// <summary>Positions written, and readings the window actually made.</summary>
    public (long Written, long Taken) Counts => (Volatile.Read(ref _written), Volatile.Read(ref _taken));
}
