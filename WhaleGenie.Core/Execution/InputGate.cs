using System;
using System.Threading;
using System.Threading.Tasks;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// 一扇门，只有一步键鼠能同时走进去。A macro that sends input and another macro that sends input
/// are two hands on one keyboard: without this, whichever step happens to come first wins, and the
/// window that is being typed into sees the two runs written through each other. Reading the screen
/// is not input and does not come through here — two macros may look at the same window at once,
/// which is what makes waiting for a picture in one macro and typing in another work.
/// </summary>
/// <remarks>
/// The door is held for one step at a time rather than for a whole macro: what has to be kept apart
/// is the input, and a macro that waits five seconds between two key presses has no reason to hold
/// the keyboard while it waits. Runs that did not ask to share the door are not held back by it —
/// the lock is for the macros whose author said so, and a single macro on its own never notices it.
/// </remarks>
public static class InputGate
{
    private static readonly SemaphoreSlim Door = new(1, 1);
    private static string _owner = string.Empty;

    /// <summary>Raised when the door changes hands, on whatever thread that happened on.</summary>
    public static event Action? Changed;

    /// <summary>The run that is sending input right now, or empty when nothing is.</summary>
    public static string Owner => Volatile.Read(ref _owner);

    /// <summary>
    /// Waits for the door and reports the run by name while it is inside. The hold has to be given
    /// up — the caller gives it back with <see cref="IDisposable.Dispose"/>, which is what lets the
    /// next run in whether this one finished or failed.
    /// </summary>
    public static async Task<IDisposable> Take(string run, CancellationToken token)
    {
        await Door.WaitAsync(token);
        Volatile.Write(ref _owner, run);
        Changed?.Invoke();
        return new Hold();
    }

    private sealed class Hold : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref _owner, string.Empty);
            Door.Release();
            Changed?.Invoke();
        }
    }
}
