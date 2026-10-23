using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Window events, through <c>SetWinEventHook</c>. Windows delivers a hook that is installed
/// out-of-context through the message queue of the thread that installed it, so this class keeps a
/// thread of its own, with a message queue of its own, for the whole life of the hook.
/// </summary>
/// <remarks>
/// Only the changes a "wait for this window" macro can notice are asked for: a window being made,
/// shown, hidden, destroyed, or renamed. Events about parts of a window — a menu, the focus, a
/// scroll — are dropped, because they say nothing about which windows are there. Renaming counts
/// because a macro waiting for a page to finish loading waits for a title, and the window was
/// already up when the title arrived.
/// </remarks>
public sealed class WindowsWindowEvents : IWindowEvents
{
    /// <summary>Events about the window itself, not about a part of it.</summary>
    private const int ObjectIdWindow = 0;

    private const int EventObjectCreate = 0x8000;
    private const int EventObjectDestroy = 0x8001;
    private const int EventObjectShow = 0x8002;
    private const int EventObjectHide = 0x8003;
    private const int EventObjectNameChange = 0x800C;
    private const int EventObjectLocationChange = 0x800B;
    private const int EventSystemForeground = 0x0003;

    private const uint OutOfContext = 0x0000;
    private const uint WmQuit = 0x0012;

    /// <summary>
    /// A hook is taken away again if the delegate it was given is collected, so the delegate is
    /// held for as long as the hook is.
    /// </summary>
    private readonly WinEventProcedure _callback;

    private readonly ManualResetEventSlim _asked = new(false);
    private Thread? _thread;
    private IntPtr _created;
    private IntPtr _renamed;
    private IntPtr _foreground;
    private IntPtr _bounds;
    private uint _nativeId;
    private bool _started;
    private bool _disposed;

    public WindowsWindowEvents() => _callback = OnWindowEvent;

    public bool Available => _created != IntPtr.Zero || _renamed != IntPtr.Zero;

    public event Action? Changed;

    public event Action? ForegroundChanged;

    public event Action<long>? BoundsChanged;

    public void Start()
    {
        if (_started || _disposed || !OperatingSystem.IsWindows())
        {
            return;
        }

        _started = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "window events" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // Installing the hooks takes a moment, and what the caller does next depends on whether it
        // worked, so the answer is waited for rather than guessed at.
        _asked.Wait(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_created != IntPtr.Zero)
        {
            UnhookWinEvent(_created);
            _created = IntPtr.Zero;
        }

        if (_renamed != IntPtr.Zero)
        {
            UnhookWinEvent(_renamed);
            _renamed = IntPtr.Zero;
        }

        if (_foreground != IntPtr.Zero)
        {
            UnhookWinEvent(_foreground);
            _foreground = IntPtr.Zero;
        }

        if (_bounds != IntPtr.Zero)
        {
            UnhookWinEvent(_bounds);
            _bounds = IntPtr.Zero;
        }

        if (_thread is { IsAlive: true } thread)
        {
            PostThreadMessage(_nativeId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            thread.Join(TimeSpan.FromSeconds(2));
        }

        _asked.Dispose();
    }

    /// <summary>
    /// The window events, on a thread with a queue to receive them on. The queue has to exist
    /// before the thread can be woken by an id, and the hook has to be installed on the thread that
    /// is going to read it.
    /// </summary>
    private void Loop()
    {
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _nativeId = GetCurrentThreadId();

        try
        {
            _created = SetWinEventHook(EventObjectCreate, EventObjectHide, IntPtr.Zero, _callback,
                0, 0, OutOfContext);
            _renamed = SetWinEventHook(EventObjectNameChange, EventObjectNameChange, IntPtr.Zero,
                _callback, 0, 0, OutOfContext);
            _foreground = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero,
                _callback, 0, 0, OutOfContext);
            _bounds = SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange,
                IntPtr.Zero, _callback, 0, 0, OutOfContext);
        }
        catch (Exception)
        {
            // A hook that could not be installed is a machine that has nothing to say, and the
            // caller keeps looking on its own.
        }

        _asked.Set();

        // Nothing is done with the messages themselves: receiving them is what delivers the events.
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
        }
    }

    private void OnWindowEvent(IntPtr hook, uint kind, IntPtr window, int idObject, int idChild,
        uint thread, uint time)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (kind == EventSystemForeground)
            {
                ForegroundChanged?.Invoke();
                return;
            }

            if (kind == EventObjectLocationChange)
            {
                if (window != IntPtr.Zero && idObject == ObjectIdWindow)
                {
                    BoundsChanged?.Invoke(window.ToInt64());
                }

                return;
            }

            if (window == IntPtr.Zero || idObject != ObjectIdWindow || idChild != 0)
            {
                return;
            }

            Changed?.Invoke();
        }
        catch (Exception)
        {
            // Whatever answers this must not be able to stop the thread that hands events over, or
            // the hook is dead for the rest of the run.
        }
    }

    private delegate void WinEventProcedure(IntPtr hook, uint kind, IntPtr window, int idObject,
        int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Kind;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventProcedure callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out Message message, IntPtr window, uint first, uint last);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last,
        uint remove);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
