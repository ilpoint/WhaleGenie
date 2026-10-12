using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// The desktop, handed over by the graphics card: the picture the display is being given is
/// duplicated into a texture of our own and copied out of graphics memory, so what comes back is
/// what is on the monitor with no window in the way. It is the cheapest way to read the whole
/// screen — a frame is one copy of 2560×1440×4 ≈ 14.7 MB, measured at 2 ms against 28 ms for
/// copying the screen out with GDI — and it is the only way of reading a game that fills the
/// screen on its own, which GDI answers with black.
/// </summary>
/// <remarks>
/// Everything happens on one thread of this object's own, because the calls belong to one graphics
/// device and are made from the engine's thread and from the interface's. A display that is being
/// switched over to a game, locked, or read by another program too puts the duplication out of
/// work; that is said by handing back nothing, which leaves the caller to read the screen another
/// way, and the duplication is opened again a second later rather than for every look.
/// </remarks>
internal sealed class DesktopDuplicator : IDisposable
{
    /// <summary>How long after a duplication is lost the next attempt is made.</summary>
    private const long RetryMs = 1000;

    /// <summary>How long the first picture a fresh duplication starts with is waited for.</summary>
    private const uint BlankFrameMs = 100;

    private readonly OneThread _thread = new("desktop duplication");

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;

    /// <summary>The whole of the display as it was last read, and what it was read from.</summary>
    private byte[]? _whole;
    private int _width;
    private int _height;

    /// <summary>
    /// How many pictures this duplication has handed over. The first one is the only one at risk of
    /// being blank, which is what <see cref="WorthServing"/> reads it against.
    /// </summary>
    private int _frames;

    private bool _refused;
    private long _againAt;

    /// <summary>
    /// The rectangle that was asked for, out of the display's own picture of itself, or nothing when
    /// this machine cannot duplicate it — no graphics device that will do it, a display another
    /// program is also reading, a picture in a form this does not read, or a display turned on its
    /// side.
    /// </summary>
    public ScreenGrab? Try(int x, int y, int width, int height)
        => _thread.Run(() =>
        {
            if (!Ready())
            {
                return null;
            }

            try
            {
                return Shot(x, y, width, height);
            }
            catch (EmptyRegionException)
            {
                // A rectangle that is not on the display is not something about the duplication: it
                // is the same answer whichever way the screen would have been read, so it is not
                // turned into "this machine cannot do it".
                throw;
            }
            catch (Exception)
            {
                LetGo();
                return null;
            }
        });

    /// <summary>Whether this machine can duplicate the display at all, settled the first time it is asked.</summary>
    private bool Ready()
    {
        if (_duplication is not null)
        {
            return true;
        }

        if (_refused || Environment.TickCount64 < _againAt)
        {
            return false;
        }

        try
        {
            Open();
            return true;
        }
        catch (Exception)
        {
            // A machine with no graphics device that will duplicate the display is not going to
            // grow one while a macro runs, so it is written off rather than tried again.
            _refused = true;
            LetGo();
            return false;
        }
    }

    /// <summary>
    /// Opens the graphics device and the duplication of the display sitting at the top left of the
    /// virtual desktop — the primary one, which is the one the engine's coordinates are counted in.
    /// A display that is rotated is left alone: the picture would come back on its side, and a
    /// macro aiming at it would aim at the wrong place.
    /// </summary>
    private void Open()
    {
        Vortice.Direct3D11.D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1], out ID3D11Device device,
            out ID3D11DeviceContext context);
        _device = device;
        _context = context;

        using var dxgi = device.QueryInterface<IDXGIDevice>();
        var adapter = dxgi.GetAdapter();
        for (uint index = 0; ; index++)
        {
            if (adapter.EnumOutputs(index, out var output).Failure)
            {
                break;
            }

            using (output)
            {
                var places = output.Description.DesktopCoordinates;
                if (places.Left != 0 || places.Top != 0)
                {
                    continue;
                }

                using var direct = output.QueryInterface<IDXGIOutput1>();
                var duplication = direct.DuplicateOutput(device);
                var mode = duplication.Description.ModeDescription;
                if (!Supported(duplication.Description, mode))
                {
                    duplication.Dispose();
                    continue;
                }

                _duplication = duplication;
                _width = (int)mode.Width;
                _height = (int)mode.Height;

                // The picture a fresh duplication starts with is often one nothing has been drawn
                // into yet, so it is kept rather than trusted: the next picture that arrives is the
                // display and replaces it. Keeping it is also what a still screen is read by — a
                // screen nobody is drawing hands nothing else over, and throwing this one away
                // would leave the first look of that screen with nothing to hand to the caller,
                // which reads as a machine that cannot duplicate the display at all.
                Take(BlankFrameMs);
                return;
            }
        }

        throw new InvalidOperationException("no display can be duplicated");
    }

    /// <summary>
    /// Whether a duplicated display is one this reads: a picture of the desktop four bytes to a
    /// pixel, blue first, sitting the right way up. A display showing high dynamic range hands over
    /// a wider picture than that, and one turned on its side hands over a picture lying on its
    /// side; either would have to be turned into the shape the rest of the engine reads, and until
    /// that is done another way of reading the screen is the one to use.
    /// </summary>
    private static bool Supported(OutduplDescription description, ModeDescription mode)
        => description.Rotation == ModeRotation.Identity
            && mode.Format == Format.B8G8R8A8_UNorm
            && mode.Width > 0
            && mode.Height > 0;

    /// <summary>
    /// One reading: whatever the card has drawn since the last look is copied into our own staging
    /// texture and read out of it. A desktop that has not been drawn again has nothing to hand
    /// over, and the picture already held is still what is on screen, so it is the one used.
    /// </summary>
    private ScreenGrab? Shot(int x, int y, int width, int height)
    {
        // Whatever has been drawn since the last look is newer than what is held, so it is the one
        // to serve.
        Take(0);

        if (_whole is not { } whole)
        {
            // Nothing has been drawn since this duplication was opened. Waiting for the card to draw
            // would hold up a step on a screen that is standing still; read another way this once,
            // and the look after it finds the picture waiting.
            return null;
        }

        if (!WorthServing(_frames, whole))
        {
            return null;
        }

        return Placement.Of(new ScreenGrab(_width, _height, whole, CaptureWay.DesktopDuplication),
            0, 0, x, y, width, height);
    }

    /// <summary>
    /// The next picture the duplication hands over, copied into the picture this holds, or false
    /// when it hands over nothing within <paramref name="waitMs"/>. A display that has not been drawn
    /// again hands nothing over, and that is not a failure: what is held is still what is on screen.
    /// </summary>
    private bool Take(uint waitMs)
    {
        if (!_duplication!.AcquireNextFrame(waitMs, out _, out var resource).Success)
        {
            return false;
        }

        using (resource)
        using (var texture = resource.QueryInterface<ID3D11Texture2D>())
        {
            Stage(texture);
            _context!.CopyResource(_staging!, texture);
        }

        _duplication.ReleaseFrame();
        _frames++;
        Hold();
        return true;
    }

    /// <summary>
    /// Whether a picture that was read is one to hand to the caller. The first picture of a fresh
    /// duplication is often blank — a surface the display has not been drawn into yet — so it is
    /// handed over only when there is something in it; from the second picture on, what came back is
    /// the display, however dark the display happens to be.
    /// </summary>
    internal static bool WorthServing(int frames, byte[] picture)
        => frames > 1 || !Blank(picture);

    /// <summary>Whether a picture has nothing drawn in it at all.</summary>
    private static bool Blank(byte[] picture)
    {
        for (var at = 0; at + 2 < picture.Length; at += 4)
        {
            if (picture[at] != 0 || picture[at + 1] != 0 || picture[at + 2] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A staging texture to copy frames into, made once at the size the display is.</summary>
    private void Stage(ID3D11Texture2D texture)
    {
        if (_staging is not null)
        {
            return;
        }

        var shape = texture.Description;
        shape.Usage = ResourceUsage.Staging;
        shape.BindFlags = BindFlags.None;
        shape.CPUAccessFlags = CpuAccessFlags.Read;
        shape.MiscFlags = ResourceOptionFlags.None;
        shape.ArraySize = 1;
        shape.MipLevels = 1;
        shape.SampleDescription = new SampleDescription(1, 0);
        _staging = _device!.CreateTexture2D(shape);
    }

    /// <summary>Reads the staged frame out into the picture this keeps of the whole display.</summary>
    private void Hold()
    {
        var mapped = _context!.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var bytes = _whole ??= new byte[(long)_width * _height * 4];
            for (var row = 0; row < _height; row++)
            {
                Marshal.Copy(mapped.DataPointer + (row * (int)mapped.RowPitch), bytes, row * _width * 4,
                    _width * 4);
            }
        }
        finally
        {
            _context.Unmap(_staging!, 0);
        }
    }

    /// <summary>Puts the duplication down, keeping what was read so that asking again can try anew.</summary>
    private void LetGo()
    {
        _duplication?.Dispose();
        _duplication = null;
        _staging?.Dispose();
        _staging = null;
        _context?.Dispose();
        _context = null;
        _device?.Dispose();
        _device = null;
        _whole = null;
        _frames = 0;
        _againAt = Environment.TickCount64 + RetryMs;
    }

    public void Dispose()
    {
        _thread.Run(() =>
        {
            LetGo();
            return true;
        });

        _thread.Dispose();
    }
}
