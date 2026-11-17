using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// A compact group of macro buttons that sits inside one target window without taking keyboard
/// focus from it. The target therefore remains the foreground window while a button is clicked.
/// </summary>
/// <remarks>
/// The panel is deliberately not topmost. It belongs to the target window's layer: inserted just
/// above that window in the ordinary z-order, it is covered by whatever covers the window, instead
/// of floating through the program in front. The handle folds the buttons away and turns the
/// column into a row, so a group can stay out of the way without being closed.
/// </remarks>
public sealed class FloatingSurfaceWindow : Window
{
    private const double MacroWidth = 80;
    private const double MacroHeight = 24;
    private const double MacroGap = 2;
    private const double HeaderHeight = 20;
    private const double PanelWidth = 86;
    private const int MaximumVisible = 6;
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const int GwHwndPrev = 3;
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    private readonly Action<MacroItem> _run;
    private readonly StackPanel _buttonList;
    private readonly ScrollViewer _scrollViewer;
    private readonly ScrollBar _scrollBar;
    private readonly Grid _grid;
    private readonly Border _header;
    private readonly IconView _collapseIcon;
    private readonly IconView _layoutIcon;
    private readonly Button _collapseButton;
    private readonly Button _layoutButton;
    private DispatcherTimer? _moveTimer;
    private readonly List<MacroItem> _macros = [];
    private int _heightEstimate;
    private bool _collapsed;
    private bool _horizontal;
    private bool _placing = true;
    private bool _dragging;
    private PixelPoint _dragStartPosition;

    public FloatingSurfaceWindow(IReadOnlyList<MacroItem> macros, Action<MacroItem> run,
        SurfaceViewState view = default)
    {
        _run = run;
        _collapsed = view.Collapsed;
        _horizontal = view.Horizontal;
        _heightEstimate = EstimateHeight(macros.Count);
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        SizeToContent = SizeToContent.Height;
        Width = PanelWidth;
        Background = Brushes.Transparent;

        _buttonList = new StackPanel { Spacing = MacroGap };
        _scrollViewer = new ScrollViewer
        {
            Width = MacroWidth,
            MaxHeight = MaximumVisible * MacroHeight + (MaximumVisible - 1) * MacroGap,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            Content = _buttonList,
        };
        _scrollBar = new ScrollBar
        {
            Width = 4,
            Orientation = Orientation.Vertical,
            Minimum = 0,
            IsVisible = false,
            Margin = new Thickness(0, 1, 0, 1),
        };
        _scrollBar.PropertyChanged += (_, args) =>
        {
            if (args.Property != RangeBase.ValueProperty)
            {
                return;
            }

            _scrollViewer.Offset = _horizontal
                ? new Vector(_scrollBar.Value, _scrollViewer.Offset.Y)
                : new Vector(_scrollViewer.Offset.X, _scrollBar.Value);
        };
        _scrollViewer.ScrollChanged += (_, _) => SyncScrollBar();
        _scrollViewer.SizeChanged += (_, _) => SyncScrollBar();

        var grip = new Border
        {
            Name = "SurfaceGrip",
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = new IconView
            {
                Data = AppIcons.Move,
                Width = 12,
                Height = 12,
                Stroke = ThemeBrush.Of("TextTertiary"),
                StrokeThickness = 1.3,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        grip.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _dragStartPosition = Position;
                _dragging = true;
                IsMoving = true;
                BeginMoveDrag(e);
                _moveTimer?.Start();
            }
        };
        grip.PointerReleased += (_, _) => _moveTimer?.Start();

        _collapseIcon = HeaderIcon();
        _layoutIcon = HeaderIcon();
        _collapseButton = HeaderButton("SurfaceCollapseButton", _collapseIcon, ToggleCollapsed);
        _layoutButton = HeaderButton("SurfaceLayoutButton", _layoutIcon, ToggleLayout);

        var headerRow = new Grid
        {
            Height = HeaderHeight,
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Children = { grip, _collapseButton, _layoutButton },
        };
        Grid.SetColumn(_collapseButton, 1);
        Grid.SetColumn(_layoutButton, 2);
        _header = new Border
        {
            Name = "SurfaceHeader",
            Height = HeaderHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Background = ThemeBrush.Of("SurfaceHover") ?? Brushes.Transparent,
            Child = headerRow,
        };

        _grid = new Grid
        {
            RowDefinitions = new RowDefinitions("20,*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("80,4"),
            Children = { _header, _scrollViewer, _scrollBar },
        };
        Grid.SetRow(_header, 0);
        Grid.SetColumn(_header, 0);
        Grid.SetColumnSpan(_header, 2);
        Grid.SetRow(_scrollViewer, 1);
        Grid.SetColumn(_scrollViewer, 0);
        Grid.SetRow(_scrollBar, 1);
        Grid.SetColumn(_scrollBar, 1);

        Content = new Border
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Background = ThemeBrush.Of("SurfacePanel") ?? Brushes.Transparent,
            BorderBrush = ThemeBrush.Of("BorderControl"),
            BorderThickness = new Thickness(1),
            Child = _grid,
        };

        SetMacros(macros);
        ApplyArrangement();

        _moveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background, (_, _) => CompleteMove());
        ScalingChanged += (_, _) => ApplyRegion();
        PositionChanged += (_, _) =>
        {
            if (_dragging && !_placing)
            {
                IsMoving = true;
                _moveTimer?.Stop();
                _moveTimer?.Start();
            }
        };
        Closed += (_, _) => _moveTimer?.Stop();
    }

    /// <summary>How the group is shown, kept for the session so a hidden group comes back the same.</summary>
    public readonly record struct SurfaceViewState(bool Collapsed, bool Horizontal);

    public event Action<PixelPoint>? MoveCompleted;

    /// <summary>Raised when the handle folded or turned the group; the host stores it per group.</summary>
    public event Action<SurfaceViewState>? ViewChanged;

    public bool IsMoving { get; private set; }

    public IReadOnlyList<string> MacroNames => [.. _macros.Select(macro => macro.Name)];

    internal bool IsCollapsedForCheck => _collapsed;

    internal bool IsHorizontalForCheck => _horizontal;

    internal bool BodyVisibleForCheck => _scrollViewer.IsVisible;

    internal Orientation ButtonOrientationForCheck => _buttonList.Orientation;

    internal double HeaderHeightForCheck => _header.Height;

    internal VerticalAlignment HeaderAlignmentForCheck => _header.VerticalAlignment;

    internal void ToggleCollapsedForCheck() => ToggleCollapsed();

    internal void ToggleLayoutForCheck() => ToggleLayout();

    internal void BeginDragForCheck()
    {
        _dragStartPosition = Position;
        _dragging = true;
        IsMoving = true;
        _moveTimer?.Start();
    }

    private static IconView HeaderIcon() => new()
    {
        Width = 11,
        Height = 11,
        Stroke = ThemeBrush.Of("TextSecondary"),
        StrokeThickness = 1.5,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button HeaderButton(string name, IconView icon, Action click)
    {
        var button = new Button
        {
            Name = name,
            Width = 16,
            Height = 16,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Content = icon,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
        ApplyArrangement();
        ViewChanged?.Invoke(new SurfaceViewState(_collapsed, _horizontal));
    }

    private void ToggleLayout()
    {
        _horizontal = !_horizontal;
        ApplyArrangement();
        ViewChanged?.Invoke(new SurfaceViewState(_collapsed, _horizontal));
    }

    /// <summary>
    /// Puts the buttons in the column or the row the handle asks for, folds them away when it does,
    /// and sizes the window to that shape. The window's width is always explicit, so the placement
    /// can clamp it against the target even before the layout has run.
    /// </summary>
    private void ApplyArrangement()
    {
        _buttonList.Orientation = _horizontal ? Orientation.Horizontal : Orientation.Vertical;
        _heightEstimate = EstimateHeight(_macros.Count);
        UpdateHeader();

        if (_collapsed)
        {
            _scrollViewer.IsVisible = false;
            _scrollBar.IsVisible = false;
            // Windows will not make a top-level window as short as the handle, so the folded window
            // keeps a taller rectangle and its region clips that to the top strip. The handle has to
            // stay on the top edge: centred in the taller rectangle, its buttons would be cut.
            _grid.RowDefinitions = new RowDefinitions("Auto,0,Auto");
            _grid.ColumnDefinitions = new ColumnDefinitions("*");
            _header.Height = HeaderHeight;
            Grid.SetColumnSpan(_header, 1);
            Width = PanelWidth;
            ApplyRegion();
            return;
        }

        _scrollViewer.IsVisible = true;
        _grid.RowDefinitions = new RowDefinitions("20,*,Auto");
        _header.Height = HeaderHeight;
        if (_horizontal)
        {
            _grid.ColumnDefinitions = new ColumnDefinitions("*");
            Grid.SetColumnSpan(_header, 1);
            Grid.SetRow(_scrollViewer, 1);
            Grid.SetColumn(_scrollViewer, 0);
            Grid.SetColumnSpan(_scrollViewer, 1);
            Grid.SetRow(_scrollBar, 2);
            Grid.SetColumn(_scrollBar, 0);
            Grid.SetColumnSpan(_scrollBar, 1);
            _scrollViewer.Width = double.NaN;
            _scrollViewer.Height = MacroHeight;
            _scrollViewer.MaxWidth = MaximumVisible * MacroWidth + (MaximumVisible - 1) * MacroGap;
            _scrollViewer.MaxHeight = double.PositiveInfinity;
            _scrollViewer.HorizontalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden;
            _scrollViewer.VerticalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            _scrollBar.Orientation = Orientation.Horizontal;
            _scrollBar.Width = double.NaN;
            _scrollBar.Height = 4;
            _scrollBar.Margin = new Thickness(1, 0, 1, 0);
            var visible = Math.Max(1, Math.Min(MaximumVisible, _macros.Count));
            Width = _macros.Count == 0
                ? PanelWidth
                : visible * MacroWidth + (visible - 1) * MacroGap + 2;
        }
        else
        {
            _grid.ColumnDefinitions = new ColumnDefinitions("80,4");
            Grid.SetColumnSpan(_header, 2);
            Grid.SetRow(_scrollViewer, 1);
            Grid.SetColumn(_scrollViewer, 0);
            Grid.SetColumnSpan(_scrollViewer, 1);
            Grid.SetRow(_scrollBar, 1);
            Grid.SetColumn(_scrollBar, 1);
            Grid.SetColumnSpan(_scrollBar, 1);
            _scrollViewer.Width = MacroWidth;
            _scrollViewer.Height = double.NaN;
            _scrollViewer.MaxWidth = double.PositiveInfinity;
            _scrollViewer.MaxHeight = MaximumVisible * MacroHeight + (MaximumVisible - 1) * MacroGap;
            _scrollViewer.HorizontalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            _scrollViewer.VerticalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden;
            _scrollBar.Orientation = Orientation.Vertical;
            _scrollBar.Width = 4;
            _scrollBar.Height = double.NaN;
            _scrollBar.Margin = new Thickness(0, 1, 0, 1);
            Width = PanelWidth;
        }

        SyncScrollBar();
        ApplyRegion();
    }

    /// <summary>
    /// Windows will not make a top-level window shorter than its own minimum, so a folded panel
    /// keeps a 39-pixel window. The window is clipped to the handle's own height instead: the rest
    /// of it is not drawn and not clickable, and clicks there reach the window underneath.
    /// </summary>
    private void ApplyRegion()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!_collapsed)
        {
            // A null region means "no region": the window draws and answers everywhere again.
            _ = SetWindowRgn(handle, IntPtr.Zero, true);
            return;
        }

        var scale = RenderScaling > 0 ? RenderScaling : 1;
        var width = (int)Math.Ceiling(Width * scale) + 1;
        var height = (int)Math.Ceiling(HeaderHeight * scale) + 1;
        var round = Math.Max(2, (int)Math.Round(12 * scale));
        var region = CreateRoundRectRgn(0, 0, width, height, round, round);
        if (region == IntPtr.Zero)
        {
            return;
        }

        if (SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private void UpdateHeader()
    {
        _collapseIcon.Data = _collapsed ? AppIcons.Expand : AppIcons.Collapse;
        ToolTip.SetTip(_collapseButton,
            Strings.Get(_collapsed ? "Surface.Expand" : "Surface.Collapse"));
        _layoutIcon.Data = _horizontal ? AppIcons.ArrangeVertical : AppIcons.ArrangeHorizontal;
        ToolTip.SetTip(_layoutButton,
            Strings.Get(_horizontal ? "Surface.ArrangeVertical" : "Surface.ArrangeHorizontal"));
    }

    private void CompleteMove()
    {
        if (!_dragging)
        {
            _moveTimer?.Stop();
            return;
        }

        if (OperatingSystem.IsWindows() && (GetAsyncKeyState(0x01) & 0x8000) != 0)
        {
            return;
        }

        _moveTimer?.Stop();
        _dragging = false;
        IsMoving = false;
        if (Position != _dragStartPosition)
        {
            MoveCompleted?.Invoke(Position);
        }
    }

    public void SetMacros(IReadOnlyList<MacroItem> macros)
    {
        if (_macros.SequenceEqual(macros))
        {
            return;
        }

        _macros.Clear();
        _macros.AddRange(macros);
        _buttonList.Children.Clear();
        foreach (var macro in _macros)
        {
            var label = new TextBlock
            {
                Text = macro.Name,
                FontSize = 11,
                MaxWidth = MacroWidth - 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var button = new Button
            {
                Width = MacroWidth,
                Height = MacroHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0),
                CornerRadius = new CornerRadius(4),
                Background = ThemeBrush.Of("SurfaceControl") ?? Brushes.Transparent,
                Foreground = ThemeBrush.Of("TextPrimary"),
                Content = label,
            };
            ToolTip.SetTip(button, macro.Name);
            button.Click += (_, _) => _run(macro);
            _buttonList.Children.Add(button);
        }

        _heightEstimate = EstimateHeight(_macros.Count);
        ApplyArrangement();
    }

    private int EstimateHeight(int count)
    {
        if (_collapsed)
        {
            return (int)HeaderHeight + 2;
        }

        if (_horizontal)
        {
            return (int)(HeaderHeight + MacroHeight + 2) + (count > MaximumVisible ? 4 : 0);
        }

        var visible = Math.Min(MaximumVisible, count);
        return (int)(HeaderHeight + 2) + visible * (int)MacroHeight
               + Math.Max(0, visible - 1) * (int)MacroGap;
    }

    private void SyncScrollBar()
    {
        if (_collapsed)
        {
            _scrollBar.IsVisible = false;
            return;
        }

        var viewport = _horizontal ? _scrollViewer.Viewport.Width : _scrollViewer.Viewport.Height;
        var extent = _horizontal ? _scrollViewer.Extent.Width : _scrollViewer.Extent.Height;
        var offset = _horizontal ? _scrollViewer.Offset.X : _scrollViewer.Offset.Y;
        var maximum = Math.Max(0, extent - viewport);
        _scrollBar.Maximum = maximum;
        _scrollBar.ViewportSize = viewport;
        _scrollBar.LargeChange = Math.Max(1, viewport);
        _scrollBar.Value = Math.Max(0, Math.Min(maximum, offset));
        _scrollBar.IsVisible = maximum > 0.5;
    }

    /// <summary>Places the group relative to the target window's client area.</summary>
    public void PlaceAt(WindowInfo target, IWindowDevice windows, InterfaceLayout layout)
    {
        if (IsMoving)
        {
            return;
        }

        var origin = windows.ClientOrigin(target.Handle);
        var size = windows.ClientSize(target.Handle);
        var width = (int)Math.Ceiling(double.IsFinite(Width) ? Width : Bounds.Width);
        var height = Bounds.Height > 0 ? (int)Math.Ceiling(Bounds.Height) : _heightEstimate;
        var maxX = Math.Max(0, size.Width - width);
        var x = Math.Clamp(layout.OffsetX, 0, maxX);
        var y = Math.Clamp(layout.OffsetY, 0, Math.Max(0, size.Height - height));
        _placing = true;
        Position = new PixelPoint(origin.X + x, origin.Y + y);
        _placing = false;
    }

    /// <summary>
    /// Puts the panel directly above the target window in the ordinary z-order. It is not topmost:
    /// a window in front of the target covers the panel, the same way it covers the target.
    /// </summary>
    public void SitAbove(long targetHandle)
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || targetHandle == 0)
        {
            return;
        }

        // SetWindowPos places the window behind the handle it is given. Asking for the window
        // directly above the target — its predecessor in the z-order — and going behind that one
        // lands just above the target. A target at the very front has no predecessor, and there
        // the panel goes on top of the ordinary windows, which is the same place.
        var target = new IntPtr(targetHandle);
        var above = GetWindow(target, GwHwndPrev);
        SetWindowPos(handle, above == IntPtr.Zero ? HwndTop : above, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
            ApplyRegion();
        }
    }


    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int command);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom,
        int width, int height);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
