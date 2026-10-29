using System;
using System.Threading;

namespace WhaleGenie.Runtime;

/// <summary>
/// One window at a time. Two open copies of WhaleGenie are two writers of the same settings, the
/// same package and the same virtual keyboard, and the second one is always a mistake: the person
/// double-clicked the icon, or opened a project from Explorer while the window was already there.
/// </summary>
/// <remarks>
/// A second launch does not fail silently — it asks the window that is already running to come back
/// and then ends, which is what a person double-clicking an icon expects to see. The ask is a named
/// event rather than a message to a window handle, because the window may be out of the way in the
/// notification area and has no handle to be sent to.
/// </remarks>
internal static class SingleInstance
{
    private static Mutex? _held;

    private static EventWaitHandle? _asked;

    /// <summary>
    /// The name this program's one place is named by. Settable so a check can take a place of its
    /// own instead of the one the person's copy of the program is holding.
    /// </summary>
    internal static string Place { get; set; } = "Local\\WhaleGenie-" + Environment.UserName;

    private static string EventName => Place + "-show";

    /// <summary>
    /// Takes the one place, or says that somebody else has it. The mutex is held until the process
    /// ends; a process that dies without letting go leaves nothing behind, because the system
    /// destroys the object when the last handle to it goes.
    /// </summary>
    internal static bool TryAcquire()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, Place, out var created);
            if (!created)
            {
                mutex.Dispose();
                return false;
            }

            _held = mutex;
            _asked = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Another user's copy owns the name. Starting anyway is better than not starting: the
            // two cannot share settings or a package path in that case either way.
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return true;
        }
    }

    /// <summary>Asks the window that is already running to come back, and gives up if none answers.</summary>
    internal static void AskToComeBack()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var asked))
            {
                using (asked)
                {
                    asked.Set();
                }
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // Nobody to ask; the second launch ends quietly, which is still the right answer.
        }
    }

    /// <summary>
    /// Answers those asks for as long as the window lives. The handler runs off the interface
    /// thread, so it is handed back to the caller to marshal.
    /// </summary>
    internal static void Listen(Action comeBack)
    {
        if (_asked is null)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _asked.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                comeBack();
            }
        })
        {
            IsBackground = true,
            Name = "whalegenie-single-instance",
        };

        thread.Start();
    }
}
