using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.Views;

/// <summary>What a pick on the look's own picture is for.</summary>
public enum LookPickMode
{
    /// <summary>One point, handed back as an offset from the middle of what the step found.</summary>
    Offset,

    /// <summary>Any number of points, each handed back as an offset and the colour there.</summary>
    Colours,
}

/// <summary>
/// Picks points on the picture the step itself was looking at, rather than on the reference picture
/// it was looking for. The difference is what the two questions need: a template is a stamp that
/// says nothing about where on the screen it turned up, while the look has the whole screen on it —
/// so a place beside the thing found, and a colour a few pixels away from it, can both be aimed at
/// by eye.
/// </summary>
public partial class LookPickWindow : AppWindow
{
    /// <summary>The room the picture is fitted into, and how far it may be blown up.</summary>
    private const double RoomWidth = 690;
    private const double RoomHeight = 430;
    private const double MostZoom = 4;

    /// <summary>How small a mark may be drawn: a colour hit is one pixel of a whole screen.</summary>
    private const double SmallestMark = 4;

    private readonly ImageFrame _frame;
    private readonly ScreenPoint _origin;
    private readonly IReadOnlyList<LookBox> _boxes;
    private readonly int _chosen;
    private readonly LookPickMode _mode;
    private readonly ScreenPoint _centre;
    private readonly IReadOnlyList<VariableChoice> _variables;
    private readonly WriteableBitmap? _bitmap;
    private double _zoom;

    private readonly List<string> _added = [];
    private readonly List<ScreenPoint> _addedAt = [];

    private Canvas? _board;
    private TextBlock? _readout;
    private StackPanel? _entries;
    private Button? _confirm;
    private Canvas? _loupeLayer;

    /// <summary>放大镜：整块画面缩到窗口里之后，一个像素的偏移靠眼睛是分不出来的。</summary>
    private readonly Magnifier _magnifier = new();

    /// <summary>The row of the list that is being worked on, or -1 when none is.</summary>
    private int _selected = -1;

    private ScreenPoint? _picked;
}
