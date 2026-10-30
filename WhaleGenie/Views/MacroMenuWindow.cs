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

/// <summary>One editable or runnable position on a common or context page.</summary>
public sealed record MenuSlotRef(
    string PageId,
    int Row,
    int Column,
    MenuRuntimeCell? Cell)
{
    public string Key => $"{PageId}:{Row}:{Column}";
}

public sealed record MenuWindowActions(
    Action<MacroItem> OpenMacroEditor,
    Action<MacroItem> DebugMacro,
    Action OpenSettings,
    Action RestoreForeground,
    Func<Task<MenuRuntimeLayout?>> AddCurrentWindowRule,
    Func<Task<bool>> BlockCurrentWindow,
    Func<Task<MenuRuntimeLayout?>> AddCommonPage,
    Func<string, Task<MenuRuntimeLayout?>> RemoveCommonPage,
    Func<string, Task<MenuRuntimeLayout?>> AddContextPage,
    Func<string, Task<MenuRuntimeLayout?>> RemoveContextPage,
    Func<MenuSlotRef, Task<MenuRuntimeLayout?>> ChooseMacro,
    Func<MenuSlotRef, Task<MenuRuntimeLayout?>> ChooseIcon,
    Func<MenuSlotRef, MenuSlotRef, Task<MenuRuntimeLayout?>> Move);

/// <summary>
/// A compact, non-activating 4x6 project menu. The top half pages common macros horizontally;
/// the bottom half contains the layout selected by the foreground window rule.
/// </summary>
public sealed class MacroMenuWindow : Window
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
    private PixelPoint _anchor;
    private readonly bool _canUseForeground;
    private readonly string _foregroundProcess;

    public MacroMenuWindow(MenuRuntimeLayout layout, MenuWindowActions actions,
        string foregroundProcess = "")
    {
        _layout = layout;
        _actions = actions;
        _foregroundProcess = foregroundProcess.Trim();
        _canUseForeground = _foregroundProcess.Length > 0;

        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        Width = Columns * CellSize + (Columns - 1) * CellGap + OuterPadding * 2 + 2;
        Height = BaseHeight;

        _commonGrid = GridFor();
        _contextGrid = GridFor();
        _commonDots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _contextDots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _contextTitle = new TextBlock
        {
            FontSize = 10,
            Foreground = Brush("AppMuted", Colors.LightGray),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _search = new TextBox
        {
            Height = SearchHeight,
            FontSize = 12,
            PlaceholderText = Strings.Get("Menu.Search"),
            Focusable = false,
            IsTabStop = false,
        };
        _search.TextChanged += (_, _) => OnSearchChanged();
        _search.GotFocus += (_, _) => BeginSearchFocus();
        _search.LostFocus += (_, _) => EndSearchFocus();
        _search.KeyDown += OnSearchKeyDown;
        _search.PointerPressed += (_, _) => BeginSearchFocus();
        _searchHost = new Border
        {
            IsVisible = true,
            Opacity = 0,
            IsHitTestVisible = false,
            Margin = new Thickness(0, 0, 0, SearchGap),
            Child = _search,
        };

        var title = new Grid
        {
            Name = "MenuTitleBar",
            Height = TitleHeight,
            ColumnDefinitions = new ColumnDefinitions("28,Auto,*,Auto,Auto,Auto"),
            Margin = new Thickness(0),
            Cursor = TitleCursor,
            Background = Brush("AppChrome", Color.FromArgb(120, 20, 20, 20)),
        };
        ToolTip.SetTip(title, Strings.Get("Menu.Drag"));
        title.PointerPressed += (_, e) =>
        {
            if (IsInsideButton(e.Source)
                || !e.GetCurrentPoint(title).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _menuDragging = true;
            _restoreAfterMenuDrag = IsSearchFocused;
            BeginMoveDrag(e);
            _menuMoveTimer?.Stop();
            _menuMoveTimer?.Start();
        };
        title.PointerReleased += (_, _) => _menuMoveTimer?.Start();
        title.Children.Add(new Image
        {
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform,
            Source = new Bitmap(AssetLoader.Open(
                new Uri("avares://WhaleGenie/Assets/WhaleGenie.png"))),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var titleArea = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleArea.Children.Add(new TextBlock
        {
            Text = Strings.Get("Main.Title"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("AppText", Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var settings = DividerButton(AppIcons.Settings, Strings.Get("Menu.Settings"));
        settings.Name = "MenuSettingsButton";
        settings.Click += (_, _) =>
        {
            CloseForAction();
            _actions.OpenSettings();
        };
        titleArea.Children.Add(settings);
        Grid.SetColumn(titleArea, 1);
        title.Children.Add(titleArea);

        _searchButton = DividerButton(AppIcons.Search, Strings.Get("Menu.SearchOpen"));
        _searchButton.Name = "MenuSearchButton";
        _searchButton.Click += (_, _) => SetSearchOpen(!_searchOpen);
        Grid.SetColumn(_searchButton, 3);
        title.Children.Add(_searchButton);

        _pinButton = DividerButton(AppIcons.Pin, Strings.Get("Menu.Pin"));
        _pinButton.Name = "MenuPinButton";
        _pinButton.Content = new IconView
        {
            Name = "MenuPinIcon",
            Width = 11,
            Height = 11,
            Data = AppIcons.Pin,
            Stroke = Brush("AppMuted", Colors.LightGray),
            StrokeThickness = 1.4,
        };
        _pinButton.Click += (_, _) => SetPinned(!_pinned);
        Grid.SetColumn(_pinButton, 4);
        title.Children.Add(_pinButton);

        var close = DividerButton(AppIcons.X, Strings.Get("Menu.Close"));
        close.Name = "MenuCloseButton";
        close.Click += (_, _) => CloseFromLauncher();
        Grid.SetColumn(close, 5);
        title.Children.Add(close);

        var divider = new Grid
        {
            Height = DividerHeight,
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
        };
        divider.Children.Add(_contextTitle);
        var dotRows = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        dotRows.Children.Add(_commonDots);
        dotRows.Children.Add(_contextDots);
        Grid.SetColumn(dotRows, 1);
        divider.Children.Add(dotRows);

        _addRuleButton = FooterButton(AppIcons.SelectionPlus, CurrentRuleTip());
        _addRuleButton.Name = "MenuAddWindowRule";
        _addRuleButton.IsEnabled = _canUseForeground;
        _addRuleButton.Click += async (_, _) =>
        {
            if (await _actions.AddCurrentWindowRule() is { } updated)
            {
                SetLayout(updated);
            }
        };
        var blockButton = FooterButton(AppIcons.Ban, BlockTip());
        blockButton.Name = "MenuBlockWindow";
        blockButton.IsEnabled = _canUseForeground;
        blockButton.Click += async (_, _) =>
        {
            if (await _actions.BlockCurrentWindow())
            {
                CloseFromLauncher();
            }
        };
        var footer = new Grid
        {
            Height = FooterHeight,
            Margin = new Thickness(0, FooterGap, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        footer.Children.Add(_addRuleButton);
        Grid.SetColumn(blockButton, 2);
        footer.Children.Add(blockButton);

        Content = new Border
        {
            Margin = new Thickness(1),
            Padding = new Thickness(OuterPadding, OuterTopPadding, OuterPadding, OuterPadding),
            CornerRadius = new CornerRadius(7),
            Background = Brush("AppDock", Color.FromArgb(246, 34, 34, 34)),
            BorderBrush = Brush("AppBorder", Color.FromArgb(180, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    title,
                    _searchHost,
                    _commonGrid,
                    divider,
                    _contextGrid,
                    footer,
                },
            },
        };

        RenderLayout();
        _menuMoveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background, (_, _) => CompleteMenuMove());
        _longPressTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(LongPressMilliseconds),
            DispatcherPriority.Input, (_, _) => BeginCellDrag());
        PositionChanged += (_, _) =>
        {
            if (_menuDragging)
            {
                _menuMoveTimer?.Stop();
                _menuMoveTimer?.Start();
            }
        };
        PointerReleased += OnCellPointerReleased;
        AddHandler(PointerPressedEvent, OnMenuPointerPressed, RoutingStrategies.Tunnel);
        Deactivated += (_, _) =>
        {
            if (_searchOpen)
            {
                SetSearchOpen(false);
            }
        };
        Opened += (_, _) =>
        {
            ApplyNoActivate();
            _anchor = SystemCursor();
            Position = NearCursor(_anchor);
        };
        Closed += (_, _) =>
        {
            _menuMoveTimer?.Stop();
            _longPressTimer.Stop();
            _closing = true;
            Volatile.Write(ref _searchFocused, 0);
            Finish(null);
        };
    }

    public Task<MacroItem?> Answer => _answer.Task;

    public event Action<bool>? SearchFocusChanged;

    public event Action<bool>? PopupOpenChanged;

    public int CommonPage => _commonPage;

    public int CommonPageCount => _layout.CommonPages.Count;

    public int ContextPage => _contextPage;

    public int ContextPageCount => _layout.ContextPages.Count;

    public bool IsSearchFocused => Volatile.Read(ref _searchFocused) == 1;

    public bool IsSearchOpen => _searchOpen;

    public bool IsPinned => _pinned;

    internal bool HasOpenPopup => _openPopupCount > 0;

    internal static Cursor TitleCursorForCheck => TitleCursor;

    internal static Cursor ButtonCursorForCheck => ButtonCursor;

    internal double DividerCenterOffset => FrameInset + OuterTopPadding + TitleHeight
        + SearchHeight + SearchGap
        + RegionRows * CellSize + (RegionRows - 1) * CellGap
        + DividerHeight / 2;

    public MenuRuntimeCell? SelectedCell => _selected?.Cell;

    internal IntPtr NativeHandle => TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    public void SetLayout(MenuRuntimeLayout layout)
    {
        _layout = layout;
        _commonPage = Math.Clamp(_commonPage, 0, Math.Max(0, layout.CommonPages.Count - 1));
        _contextPage = Math.Clamp(_contextPage, 0,
            Math.Max(0, layout.ContextPages.Count - 1));
        RenderLayout();
    }

    public void SetSearchOpen(bool open)
    {
        if (_closing || _searchOpen == open)
        {
            return;
        }

        _searchOpen = open;
        _searchHost.Opacity = open ? 1 : 0;
        _searchHost.IsHitTestVisible = open;
        _search.Focusable = open;
        _search.IsTabStop = open;
        // 这个窗没有 axaml，声明不了本地样式；"开"的状态一直是由下面这行 `Background` 画的。
        // 原来还有一行给按钮贴了个"开"的类名，它从来没配到过任何样式，已删。
        // （注释里不写那个调用的原文：扫描按文本找类名，写在注释里会被当成真的加类。）
        _searchButton.Background = open
            ? Brush("AppHover", Color.FromArgb(80, 255, 255, 255))
            : Brushes.Transparent;
        ToolTip.SetTip(_searchButton, Strings.Get(open ? "Menu.SearchClose" : "Menu.SearchOpen"));

        if (open)
        {
            BeginSearchFocus();
            Dispatcher.UIThread.Post(() => _search.Focus(), DispatcherPriority.Input);
        }
        else
        {
            _search.Text = string.Empty;
            EndSearchFocus();
        }
    }

    public void SetPinned(bool pinned)
    {
        if (_closing || _pinned == pinned)
        {
            return;
        }

        _pinned = pinned;
        _pinButton.Background = pinned
            ? Brush("AppHover", Color.FromArgb(80, 255, 255, 255))
            : Brushes.Transparent;
        ToolTip.SetTip(_pinButton, Strings.Get(pinned ? "Menu.Unpin" : "Menu.Pin"));
        if (_pinButton.Content is IconView icon)
        {
            icon.Data = pinned ? AppIcons.PinOff : AppIcons.Pin;
        }
    }

    public void CloseFromLauncher()
    {
        if (IsSearchFocused)
        {
            _actions.RestoreForeground();
        }
        CloseMenu();
    }

    public void CloseFromOutside()
    {
        if (_pinned)
        {
            return;
        }

        CloseMenu();
    }

    public void HandleKey(string key)
    {
        switch (key)
        {
            case "Esc" or "Escape":
                if (IsSearchFocused)
                {
                    if (_search.Text?.Length > 0)
                    {
                        _search.Text = string.Empty;
                    }
                    else if (_searchOpen)
                    {
                        SetSearchOpen(false);
                    }
                    else
                    {
                        CloseMenu();
                    }
                }
                else if (!_pinned)
                {
                    CloseMenu();
                }
                break;
            case "Left" or "左":
                MoveSelection(0, -1);
                break;
            case "Right" or "右":
                MoveSelection(0, 1);
                break;
            case "Up" or "上":
                MoveSelection(-1, 0);
                break;
            case "Down" or "下":
                MoveSelection(1, 0);
                break;
            case "Enter" or "Return" or "回车":
                if (_selected is { } selected)
                {
                    _ = ActivateSlotAsync(selected);
                }
                break;
        }
    }

    internal static bool FillsCell(PixelSize size)
        => size.Width > 0 && size.Width == size.Height;

    internal static TimeSpan CellDragDelay => TimeSpan.FromMilliseconds(LongPressMilliseconds);

    internal void BeginCellDragForCheck(MenuSlotRef slot, Button button)
    {
        _dragSource = slot;
        _dragButton = button;
        BeginCellDrag();
    }

    internal Task DropCellForCheckAsync(Point point) => DropCellAsync(point);

    private static Grid GridFor()
    {
        var grid = new Grid
        {
            ColumnSpacing = CellGap,
            RowSpacing = CellGap,
            Width = Columns * CellSize + (Columns - 1) * CellGap,
            Height = RegionRows * CellSize + (RegionRows - 1) * CellGap,
        };
        for (var column = 0; column < Columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(CellSize),
            });
        }
        for (var row = 0; row < RegionRows; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(CellSize),
            });
        }
        return grid;
    }

    private void RenderLayout()
    {
        _commonPage = Math.Clamp(_commonPage, 0, Math.Max(0, _layout.CommonPages.Count - 1));
        _contextPage = Math.Clamp(_contextPage, 0,
            Math.Max(0, _layout.ContextPages.Count - 1));
        FillGrid(_commonGrid, _layout.CommonPages[_commonPage]);
        FillGrid(_contextGrid, _layout.ContextPages[_contextPage]);
        _contextTitle.Text = _layout.ContextPages[_contextPage].RuleSummary;
        RenderDots();
    }

    private void OnSearchChanged()
    {
        var wanted = _search.Text?.Trim() ?? string.Empty;
        _selected = null;
        if (wanted.Length > 0)
        {
            for (var index = 0; index < _layout.CommonPages.Count; index++)
            {
                if (_layout.CommonPages[index].Cells.Any(MatchesSearch))
                {
                    _commonPage = index;
                    break;
                }
            }
        }
        RenderLayout();
    }

    private bool MatchesSearch(MenuRuntimeCell cell)
    {
        var wanted = _search.Text?.Trim() ?? string.Empty;
        return wanted.Length == 0
               || cell.Macro.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)
               || cell.Macro.Description.Contains(wanted, StringComparison.OrdinalIgnoreCase);
    }

    private void BeginSearchFocus()
    {
        if (_closing || Volatile.Read(ref _searchFocused) == 1)
        {
            return;
        }

        Volatile.Write(ref _searchFocused, 1);
        SearchFocusChanged?.Invoke(true);
        ShowActivated = true;
        SetNoActivate(false);
        Activate();
    }

    private void EndSearchFocus()
    {
        if (_closing || Interlocked.Exchange(ref _searchFocused, 0) == 0)
        {
            return;
        }

        var restore = IsActive;
        ShowActivated = false;
        ApplyNoActivate();
        SearchFocusChanged?.Invoke(false);
        if (restore && !_menuDragging && !_pinned)
        {
            Dispatcher.UIThread.Post(_actions.RestoreForeground);
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Up:
                if (_selected is null)
                {
                    _selected = FirstVisibleMatch();
                }
                else
                {
                    MoveSelection(e.Key == Key.Down ? 1 : -1, 0);
                }
                e.Handled = true;
                break;
            case Key.Enter:
                _selected ??= FirstVisibleMatch();
                if (_selected is { } selected)
                {
                    _ = ActivateSlotAsync(selected);
                }
                e.Handled = true;
                break;
            case Key.Escape:
                HandleKey("Esc");
                e.Handled = true;
                break;
        }
    }

    private MenuSlotRef? FirstVisibleMatch()
    {
        var common = _layout.CommonPages[_commonPage];
        var context = _layout.ContextPages[_contextPage];
        var cell = common.Cells.Concat(context.Cells)
            .Where(MatchesSearch)
            .OrderBy(item => item.Row)
            .ThenBy(item => item.Column)
            .FirstOrDefault();
        if (cell is null)
        {
            return null;
        }

        return new MenuSlotRef(
            common.Cells.Contains(cell) ? common.PageId : context.PageId,
            cell.Row, cell.Column, cell);
    }

    private void FillGrid(Grid grid, MenuRuntimePage page)
    {
        grid.Children.Clear();
        for (var row = 0; row < RegionRows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var cell = page.Cells.FirstOrDefault(candidate =>
                    candidate.Row == row && candidate.Column == column);
                var slot = new MenuSlotRef(page.PageId, row, column, cell);
                var child = cell is null ? EmptyCell(slot) : CellButton(slot, cell);
                var matches = cell is null || MatchesSearch(cell);
                child.Opacity = matches ? 1 : 0.25;
                child.IsHitTestVisible = matches
                    && (cell is not null || string.IsNullOrWhiteSpace(_search.Text));
                if (_selected?.Key == slot.Key)
                {
                    child.BorderBrush = Brush("AppAccent", Color.FromRgb(78, 164, 232));
                    child.BorderThickness = new Thickness(2);
                }
                Grid.SetRow(child, row);
                Grid.SetColumn(child, column);
                grid.Children.Add(child);
            }
        }
    }

    private Button EmptyCell(MenuSlotRef slot)
    {
        var button = BaseCellButton();
        button.Tag = slot;
        button.Content = new IconView
        {
            Width = 24,
            Height = 24,
            Data = AppIcons.SelectionPlus,
            Stroke = Brush("AppMuted", Colors.LightGray),
            StrokeThickness = 1.4,
        };
        ToolTip.SetTip(button, Strings.Get("Menu.AddCell"));
        button.Click += async (_, _) => await ChooseSlotAsync(slot);
        WireDrag(button, slot);
        AttachHover(button, slot);
        return button;
    }

    private Button CellButton(MenuSlotRef slot, MenuRuntimeCell cell)
    {
        var button = BaseCellButton();
        button.Tag = slot;
        button.Content = CellContent(cell);
        var description = cell.Macro.Description.Trim();
        ToolTip.SetTip(button, description.Length == 0
            ? cell.Macro.Name
            : $"{cell.Macro.Name}\n{description}");
        AttachContextMenu(button, CellMenu(slot));
        button.Click += async (_, _) =>
        {
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }

            await ActivateSlotAsync(slot);
        };
        WireDrag(button, slot);
        AttachHover(button, slot);
        return button;
    }

    private static Control CellContent(MenuRuntimeCell cell)
    {
        if (cell.Icon is { IsEmpty: false } icon && TryIcon(icon, out var iconView))
        {
            return iconView;
        }

        return new TextBlock
        {
            Text = cell.Macro.Name,
            FontSize = 10.5,
            Foreground = Brush("AppText", Colors.White),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            MaxHeight = 42,
        };
    }

    private static bool TryIcon(MenuIconReference icon, out Control control)
    {
        if (icon.Kind == MenuIconKind.Builtin
            && MenuIconCatalog.Find(icon.Value) is { } option)
        {
            control = new IconView
            {
                Width = 44,
                Height = 44,
                Data = option.Kind,
                Stroke = Brush("AppText", Colors.White),
                StrokeThickness = 1.5,
            };
            return true;
        }

        if (icon.Kind == MenuIconKind.Image && File.Exists(icon.Value))
        {
            try
            {
                var bitmap = new Bitmap(icon.Value);
                var fill = FillsCell(bitmap.PixelSize);
                control = new Border
                {
                    Width = fill ? 74 : 48,
                    Height = fill ? 74 : 48,
                    CornerRadius = new CornerRadius(3),
                    ClipToBounds = true,
                    Child = new Image
                    {
                        Width = fill ? 74 : 48,
                        Height = fill ? 74 : 48,
                        Stretch = fill ? Stretch.UniformToFill : Stretch.Uniform,
                        Source = bitmap,
                    },
                };
                return true;
            }
            catch
            {
                // A moved or unreadable image falls back to the macro name.
            }
        }

        control = null!;
        return false;
    }

    private static Button BaseCellButton()
    {
        var button = new Button
        {
            Width = CellSize,
            Height = CellSize,
            Margin = new Thickness(1),
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Background = Brush("AppField", Color.FromRgb(48, 48, 48)),
            BorderBrush = Brush("AppBorder", Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = ButtonCursor,
        };

        // 这个类没有样式，但它**有用**：六个菜单测试按 `Classes.Contains("MenuCell")` 找格子。
        // 删它会把测试变成"找不到元素"——**"没有样式"不等于"没人用"**，测试也是消费者。
        button.Classes.Add("MenuCell");
        return button;
    }

    private void OnMenuPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_searchOpen
            || !(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                || e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            || IsInsideSearchControl(e.Source))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_searchOpen)
            {
                SetSearchOpen(false);
            }
        }, DispatcherPriority.Background);
    }

    private bool IsInsideSearchControl(object? source)
    {
        for (var control = source as Control; control is not null; control = control.Parent as Control)
        {
            if (ReferenceEquals(control, _searchHost)
                || ReferenceEquals(control, _searchButton))
            {
                return true;
            }
        }
        return false;
    }

    private ContextMenu CellMenu(MenuSlotRef slot)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Menu.EditMacro", () =>
        {
            CloseForAction();
            _actions.OpenMacroEditor(slot.Cell!.Macro);
        }));
        menu.Items.Add(Item("Menu.Replace", async () =>
        {
            if (await _actions.ChooseMacro(slot) is { } updated)
            {
                SetLayout(updated);
            }
        }));
        menu.Items.Add(Item("Menu.ChooseIcon", async () =>
        {
            if (await _actions.ChooseIcon(slot) is { } updated)
            {
                SetLayout(updated);
            }
        }));
        menu.Items.Add(Item("Menu.Debug", () =>
        {
            CloseForAction();
            _actions.DebugMacro(slot.Cell!.Macro);
        }));
        return menu;
    }

    private void AttachContextMenu(Control control, ContextMenu menu)
    {
        control.ContextMenu = menu;
        menu.Opened += (_, _) =>
        {
            if (Interlocked.Increment(ref _openPopupCount) == 1)
            {
                PopupOpenChanged?.Invoke(true);
            }
        };
        menu.Closed += (_, _) =>
        {
            if (Interlocked.Decrement(ref _openPopupCount) <= 0)
            {
                Interlocked.Exchange(ref _openPopupCount, 0);
                PopupOpenChanged?.Invoke(false);
            }
        };
        control.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(control).Properties.IsRightButtonPressed || menu.IsOpen)
            {
                return;
            }

            e.Handled = true;
            menu.Open(control);
        };
    }

    private static MenuItem Item(string key, Action action)
    {
        var item = new MenuItem { Header = Strings.Get(key) };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Item(string key, Func<Task> action)
    {
        var item = new MenuItem { Header = Strings.Get(key) };
        item.Click += async (_, _) => await action();
        return item;
    }

    private async Task ActivateSlotAsync(MenuSlotRef slot)
    {
        _selected = slot;
        if (slot.Cell is null)
        {
            await ChooseSlotAsync(slot);
            RenderLayout();
            return;
        }

        if (IsSearchFocused)
        {
            _actions.RestoreForeground();
        }
        _closing = true;
        Finish(slot.Cell.Macro);
        Close();
    }

    private async Task ChooseSlotAsync(MenuSlotRef slot)
    {
        if (await _actions.ChooseMacro(slot) is { } layout)
        {
            SetLayout(layout);
        }
    }

    private void WireDrag(Button button, MenuSlotRef slot)
    {
        button.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _dragSource = slot;
            _dragButton = button;
            _dragPointer = e.Pointer;
            _dragging = false;
            _longPressTimer.Stop();
            _longPressTimer.Start();
        };
        button.PointerReleased += (_, _) => _longPressTimer.Stop();
    }

    private void BeginCellDrag()
    {
        _longPressTimer.Stop();
        if (_dragSource is null || _dragButton is null)
        {
            return;
        }

        _dragging = true;
        _suppressClick = true;
        _dragPointer?.Capture(this);
        _dragButton.Opacity = 0.65;
        _dragButton.BorderBrush = Brush("AppAccent", Color.FromRgb(78, 164, 232));
        _dragButton.BorderThickness = new Thickness(2);
    }

    private void FinishDrag()
    {
        _longPressTimer.Stop();
        if (_dragButton is not null)
        {
            _dragButton.Opacity = 1;
        }
        _dragSource = null;
        _dragButton = null;
        _dragPointer = null;
        _dragging = false;
    }

    private async void OnCellPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        await DropCellAsync(e.GetPosition(this));
    }

    private async Task DropCellAsync(Point point)
    {
        if (!_dragging || _dragSource is not { } source)
        {
            return;
        }

        _longPressTimer.Stop();
        _suppressClick = true;
        _dragPointer?.Capture(null);
        _dragSource = null;
        _dragButton = null;
        _dragPointer = null;
        _dragging = false;
        if (SlotAtPoint(point) is { } target
            && target.Key != source.Key
            && await _actions.Move(source, target) is { } layout)
        {
            SetLayout(layout);
        }
        Dispatcher.UIThread.Post(() => _suppressClick = false);
    }

    private MenuSlotRef? SlotAtPoint(Point point)
    {
        if (this.TranslatePoint(point, _commonGrid) is { } common
            && common.X >= 0 && common.Y >= 0
            && common.X < _commonGrid.Bounds.Width && common.Y < _commonGrid.Bounds.Height)
        {
            return SlotAt(common, _layout.CommonPages[_commonPage]);
        }

        if (this.TranslatePoint(point, _contextGrid) is { } context
            && context.X >= 0 && context.Y >= 0
            && context.X < _contextGrid.Bounds.Width && context.Y < _contextGrid.Bounds.Height)
        {
            return SlotAt(context, _layout.ContextPages[_contextPage]);
        }

        return null;
    }

    private static MenuSlotRef SlotAt(Point point, MenuRuntimePage page)
    {
        var column = Math.Clamp((int)(point.X / (CellSize + CellGap)), 0, (int)Columns - 1);
        var row = Math.Clamp((int)(point.Y / (CellSize + CellGap)), 0, (int)RegionRows - 1);
        var cell = page.Cells.FirstOrDefault(candidate =>
            candidate.Row == row && candidate.Column == column);
        return new MenuSlotRef(page.PageId, row, column, cell);
    }

    private void MoveSelection(int rowStep, int columnStep)
    {
        var common = _layout.CommonPages[_commonPage];
        var context = _layout.ContextPages[_contextPage];
        var commonCells = common.Cells.Where(MatchesSearch).ToList();
        var contextCells = context.Cells.Where(MatchesSearch).ToList();
        var page = _selected?.PageId == context.PageId
            || commonCells.Count == 0
            ? context
            : common;
        var cells = page.PageId == context.PageId ? contextCells : commonCells;
        if (cells.Count == 0)
        {
            return;
        }

        if (_selected is null)
        {
            var first = cells[0];
            _selected = new MenuSlotRef(page.PageId, first.Row, first.Column, first);
            RenderLayout();
            return;
        }

        var current = _selected.Cell is { } selected && cells.Contains(selected)
            ? selected
            : cells[0];
        var row = current.Row + rowStep;
        var column = current.Column + columnStep;
        while (row >= 0 && row < RegionRows && column >= 0 && column < Columns)
        {
            if (cells.FirstOrDefault(cell => cell.Row == row && cell.Column == column)
                is { } next)
            {
                _selected = new MenuSlotRef(page.PageId, next.Row, next.Column, next);
                RenderLayout();
                return;
            }

            row += rowStep;
            column += columnStep;
        }
    }

    private void RenderDots()
    {
        FillDots(_commonDots, _layout.CommonPages.Count, _commonPage, common: true);
        FillDots(_contextDots, _layout.ContextPages.Count, _contextPage, common: false);
    }

    private void FillDots(StackPanel target, int count, int current, bool common)
    {
        target.Children.Clear();
        for (var index = 0; index < count; index++)
        {
            var button = new Button
            {
                Name = $"{(common ? "Common" : "Context")}PageButton{index + 1}",
                Width = 12,
                Height = 16,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = ButtonCursor,
                Content = new Ellipse
                {
                    Width = index == current ? 6 : 4,
                    Height = index == current ? 6 : 4,
                    Fill = index == current
                        ? Brush("AppAccent", Color.FromRgb(78, 164, 232))
                        : Brush("AppMuted", Color.FromRgb(120, 120, 120)),
                },
            };
            var page = index;
            button.Click += (_, _) =>
            {
                if (common)
                {
                    _commonPage = page;
                }
                else
                {
                    _contextPage = page;
                }
                _selected = null;
                RenderLayout();
            };
            if (index > 0 && count > 1)
            {
                var remove = new MenuItem { Header = Strings.Get("Menu.RemovePage") };
                remove.Click += async (_, _) =>
                {
                    var pageId = common
                        ? _layout.CommonPages[page].PageId
                        : _layout.ContextPages[page].PageId;
                    if (await (common
                            ? _actions.RemoveCommonPage(pageId)
                            : _actions.RemoveContextPage(pageId))
                        is { } updated)
                    {
                        SetLayout(updated);
                    }
                };
                AttachContextMenu(button, new ContextMenu { Items = { remove } });
            }
            target.Children.Add(button);
        }

        if (count < ProjectTriggers.MaxCommonMenuPages)
        {
            var add = DividerButton(AppIcons.Plus, Strings.Get("Menu.AddPage"));
            add.Name = common ? "AddCommonPageButton" : "AddContextPageButton";
            add.Click += async (_, _) =>
            {
                var added = common
                    ? await _actions.AddCommonPage()
                    : await _actions.AddContextPage(_layout.ContextPages[_contextPage].PageId);
                if (added is { } updated)
                {
                    SetLayout(updated);
                    if (common)
                    {
                        _commonPage = updated.CommonPages.Count - 1;
                    }
                    else
                    {
                        _contextPage = updated.ContextPages.Count - 1;
                    }
                    RenderLayout();
                }
            };
            target.Children.Add(add);
        }
    }

    private void AttachHover(Button button, MenuSlotRef slot)
    {
        var scale = new ScaleTransform();
        button.RenderTransform = scale;
        button.RenderTransformOrigin = RelativePoint.Center;
        button.Transitions =
        [
            new DoubleTransition
            {
                Property = ScaleTransform.ScaleXProperty,
                Duration = TimeSpan.FromMilliseconds(90),
            },
            new DoubleTransition
            {
                Property = ScaleTransform.ScaleYProperty,
                Duration = TimeSpan.FromMilliseconds(90),
            },
        ];
        button.PointerEntered += (_, _) =>
        {
            scale.ScaleX = 1.04;
            scale.ScaleY = 1.04;
            button.Background = Brush("AppHover", Color.FromRgb(64, 64, 64));
            button.BorderBrush = Brush("AppAccent", Color.FromRgb(78, 164, 232));
            button.BorderThickness = new Thickness(2);
        };
        button.PointerExited += (_, _) =>
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            button.Background = Brush("AppField", Color.FromRgb(48, 48, 48));
            var selected = _selected?.Key == slot.Key;
            button.BorderBrush = Brush(selected ? "AppAccent" : "AppBorder",
                selected ? Color.FromRgb(78, 164, 232) : Color.FromRgb(80, 80, 80));
            button.BorderThickness = new Thickness(selected ? 2 : 1);
        };
    }

    private void CompleteMenuMove()
    {
        if (!_menuDragging)
        {
            _menuMoveTimer?.Stop();
            return;
        }

        if (OperatingSystem.IsWindows() && (GetAsyncKeyState(0x01) & 0x8000) != 0)
        {
            return;
        }

        _menuMoveTimer?.Stop();
        _menuDragging = false;
        if (_restoreAfterMenuDrag)
        {
            _restoreAfterMenuDrag = false;
            ShowActivated = false;
            ApplyNoActivate();
            _actions.RestoreForeground();
        }
    }

    private static Button DividerButton(LucideIconKind icon, string tip)
    {
        var button = new Button
        {
            Width = 24,
            Height = 18,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = ButtonCursor,
            Content = new IconView
            {
                Width = 11,
                Height = 11,
                Data = icon,
                Stroke = Brush("AppMuted", Colors.LightGray),
                StrokeThickness = 1.4,
            },
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Button FooterButton(LucideIconKind icon, string tip)
    {
        var button = new Button
        {
            Height = FooterHeight,
            Padding = new Thickness(8, 0),
            Background = Brush("AppField", Color.FromArgb(150, 48, 48, 48)),
            BorderBrush = Brush("AppBorder", Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Cursor = ButtonCursor,
            Content = new IconView
            {
                Width = 14,
                Height = 14,
                Data = icon,
                Stroke = Brush("AppText", Colors.White),
                StrokeThickness = 1.5,
            },
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private string CurrentRuleTip()
        => _canUseForeground
            ? Strings.Format("Menu.AddWindowRule", _foregroundProcess)
            : Strings.Get("Menu.NoForeground");

    private string BlockTip()
        => _canUseForeground
            ? Strings.Format("Menu.BlockWindow", _foregroundProcess)
            : Strings.Get("Menu.NoForeground");

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var commonPoint = e.GetPosition(_commonGrid);
        var contextPoint = e.GetPosition(_contextGrid);
        var overCommon = commonPoint.X >= 0 && commonPoint.Y >= 0
            && commonPoint.X <= _commonGrid.Bounds.Width
            && commonPoint.Y <= _commonGrid.Bounds.Height;
        var overContext = contextPoint.X >= 0 && contextPoint.Y >= 0
            && contextPoint.X <= _contextGrid.Bounds.Width
            && contextPoint.Y <= _contextGrid.Bounds.Height;
        if (!overCommon && !overContext)
        {
            return;
        }

        if (e.Delta.Y > 0)
        {
            if (overCommon)
            {
                _commonPage = Math.Max(0, _commonPage - 1);
            }
            else
            {
                _contextPage = Math.Max(0, _contextPage - 1);
            }
        }
        else if (e.Delta.Y < 0)
        {
            if (overCommon)
            {
                _commonPage = Math.Min(_layout.CommonPages.Count - 1, _commonPage + 1);
            }
            else
            {
                _contextPage = Math.Min(_layout.ContextPages.Count - 1, _contextPage + 1);
            }
        }
        _selected = null;
        RenderLayout();
        e.Handled = true;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyNoActivate();
    }

    private void ApplyNoActivate() => SetNoActivate(true);

    private void SetNoActivate(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        var next = enabled
            ? style | WsExNoActivate | WsExToolWindow
            : style & ~WsExNoActivate | WsExToolWindow;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(next));
    }

    private void CloseForAction()
    {
        if (IsSearchFocused)
        {
            _actions.RestoreForeground();
        }
        CloseMenu();
    }

    private void CloseMenu()
    {
        _closing = true;
        Finish(null);
        Close();
    }

    private void Finish(MacroItem? macro)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _answer.TrySetResult(macro);
    }

    private PixelPoint NearCursor()
    {
        PixelPoint cursor;
        try
        {
            cursor = SystemCursor();
        }
        catch
        {
            cursor = Screens.Primary?.Bounds.Center ?? default;
        }

        return NearCursor(cursor);
    }

    internal PixelPoint NearCursor(PixelPoint cursor)
    {
        var screen = Screens.ScreenFromPoint(cursor) ?? Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0,
            Math.Max(1, (int)Width), Math.Max(1, (int)Height));
        return PlaceNear(cursor, area, screen?.Scaling ?? RenderScaling);
    }

    internal PixelPoint PlaceNear(PixelPoint cursor, PixelRect workArea, double scale)
    {
        var width = Math.Max(1, (int)Math.Ceiling(Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(Height * scale));
        var x = cursor.X + 12;
        if (x + width > workArea.Right)
        {
            x = cursor.X - width - 12;
        }
        var y = cursor.Y - (int)Math.Round(DividerCenterOffset * scale);

        x = Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.Right - width));
        y = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height));
        return new PixelPoint(x, y);
    }

    private static PixelPoint SystemCursor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return default;
        }

        GetCursorPos(out var point);
        return new PixelPoint(point.X, point.Y);
    }

    private static IBrush Brush(string key, Color fallback)
        => Application.Current?.TryFindResource(key, out var found) == true && found is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    private static bool IsInsideButton(object? source)
    {
        for (var control = source as Control; control is not null; control = control.Parent as Control)
        {
            if (control is Button)
            {
                return true;
            }
        }
        return false;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
