using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Lucide.Avalonia;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// A compact, non-activating 4x6 project menu. The top half pages common macros horizontally;
/// the bottom half contains the layout selected by the foreground window rule.
/// </summary>
public sealed partial class MacroMenuWindow : Window
{
    private const double CellSize = 80;
    private const double CellGap = 4;
    private const double Columns = ProjectTriggers.MenuColumns;
    private const double RegionRows = ProjectTriggers.MenuRegionRows;
    private const double OuterPadding = 8;
    private const double OuterTopPadding = 3;
    private const double FrameInset = 2;
    private const double TitleHeight = 40;
    private const double SearchHeight = 34;
    private const double SearchGap = 6;
    private const double DividerHeight = 44;
    private const double FooterHeight = 28;
    private const double FooterGap = 6;
    private const double RegionsHeight = RegionRows * CellSize * 2
        + (RegionRows - 1) * CellGap * 2;
    private const double BaseHeight = TitleHeight + SearchHeight + SearchGap
        + RegionsHeight + DividerHeight
        + FooterGap + FooterHeight + OuterTopPadding + OuterPadding + 2;
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const int LongPressMilliseconds = 400;
    private static readonly Cursor TitleCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor ButtonCursor = new(StandardCursorType.Hand);

    private readonly MenuWindowActions _actions;
    private readonly Grid _commonGrid;
    private readonly Grid _contextGrid;
    private readonly StackPanel _commonDots;
    private readonly StackPanel _contextDots;
    private readonly TextBlock _contextTitle;
    private readonly Border _searchHost;
    private readonly TextBox _search;
    private readonly Button _searchButton;
    private readonly Button _pinButton;
    private readonly Button _addRuleButton;
    private readonly DispatcherTimer? _menuMoveTimer;
    private readonly DispatcherTimer _longPressTimer;
    private readonly TaskCompletionSource<MacroItem?> _answer =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MenuRuntimeLayout _layout;
    private int _commonPage;
    private int _contextPage;
    private MenuSlotRef? _selected;
    private MenuSlotRef? _dragSource;
    private Button? _dragButton;
    private IPointer? _dragPointer;
    private bool _dragging;
    private bool _suppressClick;
    private bool _done;
    private bool _closing;
    private bool _searchOpen;
    private int _searchFocused;
    private bool _pinned;
    private bool _menuDragging;
    private bool _restoreAfterMenuDrag;
    private int _openPopupCount;

    /// <summary>
    /// The check that the menu being opened actually arrived. It stands from the moment the menu
    /// starts opening until the answer is known, and is what keeps a second right-click from
    /// starting a second menu on top of the first.
    /// </summary>
    private DispatcherTimer? _menuWatch;
    private PixelPoint _anchor;
    private readonly bool _canUseForeground;
    private readonly string _foregroundProcess;
}
