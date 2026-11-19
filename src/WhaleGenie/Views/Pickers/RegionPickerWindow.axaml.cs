using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.Views;

/// <summary>
/// Shows the desk and lets the user drag a rectangle on it, with everything outside the rectangle
/// pushed back so that the part being framed reads plainly. A rectangle drawn in one go is never
/// quite where it should be, so one that is already there can be moved by dragging inside it and
/// resized by dragging any of its eight handles. The rectangle comes back in screen pixels, which
/// is what every action that works on a region expects.
/// </summary>
public partial class RegionPickerWindow : Window
{
    /// <summary>A rectangle of the screen, in the pixels the engine works in.</summary>
    public readonly record struct Region(int X, int Y, int Width, int Height);

    /// <summary>The two ends of a drag, in the order the pointer visited them.</summary>
    public readonly record struct DragSpan(int StartX, int StartY, int EndX, int EndY);

    /// <summary>A drag shorter than this is a click rather than a selection, and no handle pulls a
    /// rectangle smaller than this either: a rectangle too small to be seen cannot be confirmed.</summary>
    private const int MinimumSize = 4;

    /// <summary>How big a handle is drawn, which is also how close to an edge counts as being on it.</summary>
    private const double HandleSize = 7;

    /// <summary>
    /// 方块往外还能抓住多远。只用几像素：框外再远一点按下去就是「另画一个」，否则想重新框一块
    /// 紧挨着现有框的地方时，会先抓住旧框的边角，一拖就把旧框拉成一条线。
    /// </summary>
    private const double GrabOutside = 3;

    /// <summary>Where a handle sits on a rectangle, as a fraction of it: the four corners first,
    /// then the middle of each side.</summary>
    private static readonly (double X, double Y)[] HandlePlaces =
    [
        (0, 0), (1, 0), (0, 1), (1, 1),
        (0.5, 0), (0.5, 1), (0, 0.5), (1, 0.5),
    ];

    internal static readonly Cursor MoveCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor WestEast = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor NorthSouth = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor NorthWestCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor NorthEastCursor = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor SouthWestCursor = new(StandardCursorType.BottomLeftCorner);
    private static readonly Cursor SouthEastCursor = new(StandardCursorType.BottomRightCorner);

    /// <summary>What a press is doing: drawing a new rectangle, moving the one that is there, or
    /// pulling one of its edges or corners.</summary>
    private enum Grip
    {
        None,
        Move,
        North,
        South,
        East,
        West,
        NorthEast,
        NorthWest,
        SouthEast,
        SouthWest,
    }

    private readonly IWindowDevice _windows;
    private readonly IScreenDevice _screen;
    private readonly Region? _initial;
    private readonly Region? _bounds;
    private readonly bool _arrow;
    private readonly long _liveWindow;
    private readonly Region? _liveOriginal;
    private readonly Border[] _handles;
    private readonly Border[] _targetDim;

    /// <summary>The four bands that push back what is outside the rectangle.</summary>
    private readonly Border[] _dim;

    private readonly Image? _desk;

    private readonly Line _arrowShaft;
    private readonly Line _arrowLeft;
    private readonly Line _arrowRight;
    private readonly Ellipse _arrowStart;

    /// <summary>The desk as it was when this opened, which is what stands in for the screen.</summary>
    private WriteableBitmap? _picture;

    /// <summary>The same desk as pixels, which is what the loupe reads.</summary>
    private ImageFrame? _deskFrame;

    /// <summary>放大镜：拉边、拉角时看得见手里那一像素落在哪。</summary>
    private readonly Magnifier _magnifier = new();

    /// <summary>放大镜这一帧落在哪，读数气泡要挂在它下面。</summary>
    private Rect? _loupeAt;

    /// <summary>Where the screen starts, in screen pixels; the window sits exactly there.</summary>
    private PixelPoint _origin;

    private double _scaling = 1;

    /// <summary>
    /// 键盘从钩子读而不是从这个窗口读：拾取器开在最上面，但 Windows 不保证把前台交给它，
    /// 交不出去的时候 F1 与 Esc 就落到了别的窗口上，看起来像"按了没反应"。钩子读得到。
    /// </summary>
    private static readonly HookKey F1 = KeyNames.Resolve("F1") ?? default;
    private static readonly HookKey Escape = KeyNames.Resolve("Esc") ?? default;

    private readonly IInputHook? _hookSource;
    private IInputHook? _hook;

    private Border? _selection;
    private Border? _allowedArea;
    private Border? _readout;
    private TextBlock? _sizeText;
    private TextBlock? _hintText;

    /// <summary>Whether a press is being followed, and what it is doing.</summary>
    private bool _active;
    private Grip _grip;

    /// <summary>Where the pointer went down, and the rectangle as it stood then.</summary>
    private PixelPoint _grab;
    private int _left;
    private int _top;
    private int _right;
    private int _bottom;

    /// <summary>The rectangle as two opposite corners, which is how a drag is followed.</summary>
    private PixelPoint _start;
    private PixelPoint _end;
    private PixelPoint? _pointer;
    private Region? _liveAt;
    private bool _done;
}
