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

// Building the window: the title, the search box, the grid, the footer and the menu it opens with.
public sealed partial class MacroMenuWindow
{
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
            Foreground = ThemeBrush.Of("TextTertiary"),
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
        };
        // 标题栏底：**绑令牌**，不是取一次快照——换主题要跟着变。
        ThemeBrush.Assign(title, BackgroundProperty, "SurfaceChrome");
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
            Foreground = ThemeBrush.Of("TextPrimary"),
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
            Stroke = ThemeBrush.Of("TextTertiary"),
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
            Background = ThemeBrush.Of("SurfacePanel") ?? Brushes.Transparent,
            BorderBrush = ThemeBrush.Of("BorderControl"),
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

    /// <summary>
    /// Raised when the pin changes. The hook thread answers the arrow keys from a copied flag, so
    /// it never reads this window; this is the one place the copy is moved.
    /// </summary>
    public event Action<bool>? PinnedChanged;

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
}
