using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// The desktop, handed over by the graphics card: the picture a display is being given is duplicated
/// into a texture of our own and copied out of graphics memory, so what comes back is what is on the
/// monitor with no window in the way. It is the cheapest way to read the whole screen — a frame is
/// one copy of 2560×1440×4 ≈ 14.7 MB, measured at 2 ms against 28 ms for copying the screen out with
/// GDI — and it is the only way of reading a game that fills the screen on its own, which GDI
/// answers with black.
/// </summary>
/// <remarks>
/// Everything happens on one thread of this object's own, because the calls belong to one graphics
/// device and are made from the engine's thread and from the interface's. A display that is being
/// switched over to a game, locked, or read by another program too puts the duplication out of
/// work; that is said by handing back nothing, which leaves the caller to read the screen another
/// way, and the duplication is opened again a second later rather than for every look.
///
/// The card hands over one display at a time, so a desktop of several is read one display each and
/// put together: a rectangle straddling two displays takes its left-hand part from one and its
/// right-hand part from the other. A display nobody has asked about yet is not duplicated at all —
/// duplicating one costs, and most looks sit on one display.
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
    private readonly List<Output> _outputs = [];

    private bool _refused;

    /// <summary>
    /// The rectangle that was asked for, out of the displays' own pictures of themselves, or nothing
    /// when this machine cannot duplicate them — no graphics device that will do it, a display
    /// another program is also reading, a picture in a form this does not read, or a display turned
    /// on its side. A rectangle covering several displays is answered with one picture put together
    /// from each, and is refused outright when any one of those displays cannot be read: half a
    /// picture, with the other half black, would say something untrue about the desktop.
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
                // A rectangle that is not on any display is not something about the duplication: it
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

    /// <summary>Whether this machine can duplicate a display at all, settled the first time it is asked.</summary>
    private bool Ready()
    {
        if (_outputs.Count > 0)
        {
            return true;
        }

        if (_refused)
        {
            return false;
        }

        try
        {
            Open();
            return _outputs.Count > 0;
        }
        catch (Exception)
        {
            // A machine with no graphics device that will duplicate a display is not going to grow
            // one while a macro runs, so it is written off rather than tried again.
            _refused = true;
            LetGo();
            return false;
        }
    }

    /// <summary>
    /// Opens the graphics device and notes every display it has an output for. The duplications
    /// themselves are opened when a display is first read, so a desktop of three displays where the
    /// macro only ever looks at one of them duplicates only that one. A display that is rotated is
    /// left out: the picture would come back on its side, and a macro aiming at it would aim at the
    /// wrong place.
    /// </summary>
    private void Open()
    {
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
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
                var area = new ScreenArea(places.Left, places.Top, places.Right - places.Left,
                    places.Bottom - places.Top);
                if (area.IsEmpty)
                {
                    continue;
                }

                // Kept rather than let go of: the duplication is opened from it later, the first
                // time this display is really read.
                _outputs.Add(new Output(area, output.QueryInterface<IDXGIOutput1>()));
            }
        }
    }

    /// <summary>
    /// One reading of the rectangle that was asked for. A rectangle on one display is cut out of
    /// that display's picture; one that straddles two is put together from both.
    /// </summary>
    private ScreenGrab? Shot(int x, int y, int width, int height)
    {
        var pieces = ScreenPlacement.Pieces([.. _outputs.ConvertAll(output => output.Area)],
            x, y, width, height);

        if (pieces.Count == 1)
        {
            var piece = pieces[0];
            return Whole(OutputFor(piece.Display)) is { } picture
                ? Placement.Of(Duplicated(picture), piece.Display.Left, piece.Display.Top,
                    x, y, width, height)
                : null;
        }

        var pixels = new byte[(long)width * height * 4];
        foreach (var piece in pieces)
        {
            if (Whole(OutputFor(piece.Display)) is not { } picture)
            {
                return null;
            }

            Placement.Blit(Duplicated(picture), piece.SourceX, piece.SourceY, pixels, width,
                piece.ToX, piece.ToY, piece.Width, piece.Height);
        }

        return new ScreenGrab(width, height, pixels, CaptureWay.DesktopDuplication);
    }

    private static ScreenGrab Duplicated(Picture picture)
        => new(picture.Width, picture.Bgra.Length / 4 / picture.Width, picture.Bgra,
            CaptureWay.DesktopDuplication);

    /// <summary>The display read this way, out of the ones the adapter listed.</summary>
    private Output OutputFor(ScreenArea area)
        => _outputs.Find(output => output.Area == area)
            ?? throw new EmptyRegionException($"{area.Left},{area.Top} is not a display any more");

    /// <summary>
    /// The display's own picture of itself as it was last read, or nothing when this display cannot
    /// be duplicated — for now, at least, which is what lets the caller read it another way this
    /// once and find this ready again a second later.
    /// </summary>
    private Picture? Whole(Output output)
    {
        if (!output.Begin(_device!, _context!, BlankFrameMs))
        {
            return null;
        }

        // Whatever has been drawn since the last look is newer than what is held, so it is the one
        // to serve.
        output.Take(0, _device!, _context!);

        if (output.Whole is not { } whole)
        {
            // Nothing has been drawn since this duplication was opened. Waiting for the card to draw
            // would hold up a step on a screen that is standing still; read another way this once,
            // and the look after it finds the picture waiting.
            return null;
        }

        return WorthServing(output.Frames, whole) ? new Picture(whole, output.Width) : null;
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

    /// <summary>Puts every duplication down, keeping nothing that came out of them.</summary>
    private void LetGo()
    {
        foreach (var output in _outputs)
        {
            output.Dispose();
        }

        _outputs.Clear();
        _context?.Dispose();
        _context = null;
        _device?.Dispose();
        _device = null;
    }

    public void Dispose()
    {
        _thread.Run(() =>
        {
            LetGo();
            _refused = true;
            return true;
        });

        _thread.Dispose();
    }

    /// <summary>
    /// One display's picture of itself, four bytes to a pixel, and how wide a row of it is. The
    /// height is the height of the picture, which the reader works out from the two.
    /// </summary>
    private sealed record Picture(byte[] Bgra, int Width);

    /// <summary>
    /// One display, its own duplication of it, and the last picture taken out of that. Each display
    /// is duplicated the first time it is read and kept while it lives, so a macro looking at the
    /// same display over and over is handed the picture the card is already giving.
    /// </summary>
    private sealed class Output(ScreenArea area, IDXGIOutput1 direct) : IDisposable
    {
        private IDXGIOutputDuplication? _duplication;
        private ID3D11Texture2D? _staging;

        public ScreenArea Area { get; } = area;

        /// <summary>The whole of the display as it was last read.</summary>
        public byte[]? Whole { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        /// <summary>
        /// How many pictures this duplication has handed over. The first one is the only one at risk
        /// of being blank, which is what <see cref="WorthServing"/> reads it against.
        /// </summary>
        public int Frames { get; private set; }

        /// <summary>When this display may be tried again after a duplication of it was lost.</summary>
        private long AgainAt { get; set; }

        /// <summary>
        /// Starts duplicating this display if it is not already duplicated, and says whether a picture
        /// can be read out of it. A display another program is also reading, one that has gone, or one
        /// handing over a shape this does not read is refused until a second has passed — the display
        /// may be free by then.
        /// </summary>
        public bool Begin(ID3D11Device device, ID3D11DeviceContext context, uint blankMs)
        {
            if (_duplication is not null)
            {
                return true;
            }

            if (Environment.TickCount64 < AgainAt)
            {
                return false;
            }

            try
            {
                var duplication = direct.DuplicateOutput(device);
                var mode = duplication.Description.ModeDescription;
                if (!Supported(duplication.Description, mode))
                {
                    duplication.Dispose();
                    AgainAt = Environment.TickCount64 + RetryMs;
                    return false;
                }

                _duplication = duplication;
                Width = (int)mode.Width;
                Height = (int)mode.Height;

                // The picture a fresh duplication starts with is often one nothing has been drawn
                // into yet, so it is kept rather than trusted: the next picture that arrives is the
                // display and replaces it. Keeping it is also what a still screen is read by — a
                // screen nobody is drawing hands nothing else over, and throwing this one away would
                // leave the first look of that screen with nothing to hand to the caller, which
                // reads as a machine that cannot duplicate the display at all.
                Take(blankMs, device, context);
                return true;
            }
            catch (Exception)
            {
                Close();
                AgainAt = Environment.TickCount64 + RetryMs;
                return false;
            }
        }

        /// <summary>
        /// Whether a duplicated display is one this reads: a picture of the desktop four bytes to a
        /// pixel, blue first, sitting the right way up. A display showing high dynamic range hands
        /// over a wider picture than that, and one turned on its side hands over a picture lying on
        /// its side; either would have to be turned into the shape the rest of the engine reads, and
        /// until that is done another way of reading the screen is the one to use.
        /// </summary>
        private static bool Supported(OutduplDescription description, ModeDescription mode)
            => description.Rotation == ModeRotation.Identity
                && mode.Format == Format.B8G8R8A8_UNorm
                && mode.Width > 0
                && mode.Height > 0;

        /// <summary>
        /// The next picture this display's duplication hands over, copied into the picture held, or
        /// false when it hands over nothing within <paramref name="waitMs"/>. A display that has not
        /// been drawn again hands nothing over, and that is not a failure: what is held is still what
        /// is on screen.
        /// </summary>
        public bool Take(uint waitMs, ID3D11Device device, ID3D11DeviceContext context)
        {
            if (_duplication is null)
            {
                return false;
            }

            if (!_duplication.AcquireNextFrame(waitMs, out _, out var resource).Success)
            {
                return false;
            }

            using (resource)
            using (var texture = resource.QueryInterface<ID3D11Texture2D>())
            {
                var staging = Stage(texture, device);
                context.CopyResource(staging, texture);
                Hold(staging, context);
            }

            _duplication.ReleaseFrame();
            Frames++;
            return true;
        }

        /// <summary>A staging texture to copy frames into, made once at the size the display is.</summary>
        private ID3D11Texture2D Stage(ID3D11Texture2D texture, ID3D11Device device)
        {
            if (_staging is not null)
            {
                return _staging;
            }

            var shape = texture.Description;
            shape.Usage = ResourceUsage.Staging;
            shape.BindFlags = BindFlags.None;
            shape.CPUAccessFlags = CpuAccessFlags.Read;
            shape.MiscFlags = ResourceOptionFlags.None;
            shape.ArraySize = 1;
            shape.MipLevels = 1;
            shape.SampleDescription = new SampleDescription(1, 0);
            return _staging = device.CreateTexture2D(shape);
        }

        /// <summary>Reads the staged frame out into the picture this keeps of the whole display.</summary>
        private void Hold(ID3D11Texture2D staging, ID3D11DeviceContext context)
        {
            var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var bytes = Whole ??= new byte[(long)Width * Height * 4];
                for (var row = 0; row < Height; row++)
                {
                    Marshal.Copy(mapped.DataPointer + (row * (int)mapped.RowPitch), bytes,
                        row * Width * 4, Width * 4);
                }
            }
            finally
            {
                context.Unmap(staging, 0);
            }
        }

        /// <summary>Lets this display's duplication go, keeping nothing that came out of it.</summary>
        public void Close()
        {
            _duplication?.Dispose();
            _duplication = null;
            _staging?.Dispose();
            _staging = null;
            Whole = null;
            Frames = 0;
        }

        public void Dispose()
        {
            Close();
            direct.Dispose();
        }
    }
}
