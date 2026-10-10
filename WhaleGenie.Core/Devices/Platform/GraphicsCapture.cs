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

    private CaptureService? _service;

    private bool _unavailable;

    /// <summary>The whole of one window's content, at the size the window draws it.</summary>
    public ImageFrame Window(long handle, int x, int y, int width, int height)
        => _thread.Run(() =>
        {
            var region = new RectWindowRegion
            {
                WindowHandle = new IntPtr(handle),
                Rectangle = new System.Drawing.Rectangle(x, y, width, height),
                UseClientCoordinates = true,
                PixelFormat = PixelFormat.Bgra32,
            };

            return Read(Session(handle, region), keeping: true);
        });

    /// <summary>The whole of one display, in that display's own coordinates.</summary>
    public ImageFrame Desktop(int x, int y, int width, int height)
        => _thread.Run(() =>
        {
            var region = new RectDesktopRegion
            {
                DisplayIndex = PrimaryDisplay,
                Rectangle = new System.Drawing.Rectangle(x, y, width, height),
                PixelFormat = PixelFormat.Bgra32,
            };

            try
            {
                using var session = Service().CreateSessionAsync(region, CancellationToken.None)
                    .GetAwaiter().GetResult();
                return Read(session, keeping: false);
            }
            catch (Exception error) when (error is not DeviceUnavailableException
                and not DeviceActionException)
            {
                throw Refused(error);
            }
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
        catch (Exception error) when (error is not DeviceActionException)
        {
            throw Refused(error);
        }
    }

    /// <summary>
    /// One picture out of a session, in the layout the rest of the engine uses. A session that
    /// could not deliver is let go of, because the window it was opened on has been closed or
    /// drawn again from scratch, and the next reading of that window has to open a new one.
    /// </summary>
    private ImageFrame Read(ICaptureSession session, bool keeping)
    {
        try
        {
            var frame = session.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult()
                ?? throw new DeviceActionException("Run.CaptureFailed", "no picture came back");

            return Picture(frame);
        }
        catch (Exception error) when (error is not DeviceUnavailableException
            and not DeviceActionException)
        {
            if (keeping)
            {
                Drop(session);
            }

            throw Refused(error);
        }
    }

    /// <summary>Lets go of the session a window was being read through.</summary>
    private void Drop(ICaptureSession session)
    {
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
