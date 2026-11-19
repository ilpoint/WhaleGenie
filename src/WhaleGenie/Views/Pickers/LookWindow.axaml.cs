using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Views;

/// <summary>
/// What one step saw when it looked at the screen: the picture, every place it found what it was
/// after, and the scores. This is the window that answers "为什么没找到" — it shows what the
/// machine was actually looking at, which a line of text in a log cannot.
/// </summary>
public partial class LookWindow : AppWindow
{
    /// <summary>How small a mark may be drawn: a colour hit is one pixel of a whole screen.</summary>
    private const double SmallestMark = 9;

    /// <summary>How far down the window the title bar it draws reaches, margins included: this is
    /// the one part of a window with no decorations that acts like a title bar.</summary>
    private const double TitleBarStrip = 44;

    /// <summary>
    /// The step this window was opened on, and the machine it looks at, when whoever opened it had
    /// them: with both of these the window can look again by itself. Null when the picture belongs
    /// to a run rather than to a step being written, and then there is nothing to look again at.
    /// </summary>
    private readonly ExecutableStep? _step;
    private readonly IDeviceLayer? _devices;

    private StepLook _look;
    private WriteableBitmap? _frame;
    private WriteableBitmap? _needle;

    private ScrollViewer? _scroll;
    private Canvas? _board;
    private StackPanel? _marks;

    /// <summary>The rows beside the picture, in the order they were drawn: what each one says,
    /// what it stands for, and the switch that shows it on the picture.</summary>
    private readonly List<string> _markTexts = [];

    private readonly List<string> _markRoles = [];
    private readonly List<Border> _markRows = [];
    private readonly List<CheckBox> _markToggles = [];
    private TextBlock? _zoomText;
    private Border? _needleFrame;
    private Image? _needleShot;

    private readonly List<(LookBox Box, Border Visual)> _drawn = [];

    private double _zoom = 1;

    /// <summary>Whether the zoom on show is the one that fits; anything else is the user's own.</summary>
    private bool _fitted = true;

    /// <summary>True while the picture is being dragged, and where the drag started.</summary>
    private bool _panning;
    private Point _panFrom;
    private Vector _panOrigin;

    /// <summary>What was read off this picture, one reading to a line, for taking away.</summary>

    /// <summary>True while a second look is being taken, so one press is one look.</summary>
    private bool _looking;
}
