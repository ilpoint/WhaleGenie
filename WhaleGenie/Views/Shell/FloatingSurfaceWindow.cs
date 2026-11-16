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
public sealed class FloatingSurfaceWindow : Window
{
    private const double MacroWidth = 80;
    private const double MacroHeight = 24;
    private const double MacroGap = 2;
    private const int MaximumVisible = 6;
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;

    private readonly Action<MacroItem> _run;
    private readonly StackPanel _buttonList;
    private readonly ScrollViewer _scrollViewer;
    private readonly ScrollBar _scrollBar;
    private DispatcherTimer? _moveTimer;
    private readonly List<MacroItem> _macros = [];
    private int _heightEstimate;
    private bool _placing = true;
    private bool _dragging;
    private PixelPoint _dragStartPosition;

    public FloatingSurfaceWindow(IReadOnlyList<MacroItem> macros, Action<MacroItem> run)
    {
        _run = run;
        _heightEstimate = EstimateHeight(macros.Count);
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        SizeToContent = SizeToContent.Height;
        Width = 86;
        Background = Brushes.Transparent;

        _buttonList = new StackPanel { Spacing = MacroGap };
        var handle = new Border
        {
            Width = MacroWidth,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = ThemeBrush.Of("SurfaceHover") ?? Brushes.Transparent,
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
        handle.PointerPressed += (_, e) =>
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
        handle.PointerReleased += (_, _) => _moveTimer?.Start();
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
            if (args.Property == RangeBase.ValueProperty)
            {
                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, _scrollBar.Value);
            }
        };
        _scrollViewer.ScrollChanged += (_, _) => SyncScrollBar();
        SetMacros(macros);

        Content = new Border
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Background = ThemeBrush.Of("SurfacePanel") ?? Brushes.Transparent,
            BorderBrush = ThemeBrush.Of("BorderControl"),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("20,*"),
                ColumnDefinitions = new ColumnDefinitions("80,4"),
                Children =
                {
                    handle,
                    _scrollViewer,
                    _scrollBar,
                },
            },
        };
        Grid.SetRow(_scrollViewer, 1);
        Grid.SetColumn(_scrollBar, 1);
        Grid.SetRow(_scrollBar, 1);
        _moveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background, (_, _) => CompleteMove());
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

    public event Action<PixelPoint>? MoveCompleted;

    public bool IsMoving { get; private set; }

    public IReadOnlyList<string> MacroNames => [.. _macros.Select(macro => macro.Name)];

    internal void BeginDragForCheck()
    {
        _dragStartPosition = Position;
        _dragging = true;
        IsMoving = true;
        _moveTimer?.Start();
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
                TextTrimming = TextTrimming.CharacterEllipsis,
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
        SyncScrollBar();
    }

    private static int EstimateHeight(int count)
        => 20 + Math.Min(MaximumVisible, count) * (int)MacroHeight
           + Math.Max(0, Math.Min(MaximumVisible, count) - 1) * (int)MacroGap
           + 2;

    private void SyncScrollBar()
    {
        var maximum = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        _scrollBar.Maximum = maximum;
        _scrollBar.ViewportSize = _scrollViewer.Viewport.Height;
        _scrollBar.LargeChange = Math.Max(1, _scrollViewer.Viewport.Height);
        _scrollBar.Value = Math.Max(0, Math.Min(maximum, _scrollViewer.Offset.Y));
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
        var width = (int)Math.Ceiling(Bounds.Width > 0 ? Bounds.Width : Width);
        var maxX = Math.Max(0, size.Width - width);
        var x = Math.Clamp(layout.OffsetX, 0, maxX);
        var y = Math.Clamp(layout.OffsetY, 0, Math.Max(0, size.Height - _heightEstimate));
        _placing = true;
        Position = new PixelPoint(origin.X + x, origin.Y + y);
        _placing = false;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
        }
    }


    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
