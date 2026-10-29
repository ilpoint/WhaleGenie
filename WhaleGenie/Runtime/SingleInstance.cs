using System;
using System.Runtime.InteropServices;
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
///
/// Asking is not the same as arriving, and arriving is the part that used to fail: the launch that
/// asks is the one holding the foreground, so it hands that right on before it ends, and it stays
/// alive until the window says it is in front. Without both, Windows refuses the raise and only the
/// taskbar button flashes.
/// </remarks>
internal static class SingleInstance
{
    private static Mutex? _held;

    private static EventWaitHandle? _asked;

    private static EventWaitHandle? _came;

    /// <summary>
    /// The name this program's one place is named by. Settable so a check can take a place of its
    /// own instead of the one the person's copy of the program is holding.
    /// </summary>
    internal static string Place { get; set; } = "Local\\WhaleGenie-" + Environment.UserName;

    private static string EventName => Place + "-show";

    private static string CameName => Place + "-came";

    /// <summary>How long a second launch waits for the window to say it is in front.</summary>
    private static readonly TimeSpan Answer = TimeSpan.FromSeconds(2);


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
            _came = new EventWaitHandle(false, EventResetMode.AutoReset, CameName);
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

    /// <summary>
    /// Asks the window that is already running to come back, and gives up if none answers. It stays
    /// for as long as the answer takes, because it is the process holding the foreground and it has
    /// just handed that right on: the window it asked can only use it while this one is still here.
    /// </summary>
    internal static void AskToComeBack()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(EventName, out var asked))
            {
                return;
            }

            using (asked)
            {
                var came = EventWaitHandle.TryOpenExisting(CameName, out var answer) ? answer : null;
                using (came)
                {
                    came?.Reset();
                    HandOverForeground();
                    asked.Set();
                    came?.WaitOne(Answer);
                }
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // Nobody to ask; the second launch ends quietly, which is still the right answer.
        }
    }

    /// <summary>
    /// Hands this launch's right to take the foreground on to whoever needs it. Only a program that
    /// may take the foreground can grant that, and this one may: it is the copy the person just
    /// started, so it is the one in front.
    /// </summary>
    private static void HandOverForeground()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            AllowSetForegroundWindow(AnyProcess);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            // Nothing to hand over. The window has its own ways of coming forward, they are just
            // the slower ones.
        }
    }

    /// <summary>
    /// Answers those asks for as long as the window lives. The handler runs off the interface
    /// thread, so it is handed back to the caller to marshal; the answer that it is done is given
    /// only once the caller says the window is in front.
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

                var gone = false;
                try
                {
                    comeBack();
                }
                finally
                {
                    try
                    {
                        _came?.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                        gone = true;
                    }
                }

                if (gone)
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "whalegenie-single-instance",
        };

        thread.Start();
    }

    /// <summary>ASFW_ANY: any process may take the foreground.</summary>
    private static readonly uint AnyProcess = unchecked((uint)-1);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint process);
}
