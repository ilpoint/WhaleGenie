using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// Pictures taken through Windows Graphics Capture: the machinery that composes windows on screen is
/// asked for a window's own picture instead of the pixels where the window happens to be, so a game
/// another window is covering is read as if it were in front. The whole of a display can be read the
/// same way.
/// </summary>
/// <remarks>
/// Everything happens on one thread of this object's own, because the capture pool and the graphics
/// device behind it belong to one thread at a time, and the callers are the engine's thread and the
/// interface's. Sessions are handed out rather than kept here: who needs one and for how long is
/// <see cref="ScreenGrabber"/>'s business.
/// </remarks>
internal sealed class WindowsGraphicsCapture : IPictureSources
{
    private readonly OneThread _thread = new("graphics capture");

    private Devices? _devices;
    private bool _unavailable;

    /// <summary>
    /// Whether this machine can read the screen this way at all. Asked once and remembered: a machine
    /// without Windows Graphics Capture is not going to grow one while a macro runs.
    /// </summary>
    public bool Available => _thread.Run(() =>
    {
        if (_unavailable)
        {
            return false;
        }

        try
        {
            return GraphicsCaptureSession.IsSupported();
        }
        catch (Exception)
        {
            _unavailable = true;
            return false;
        }
    });

    public IPictureSource Open(SourceKey key) => _thread.Run(() =>
    {
        try
        {
            var item = key.IsDisplay
                ? Interop.ForDisplay(key.Display)
                : Interop.ForWindow(new IntPtr(key.Window));
            return (IPictureSource)new OneThreadPictureSource(Session(item, key), _thread);
        }
        catch (CaptureUnavailableException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new CaptureUnavailableException(
                key.IsDisplay
                    ? $"the display at {key.Display.Left},{key.Display.Top} cannot be read this way"
                    : $"{key.Window:X} cannot be read this way",
                error);
        }
    });

    /// <summary>
    /// One session on one item: a pool of pictures the size of what is being read, the session that
    /// keeps it filling, and the reading of a frame out of it.
    /// </summary>
    private WindowsCaptureSession Session(GraphicsCaptureItem item, SourceKey key)
    {
        var devices = Device();
        var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(devices.Runtime,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        var session = pool.CreateCaptureSession(item);

        // The pointer is drawn into the picture the way the screen shows it: a macro aiming at what
        // is under the pointer would otherwise be aiming at a picture the pointer is missing from.
        session.IsCursorCaptureEnabled = true;
        Interop.WithoutBorder(session);
        session.StartCapture();
        return new WindowsCaptureSession(devices.Direct3D, devices.Context, pool, session, key);
    }

    /// <summary>
    /// The graphics device the pictures are drawn in, built on first use: a macro that never reads a
    /// window never builds one, and one device serves every session. It is asked for twice, because
    /// the Direct3D calls are made through one shape of it and the capture pool takes the other.
    /// </summary>
    private Devices Device()
    {
        if (_devices is { } kept)
        {
            return kept;
        }

        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1], out ID3D11Device device,
            out ID3D11DeviceContext context);
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        return (_devices = new Devices(device, context, Interop.Device(dxgi))).Value;
    }

    public void Dispose()
    {
        _thread.Run(() =>
        {
            if (_devices is { } devices)
            {
                // The Windows Runtime device holds its own reference to the Direct3D device. It has
                // to be let go of here as well, or closing the Direct3D device only drops one of
                // the references and leaves the graphics device alive after the last capture.
                devices.Runtime.Dispose();
                devices.Context.Dispose();
                devices.Direct3D.Dispose();
            }

            _devices = null;
            return true;
        });

        _thread.Dispose();
    }

    /// <summary>
    /// The graphics device, in the shapes it is asked for in. The context is kept rather than fetched
    /// per reading: the one this device hands out is the device's own, and letting go of it after a
    /// reading leaves the next one holding a context that has been let go of.
    /// </summary>
    private readonly record struct Devices(ID3D11Device Direct3D, ID3D11DeviceContext Context,
        IDirect3DDevice Runtime);
}

/// <summary>
/// One open session on one window, or on the whole of one display: pictures come out of a pool that
/// belongs to a graphics device, and the session is kept until what it reads can no longer be read
/// through it — which is what <see cref="ScreenGrabber"/> opens another one for.
/// </summary>
internal sealed class WindowsCaptureSession : IPictureSource
{
    /// <summary>
    /// How long one frame is waited for before a reading is called failed. A window that is being
    /// drawn hands a frame over within a refresh or two; one that has been shrunk hands nothing over
    /// however long it is waited for, and a macro should not spend a second of its own timeout on
    /// learning that.
    /// </summary>
    private const int FrameWaitMs = 500;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly SourceKey _key;

    public WindowsCaptureSession(ID3D11Device device, ID3D11DeviceContext context,
        Direct3D11CaptureFramePool pool, GraphicsCaptureSession session, SourceKey key)
    {
        _device = device;
        _context = context;
        _pool = pool;
        _session = session;
        _key = key;
    }

    /// <summary>
    /// One picture of the whole of what this reads, and where it sits on the screen. A frame that
    /// arrives at another size than it was asked for is read at the size it arrived at: what is
    /// being read has been resized, and the picture says so by being another size.
    /// </summary>
    public Picture Read()
    {
        using var frame = Frame();
        var size = frame.ContentSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new CaptureFailedException($"nothing came back to read: {size.Width}x{size.Height}");
        }

        var pixels = Copy(Interop.HeldBy(frame.Surface), size.Width, size.Height);
        var grab = new ScreenGrab(size.Width, size.Height, pixels, CaptureWay.GraphicsCapture);

        // A display's picture starts at that display's own corner, which is not the corner of the
        // desktop once there is more than one of them. A window's starts where the window is seen,
        // which is where this picture was taken from — the outer rectangle Windows reports runs a
        // few pixels past what is drawn.
        if (_key.IsDisplay)
        {
            return new Picture(grab, _key.Display.Left, _key.Display.Top);
        }

        var seen = WindowsDesktop.Visible(_key.Window);
        return new Picture(grab, seen.X, seen.Y);
    }

    /// <summary>
    /// One frame, waited for briefly. A frame that does not arrive is reported rather than waited
    /// for again and again: a session that has stopped delivering has to be opened again, and the
    /// macro's own clock belongs to the step rather than to the reading.
    /// </summary>
    private Direct3D11CaptureFrame Frame()
    {
        var at = Environment.TickCount64;
        while (true)
        {
            if (_pool.TryGetNextFrame() is { } frame)
            {
                return frame;
            }

            if (Environment.TickCount64 - at > FrameWaitMs)
            {
                throw new CaptureFailedException("no picture came back");
            }

            Thread.Sleep(1);
        }
    }

    /// <summary>
    /// The pixels of one texture out of graphics memory: four bytes to a pixel, blue first, and one
    /// row after another with no padding between them. The frames are not laid out the way they are
    /// read — what the card pads a row with is the card's business — so every row is copied on its
    /// own, at the pitch it was mapped with.
    /// </summary>
    private byte[] Copy(ID3D11Texture2D texture, int width, int height)
    {
        try
        {
            var shape = texture.Description;
            shape.Usage = ResourceUsage.Staging;
            shape.BindFlags = BindFlags.None;
            shape.CPUAccessFlags = CpuAccessFlags.Read;
            shape.MiscFlags = ResourceOptionFlags.None;
            shape.ArraySize = 1;
            shape.MipLevels = 1;
            shape.SampleDescription = new SampleDescription(1, 0);

            using var staging = _device.CreateTexture2D(shape);
            _context.CopyResource(staging, texture);
            var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var pixels = new byte[(long)width * height * 4];
                for (var row = 0; row < height; row++)
                {
                    Marshal.Copy(mapped.DataPointer + (row * (int)mapped.RowPitch), pixels,
                        row * width * 4, width * 4);
                }

                return pixels;
            }
            finally
            {
                _context.Unmap(staging, 0);
            }
        }
        finally
        {
            texture.Dispose();
        }
    }

    /// <summary>
    /// Lets go of the session. <c>Dispose</c> closes the pool and the session rather than only
    /// dropping the references to them: an unclosed session keeps the graphics device and the pool
    /// alive, and Windows keeps the capture border up while one is open.
    /// </summary>
    public void Dispose()
    {
        try
        {
            _session.Dispose();
        }
        catch (COMException)
        {
            // The desktop lifetime can dispose this after its owning apartment has gone. There is
            // no useful retry from another thread; dropping our references is the safe last act.
        }

        try
        {
            _pool.Dispose();
        }
        catch (COMException)
        {
            // Same last-act rule as the session above.
        }
    }
}
