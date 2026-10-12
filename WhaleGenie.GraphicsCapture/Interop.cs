using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// The pieces of Windows Graphics Capture the projected type library does not carry: making an item
/// out of a window or a display handle, reaching the Direct3D texture a frame was drawn in, and
/// turning off the border Windows draws around whatever is being read.
/// </summary>
internal static class Interop
{
    /// <summary>
    /// The class whose factory makes items. An item is not something a projected type can be asked
    /// for: what a window or a display is has to be handed over directly.
    /// </summary>
    private const string ItemClass = "Windows.Graphics.Capture.GraphicsCaptureItem";

    /// <summary>The interface an item is asked for as.</summary>
    private static readonly Guid Item = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    /// <summary>The Direct3D 11 texture interface a frame's surface is asked for as.</summary>
    private static readonly Guid Texture = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    /// <summary>
    /// The interface that makes an item out of a handle. It is an ordinary COM interface rather than
    /// a Windows Runtime one, so its methods start where IUnknown leaves off.
    /// </summary>
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    /// <summary>
    /// Whether Windows draws its capture border, which is the one thing about a session an older
    /// Windows' type library does not carry. This is a Windows Runtime interface, so the three
    /// IInspectable methods come first and what the interface it extends adds comes before its own:
    /// cursor, then border.
    /// </summary>
    [ComImport]
    [Guid("F2CDD966-22AE-5EA1-9596-3A289344C3BE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureSession3
    {
        void GetIids(out uint count, out IntPtr iids);

        void GetRuntimeClassName(out IntPtr name);

        void GetTrustLevel(out int level);

        void StartCapture();

        [return: MarshalAs(UnmanagedType.U1)]
        bool GetIsCursorCaptureEnabled();

        void PutIsCursorCaptureEnabled([MarshalAs(UnmanagedType.U1)] bool enabled);

        [return: MarshalAs(UnmanagedType.U1)]
        bool GetIsBorderRequired();

        void PutIsBorderRequired([MarshalAs(UnmanagedType.U1)] bool required);
    }

    /// <summary>The interface a frame's surface hands its texture over through.</summary>
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    /// <summary>An item for one window, which is what a session on a window is opened with.</summary>
    public static GraphicsCaptureItem ForWindow(IntPtr window) => ItemFor(window, forWindow: true);

    /// <summary>An item for the primary display, which is what a session on the whole screen is opened with.</summary>
    public static GraphicsCaptureItem ForDisplay() => ItemFor(Primary(), forWindow: false);

    /// <summary>
    /// The texture a frame was drawn in, out of the surface the type library hands over. Nothing of
    /// the surface is kept: the texture is read from and let go of, and the next frame is another.
    /// </summary>
    public static ID3D11Texture2D HeldBy(IDirect3DSurface surface)
    {
        var iid = Texture;
        return new ID3D11Texture2D(surface.As<IDirect3DDxgiInterfaceAccess>().GetInterface(ref iid));
    }

    /// <summary>
    /// A Direct3D 11 device in the shape the capture pool wants it, which is the one the Windows
    /// Runtime hands around: the same device, asked for by the interface the pool takes.
    /// </summary>
    public static IDirect3DDevice Device(IDXGIDevice dxgi)
    {
        var result = CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var device);
        if (result < 0)
        {
            throw new CaptureUnavailableException("the graphics device will not be handed over",
                Marshal.GetExceptionForHR(result)!);
        }

        return MarshalInterface<IDirect3DDevice>.FromAbi(device);
    }

    /// <summary>
    /// Turns off the border Windows draws around whatever is being read, where this Windows has the
    /// interface for saying so — Windows 11 does, Windows 10 19045 and earlier do not. A border is a
    /// nuisance rather than a reason not to read the screen, so anything that goes wrong here leaves
    /// the session as it is.
    /// </summary>
    public static void WithoutBorder(GraphicsCaptureSession session)
    {
        try
        {
            session.As<IGraphicsCaptureSession3>().PutIsBorderRequired(false);
        }
        catch (Exception)
        {
            // An older Windows has no such interface, and a session that will not be asked for one
            // is a session with a border rather than a session that cannot be read.
        }
    }

    private static GraphicsCaptureItem ItemFor(IntPtr handle, bool forWindow)
    {
        var classId = HString.Of(ItemClass);
        try
        {
            var factory = typeof(IGraphicsCaptureItemInterop).GUID;
            var result = RoGetActivationFactory(classId, ref factory, out var activation);
            if (result < 0)
            {
                throw new CaptureUnavailableException("Windows Graphics Capture is not on this machine",
                    Marshal.GetExceptionForHR(result)!);
            }

            try
            {
                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(activation);
                var iid = Item;
                var made = forWindow
                    ? interop.CreateForWindow(handle, ref iid)
                    : interop.CreateForMonitor(handle, ref iid);
                try
                {
                    return GraphicsCaptureItem.FromAbi(made);
                }
                finally
                {
                    Marshal.Release(made);
                }
            }
            finally
            {
                Marshal.Release(activation);
            }
        }
        finally
        {
            WindowsDeleteString(classId);
        }
    }

    /// <summary>
    /// The primary display, which is the one the engine's coordinates are counted in: the display
    /// holding the top left corner of the desktop.
    /// </summary>
    private static IntPtr Primary() => MonitorFromPoint(new NativePoint(), PrimaryMonitor);

    private const int PrimaryMonitor = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll")]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source,
        int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi, out IntPtr device);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, int flags);

    /// <summary>
    /// The Windows Runtime string a class name is asked for as. There is no marshaller for one in
    /// this runtime, so what the platform hands around is built and let go of here.
    /// </summary>
    private static class HString
    {
        public static IntPtr Of(string text)
        {
            var result = WindowsCreateString(text, text.Length, out var hstring);
            if (result < 0)
            {
                throw new CaptureUnavailableException($"\"{text}\" could not be named",
                    Marshal.GetExceptionForHR(result)!);
            }

            return hstring;
        }
    }
}
