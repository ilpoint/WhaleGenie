using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Zaya.Primitives;
using Zaya.Screenshot.Impl.Windows;
using Zaya.Screenshot.Models;
using Zaya.Screenshot.Services;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Pictures taken through Windows Graphics Capture: the machinery that puts windows on screen is
/// asked for a window's own picture instead of the pixels where the window happens to be, so a game
/// another window is covering is read as if it were in front.
/// </summary>
/// <remarks>
/// Everything happens on one thread of this object's own. The capture library is asynchronous and
/// hands its pictures out of a pool belonging to a graphics device, so its calls are kept in single
/// file; and a macro asks from whichever thread it is on, sometimes the interface's own, where
/// waiting on the library would wait on the thread that has to deliver the frame.
/// </remarks>
internal sealed class GraphicsCapture : IDisposable
{
    /// <summary>Which display the desktop is read from, counting from the primary one.</summary>
    private const int PrimaryDisplay = 0;

    private readonly OneThread _thread = new();

    /// <summary>One open session per window, kept because opening one is the expensive part.</summary>
    private readonly Dictionary<long, ICaptureSession> _windows = [];

    /// <summary>
    /// Windows that could not be opened for reading this way at all. Opening one fails every time,
    /// so a macro looking at the same window in a loop would pay for a failed attempt on every
    /// look; what is remembered is the window, not the attempt, because it is the window that
    /// cannot be read this way.
    /// </summary>
    private readonly HashSet<long> _refused = [];

    /// <summary>
    /// One open session for the whole of the display, kept for the same reason a window's is: an
    /// open session is worth far more than the frame that comes out of it, and a session opened
    /// and let go of for every look also makes the capture border blink on and off while a macro
    /// reads the same thing over and over.
    /// </summary>
    private ICaptureSession? _display;

    private CaptureService? _service;

    private bool _unavailable;

    /// <summary>The whole of one window's content, at the size the window draws it.</summary>
    public ImageFrame Window(long handle, int x, int y, int width, int height)
        => _thread.Run(() =>
        {
            if (_refused.Contains(handle))
            {
                throw new DeviceActionException("Run.NoGraphicsCapture", Unreadable(handle));
            }

            var region = new RectWindowRegion
            {
                WindowHandle = new IntPtr(handle),
                Rectangle = new System.Drawing.Rectangle(x, y, width, height),
                UseClientCoordinates = true,
                PixelFormat = PixelFormat.Bgra32,
            };

            try
            {
                return Read(Session(handle, region));
            }
            catch (DeviceActionException)
            {
                // A session that was opened before the window was minimised and shown again cannot
                // deliver any more, and a macro watching a game that the user alt-tabbed away from
                // should not fail a step over it. The failed reading let go of that session, so
                // this opens one now; a second failure is the window's own and is reported.
                return Read(Session(handle, region));
            }
        });

    /// <summary>
    /// The whole of one display, in that display's own coordinates, read with the one session that
    /// is kept for it and opened again if that session has stopped delivering — a display whose
    /// resolution or mode changed hands out frames of another size, or none at all.
    /// </summary>
    public ImageFrame Desktop(int x, int y, int width, int height)
        => _thread.Run(() =>
        {
            ImageFrame whole;
            try
            {
                whole = Read(Display());
            }
            catch (DeviceActionException)
            {
                // A display whose resolution or mode changed stops the session it was opened with,
                // and the frame that would have been asked of it is asked again of a new one.
                whole = Read(Display());
            }

            return Clip(whole, x, y, width, height);
        });

    /// <summary>
    /// Whether this machine can read the screen this way at all. Asked once and remembered: a
    /// machine with no graphics device is not going to grow one while a macro is running.
    /// </summary>
    public bool Available => _thread.Run(() =>
    {
        if (_unavailable)
        {
            return false;
        }

        try
        {
            return Service().IsAvailable;
        }
        catch (Exception)
        {
            _unavailable = true;
            return false;
        }
    });

    /// <summary>
    /// One session per window, opened the first time that window is read and kept while it lives.
    /// Opening one builds a graphics device and a pool of pictures, which is far more than the
    /// reading of one frame; a session that has stopped working is dropped rather than kept.
    /// </summary>
    private ICaptureSession Session(long handle, RectWindowRegion region)
    {
        if (_windows.TryGetValue(handle, out var kept))
        {
            return kept;
        }

        try
        {
            var session = Service().CreateSessionAsync(region, CancellationToken.None)
                .GetAwaiter().GetResult();
            _windows[handle] = session;
            return session;
        }
        catch (Exception error) when (error is not DeviceActionException
            and not DeviceUnavailableException)
        {
            // Opening one cannot fail and then work: the library builds the whole of the reading
            // apparatus here, and a window it will not build it for is one it never builds it for.
            // Remembering that is what stops a loop from making it try again on every look, which
            // is both a wasted attempt and a session's worth of capture border coming and going.
            _refused.Add(handle);
            throw Refused(error);
        }
    }

    /// <summary>
    /// One picture out of a session, in the layout the rest of the engine uses. A session that
    /// cannot deliver is let go of, because what it was opened on has been closed, resized or
    /// drawn again from scratch, and the next reading has to open a new one: a kept session whose
    /// reading failed once is tried again once on the same session, since a frame that did not
    /// arrive in time is not the same thing as a session that is finished.
    /// </summary>
    private ImageFrame Read(ICaptureSession session)
    {
        try
        {
            return One(session) ?? throw new DeviceActionException("Run.CaptureFailed",
                "no picture came back");
        }
        catch (Exception error) when (error is not DeviceUnavailableException
            and not DeviceActionException)
        {
            try
            {
                return One(session) ?? throw new DeviceActionException("Run.CaptureFailed",
                    "no picture came back");
            }
            catch (Exception again) when (again is not DeviceUnavailableException
                and not DeviceActionException)
            {
                Drop(session);
                throw Refused(again);
            }
        }
    }

    /// <summary>One picture out of a session, or nothing when the library hands back nothing.</summary>
    private static ImageFrame? One(ICaptureSession session)
    {
        var frame = session.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
        return frame is null ? null : Picture(frame);
    }

    /// <summary>
    /// The one session the display is read through, opened the first time it is asked for and kept
    /// from then on. It is opened on the whole of the display rather than on the rectangle that was
    /// asked for: a session belongs to the thing being read, and reading one part of the screen and
    /// then another would otherwise open and let go of a session for each of them. The part that is
    /// wanted is cut out of the frame afterwards, where it costs a copy rather than a session.
    /// </summary>
    private ICaptureSession Display()
    {
        if (_display is { } kept)
        {
            return kept;
        }

        var region = new FullScreenDesktopRegion
        {
            DisplayIndex = PrimaryDisplay,
            PixelFormat = PixelFormat.Bgra32,
        };

        try
        {
            return _display = Service().CreateSessionAsync(region, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is not DeviceActionException
            and not DeviceUnavailableException)
        {
            throw Refused(error);
        }
    }

    /// <summary>
    /// The part of the display that was asked for, cut out of the whole of it. The frame this comes
    /// from is the whole display, so a rectangle that runs off it is reported rather than quietly
    /// cut down: whoever asked for it is asking for a part of the screen that is not there.
    /// </summary>
    internal static ImageFrame Clip(ImageFrame whole, int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0
            || x + width > whole.Width || y + height > whole.Height)
        {
            throw new DeviceActionException("Run.EmptyRegion", $"{x},{y} {width}x{height}");
        }

        if (x == 0 && y == 0 && width == whole.Width && height == whole.Height)
        {
            return whole;
        }

        var pixels = new byte[(long)width * height * 4];
        for (var row = 0; row < height; row++)
        {
            Array.Copy(whole.Bgra, (((long)(y + row) * whole.Width) + x) * 4, pixels,
                (long)row * width * 4, width * 4);
        }

        return new ImageFrame(width, height, pixels);
    }

    /// <summary>Lets go of a session that can no longer deliver, and of the note that keeps it.</summary>
    private void Drop(ICaptureSession session)
    {
        if (ReferenceEquals(_display, session))
        {
            _display = null;
        }

        foreach (var (handle, kept) in _windows)
        {
            if (ReferenceEquals(kept, session))
            {
                _windows.Remove(handle);
                break;
            }
        }

        session.Dispose();
    }

    /// <summary>
    /// What to say about a window this way cannot be read. Its handle is all that is known about it
    /// by the time the attempt has failed, and a person reading the failure has the window in front
    /// of them.
    /// </summary>
    private static string Unreadable(long handle) => $"{handle:X} (recorded earlier)";

    private CaptureService Service()
    {
        var service = _service ??= new CaptureService();
        if (!service.IsAvailable)
        {
            _unavailable = true;
            throw new DeviceActionException("Run.NoGraphicsCapture",
                "this machine has none (Windows 10 2004 or later and a Direct3D 11 device)");
        }

        return service;
    }

    /// <summary>
    /// Copies a picture the library handed over into the layout the rest of the engine uses: four
    /// bytes to a pixel, blue first, and one row after another with no padding between them.
    /// </summary>
    private static ImageFrame Picture(IRawImage frame)
    {
        var stride = frame.Width * 4;
        if (!frame.Format.Equals(PixelFormat.Bgra32) || frame.Stride < stride
            || frame.Width <= 0 || frame.Height <= 0)
        {
            throw new DeviceActionException("Run.CaptureFailed",
                $"{frame.Width}x{frame.Height} {frame.Format.Name}");
        }

        var bytes = new byte[(long)frame.Width * frame.Height * 4];
        var source = frame.GetPixelData();
        for (var row = 0; row < frame.Height; row++)
        {
            source.Slice(row * frame.Stride, stride)
                .CopyTo(bytes.AsSpan(row * stride, stride));
        }

        return new ImageFrame(frame.Width, frame.Height, bytes);
    }

    /// <summary>
    /// What to say when the library refuses. The library's own words are a sentence for a person
    /// and travel as the detail beside the key, the way every other failure out of a device does.
    /// </summary>
    private static DeviceActionException Refused(Exception error)
    {
        var detail = error is LocalizedException localized
            ? localized.GetLocalizedMessage(CultureInfo.CurrentUICulture)
            : error.Message;

        return new DeviceActionException("Run.NoGraphicsCapture", detail);
    }

    public void Dispose()
    {
        _thread.Run(() =>
        {
            foreach (var session in _windows.Values)
            {
                session.Dispose();
            }

            _windows.Clear();
            _display?.Dispose();
            _display = null;
            _service?.Dispose();
            _service = null;
            return true;
        });

        _thread.Dispose();
    }

    /// <summary>
    /// One thread with a queue, for work that must not run side by side and must not run on the
    /// thread that asked for it. <c>Run</c> waits, the way a device the engine calls into does.
    /// </summary>
    private sealed class OneThread : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _worker;

        public OneThread()
        {
            _worker = new Thread(Pump) { IsBackground = true, Name = "graphics capture" };
            _worker.Start();
        }

        public T Run<T>(Func<T> work)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try
                {
                    done.SetResult(work());
                }
                catch (Exception error)
                {
                    done.SetException(error);
                }
            });

            return done.Task.GetAwaiter().GetResult();
        }

        private void Pump()
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                work();
            }
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _worker.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }
    }
}
