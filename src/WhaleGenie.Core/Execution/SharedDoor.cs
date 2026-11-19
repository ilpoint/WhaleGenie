using System;
using System.Threading;
using System.Threading.Tasks;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One door, one holder at a time: the keyboard and mouse as one, the shared browser as another.
/// Two runs sending input are two hands on one keyboard, and a browser step in one macro switching
/// the page between another macro's click and its next read leaves the second acting on a page it
/// did not choose. The door is held for one step rather than for a whole macro, so waiting does not
/// keep an unrelated run out for the rest of it.
/// </summary>
public sealed class SharedDoor
{
    private readonly SemaphoreSlim _door = new(1, 1);
    private string _owner = string.Empty;

    /// <summary>Raised when the door changes hands, on whatever thread that happened on.</summary>
    public event Action? Changed;

    /// <summary>The run holding the door right now, or empty when nothing is.</summary>
    public string Owner => Volatile.Read(ref _owner);

    /// <summary>
    /// Waits for the door and reports the run by name while it is inside. The hold is given back
    /// with <see cref="IDisposable.Dispose"/>, so the next run gets in whether this one finished or
    /// failed.
    /// </summary>
    public async Task<IDisposable> Take(string run, CancellationToken token)
    {
        await _door.WaitAsync(token);
        Volatile.Write(ref _owner, run);
        Changed?.Invoke();
        return new Hold(this);
    }

    private sealed class Hold(SharedDoor door) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref door._owner, string.Empty);
            door._door.Release();
            door.Changed?.Invoke();
        }
    }
}

/// <summary>
/// The two doors this program has. They stay apart: sending keys to the desktop and using the
/// shared browser are different pieces of state, and a macro doing one has no reason to wait for a
/// macro doing the other.
/// </summary>
public static class SharedDoors
{
    /// <summary>Keyboard and mouse input, one step at a time.</summary>
    public static SharedDoor Input { get; } = new();

    /// <summary>The one browser's page, one step at a time.</summary>
    public static SharedDoor Browser { get; } = new();
}
