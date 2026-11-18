using System;
using System.Threading;
using System.Threading.Tasks;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One browser, one page, one door. The program keeps a single browser, so a step that switches
/// tabs, reads a selector or closes the page is changing state every other run also sees. Reading
/// the page and clicking in it are part of the same turn: a switch in one macro between another
/// macro's click and its next read would make the second macro act on a page it did not choose.
/// </summary>
/// <remarks>
/// This door is separate from <see cref="InputGate"/> on purpose. Sending keys to the desktop and
/// using the shared browser are different pieces of state, and a macro that does one has no reason
/// to wait for a macro doing the other. The door is held for one browser step at a time, not for a
/// whole macro, so a wait for a page to load does not keep an unrelated browser step out for the
/// rest of the run.
/// </remarks>
public static class BrowserGate
{
    private static readonly SemaphoreSlim Door = new(1, 1);
    private static string _owner = string.Empty;

    /// <summary>Raised when the door changes hands, on whatever thread that happened on.</summary>
    public static event Action? Changed;

    /// <summary>The run using the browser right now, or empty when nothing is.</summary>
    public static string Owner => Volatile.Read(ref _owner);

    /// <summary>
    /// Waits for the browser and reports the run by name while it is inside. The caller gives the
    /// hold back with <see cref="IDisposable.Dispose"/>, whether the step succeeded or failed.
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
