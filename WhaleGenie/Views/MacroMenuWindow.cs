using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Lucide.Avalonia;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Views;

public enum MenuEditCommand
{
    Replace,
    EditLabel,
    ChooseIcon,
    ClearIcon,
    ClearCell,
}

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
    Action<MacroItem> Run,
    Action OpenSettings,
    Action RestoreForeground,
    Func<Task<MenuRuntimeLayout?>> AddCommonPage,
    Func<string, Task<MenuRuntimeLayout?>> RemoveCommonPage,
    Func<string, Task<MenuRuntimeLayout?>> AddContextPage,
    Func<string, Task<MenuRuntimeLayout?>> RemoveContextPage,
    Func<MenuSlotRef, Task<MenuRuntimeLayout?>> ChooseMacro,
    Func<MenuSlotRef, MenuEditCommand, Task<MenuRuntimeLayout?>> Edit,
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
    private const double TitleHeight = 32;
    private const double SearchHeight = 34;
    private const double SearchGap = 6;
    private const double DividerHeight = 44;
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;

    private readonly MenuWindowActions _actions;
    private readonly Grid _commonGrid;
    private readonly Grid _contextGrid;
    private readonly StackPanel _commonDots;
    private readonly StackPanel _contextDots;
    private readonly TextBlock _contextTitle;
    private readonly TextBox _search;
    private readonly Button _editButton;
    private DispatcherTimer? _menuMoveTimer;
    private readonly TaskCompletionSource<MacroItem?> _answer =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MenuRuntimeLayout _layout;
    private int _commonPage;
    private int _contextPage;
    private MenuSlotRef? _selected;
    private MenuSlotRef? _dragSource;
    private Point _dragOrigin;
    private bool _dragging;
    private bool _suppressClick;
    private bool _done;
    private bool _editing;
    private bool _closing;
    private int _searchFocused;
    private bool _menuDragging;
    private bool _restoreAfterMenuDrag;

    public MacroMenuWindow(MenuRuntimeLayout layout, MenuWindowActions actions)
    {
        _layout = layout;
        _actions = actions;

        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        Width = Columns * CellSize + (Columns - 1) * CellGap + OuterPadding * 2 + 2;
        Height = TitleHeight + SearchHeight + SearchGap + RegionRows * CellSize * 2
                 + (RegionRows - 1) * CellGap * 2 + DividerHeight + OuterPadding * 2 + 2;

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
            Margin = new Thickness(0, 0, 0, SearchGap),
            PlaceholderText = Strings.Get("Menu.Search"),
        };
        _search.TextChanged += (_, _) => OnSearchChanged();
        _search.GotFocus += (_, _) => BeginSearchFocus();
        _search.LostFocus += (_, _) => EndSearchFocus();
        _search.KeyDown += OnSearchKeyDown;
        _search.PointerPressed += (_, _) => BeginSearchFocus();
        var title = new Grid
        {
            Height = TitleHeight,
            ColumnDefinitions = new ColumnDefinitions("28,*"),
            Margin = new Thickness(0, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Background = Brush("AppChrome", Color.FromArgb(120, 20, 20, 20)),
        };
        ToolTip.SetTip(title, Strings.Get("Menu.Drag"));
        title.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(title).Properties.IsLeftButtonPressed)
            {
                _menuDragging = true;
                _restoreAfterMenuDrag = IsSearchFocused;
                BeginMoveDrag(e);
                _menuMoveTimer?.Stop();
                _menuMoveTimer?.Start();
            }
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
        var appName = new TextBlock
        {
            Text = Strings.Get("Main.Title"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("AppText", Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(appName, 1);
        title.Children.Add(appName);
        _editButton = DividerButton(AppIcons.Pencil, Strings.Get("Menu.Edit"));
        _editButton.Click += (_, _) => SetEditing(!_editing);
        var settings = DividerButton(AppIcons.Settings, Strings.Get("Menu.Settings"));
        settings.Click += (_, _) =>
        {
            _closing = true;
            Finish(null);
            Close();
            _actions.OpenSettings();
        };

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
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
        };
        buttons.Children.Add(_editButton);
        buttons.Children.Add(settings);
        Grid.SetColumn(buttons, 2);
        divider.Children.Add(buttons);

        Content = new Border
        {
            Margin = new Thickness(1),
            Padding = new Thickness(OuterPadding),
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
                    _search,
                    _commonGrid,
                    divider,
                    _contextGrid,
                },
            },
        };

        RenderLayout();
        _menuMoveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background, (_, _) => CompleteMenuMove());
        PositionChanged += (_, _) =>
        {
            if (_menuDragging)
            {
                _menuMoveTimer?.Stop();
                _menuMoveTimer?.Start();
            }
        };
        Opened += (_, _) =>
        {
            ApplyNoActivate();
            Position = NearCursor();
        };
        Closed += (_, _) =>
        {
            _menuMoveTimer?.Stop();
            _closing = true;
            Volatile.Write(ref _searchFocused, 0);
            Finish(null);
        };
    }

    public Task<MacroItem?> Answer => _answer.Task;

    public event Action<bool>? SearchFocusChanged;

    public bool IsEditing => _editing;

    public int CommonPage => _commonPage;

    public int CommonPageCount => _layout.CommonPages.Count;

    public int ContextPage => _contextPage;

    public int ContextPageCount => _layout.ContextPages.Count;

    public bool IsSearchFocused => Volatile.Read(ref _searchFocused) == 1;

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

    public void SetEditing(bool editing)
    {
        _editing = editing;
        _selected = null;
        _editButton.Classes.Set("On", editing);
        ToolTip.SetTip(_editButton, Strings.Get(editing ? "Menu.EditDone" : "Menu.Edit"));
        if (editing)
        {
            ShowActivated = true;
            SetNoActivate(false);
            Activate();
        }
        else
        {
            ShowActivated = false;
            ApplyNoActivate();
            _actions.RestoreForeground();
        }

        RenderLayout();
    }

    public void CloseFromLauncher()
    {
        if (_editing || IsSearchFocused)
        {
            _actions.RestoreForeground();
        }
        _closing = true;
        Finish(null);
        Close();
    }

    public void CloseFromOutside()
    {
        _closing = true;
        Finish(null);
        Close();
    }

    public void HandleKey(string key)
    {
        switch (key)
        {
            case "Esc" or "Escape":
                if (_editing)
                {
                    SetEditing(false);
                }
                else
                {
                    if (IsSearchFocused)
                    {
                        _actions.RestoreForeground();
                    }
                    _closing = true;
                    Finish(null);
                    Close();
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
               || cell.Label.Contains(wanted, StringComparison.OrdinalIgnoreCase)
               || cell.Macro.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase);
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
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _search.Focus());
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
        if (restore && !_menuDragging)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(_actions.RestoreForeground);
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
                if (_search.Text?.Length > 0)
                {
                    _search.Text = string.Empty;
                }
                else
                {
                    _closing = true;
                    Finish(null);
                    Close();
                }
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
                child.IsHitTestVisible = matches && (_editing || cell is not null);
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
        button.IsHitTestVisible = _editing;
        if (!_editing)
        {
            button.BorderBrush = Brush("AppBorder", Color.FromArgb(45, 255, 255, 255));
            return button;
        }

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
        return button;
    }

    private Button CellButton(MenuSlotRef slot, MenuRuntimeCell cell)
    {
        var button = BaseCellButton();
        button.Tag = slot;
        button.Content = CellContent(cell);
        ToolTip.SetTip(button, $"{cell.Label}\n{cell.Macro.Name}");
        button.ContextMenu = CellMenu(slot);
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
        return button;
    }

    private Control CellContent(MenuRuntimeCell cell)
    {
        if (cell.Icon is { IsEmpty: false } icon && TryIcon(icon, out var iconView))
        {
            return iconView;
        }

        return new TextBlock
        {
            Text = cell.Label,
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
                Data = option.Icon,
                Stroke = Brush("AppText", Colors.White),
                StrokeThickness = 1.5,
            };
            return true;
        }

        if (icon.Kind == MenuIconKind.Image && File.Exists(icon.Value))
        {
            try
            {
                control = new Image
                {
                    Width = 48,
                    Height = 48,
                    Stretch = Stretch.Uniform,
                    Source = new Bitmap(icon.Value),
                };
                return true;
            }
            catch
            {
                // A moved or unreadable image falls back to the label.
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
        };
        button.Classes.Add("MenuCell");
        return button;
    }

    private ContextMenu CellMenu(MenuSlotRef slot)
    {
        var menu = new ContextMenu();
        menu.Items.Add(EditItem("Menu.Replace", MenuEditCommand.Replace, slot));
        menu.Items.Add(EditItem("Menu.EditLabel", MenuEditCommand.EditLabel, slot));
        menu.Items.Add(EditItem("Menu.ChooseIcon", MenuEditCommand.ChooseIcon, slot));
        menu.Items.Add(EditItem("Menu.ClearIcon", MenuEditCommand.ClearIcon, slot));
        menu.Items.Add(new Separator());
        menu.Items.Add(EditItem("Menu.ClearCell", MenuEditCommand.ClearCell, slot));
        menu.Opening += (_, e) =>
        {
            if (!_editing)
            {
                e.Cancel = true;
            }
        };
        return menu;
    }

    private MenuItem EditItem(string key, MenuEditCommand command, MenuSlotRef slot)
    {
        var item = new MenuItem { Header = Strings.Get(key) };
        item.Click += async (_, _) =>
        {
            if (await _actions.Edit(slot, command) is { } layout)
            {
                SetLayout(layout);
            }
        };
        return item;
    }

    private async Task ActivateSlotAsync(MenuSlotRef slot)
    {
        _selected = slot;
        if (_editing)
        {
            if (slot.Cell is null)
            {
                await ChooseSlotAsync(slot);
            }
            RenderLayout();
            return;
        }

        if (slot.Cell is not { } cell)
        {
            return;
        }

        if (_editing || IsSearchFocused)
        {
            _actions.RestoreForeground();
        }
        _closing = true;
        Finish(cell.Macro);
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
            if (!_editing || !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _dragSource = slot;
            _dragOrigin = e.GetPosition(this);
            _dragging = false;
        };
        button.PointerMoved += (_, e) =>
        {
            if (_dragSource is not null
                && e.GetCurrentPoint(button).Properties.IsLeftButtonPressed
                && Distance(e.GetPosition(this), _dragOrigin) > 5)
            {
                _dragging = true;
                button.Opacity = 0.55;
            }
        };
        button.PointerReleased += async (_, e) =>
        {
            var source = _dragSource;
            var dragged = _dragging;
            _dragSource = null;
            _dragging = false;
            button.Opacity = 1;
            if (!dragged || source is null)
            {
                return;
            }

            _suppressClick = true;
            if (SlotAtPoint(e.GetPosition(this)) is { } target
                && target.Key != source.Key
                && await _actions.Move(source, target) is { } layout)
            {
                SetLayout(layout);
            }
        };
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

    private static double Distance(Point one, Point other)
    {
        var dx = one.X - other.X;
        var dy = one.Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
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

        var current = _selected?.Cell is { } selected
                      && cells.Contains(selected)
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
                Width = 12,
                Height = 16,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
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
            if (_editing && count > 1)
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
                button.ContextMenu = new ContextMenu { Items = { remove } };
            }
            target.Children.Add(button);
        }

        if (_editing && count < ProjectTriggers.MaxCommonMenuPages)
        {
            var add = DividerButton(AppIcons.Plus, Strings.Get("Menu.AddPage"));
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
        var screen = Screens.Primary;
        var cursor = screen?.Bounds.Center ?? new PixelPoint(0, 0);
        try
        {
            screen = Screens.ScreenFromPoint(SystemCursor()) ?? screen;
            cursor = screen?.Bounds.Center ?? cursor;
        }
        catch
        {
            // The primary screen is still a usable answer.
        }

        var area = screen?.WorkingArea ?? new PixelRect(0, 0,
            Math.Max(1, (int)Width), Math.Max(1, (int)Height));
        return PlaceNear(cursor, area, screen?.Scaling ?? RenderScaling);
    }

    internal PixelPoint PlaceNear(PixelPoint cursor, PixelRect workArea, double scale)
    {
        var width = Math.Max(1, (int)Math.Ceiling(Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(Height * scale));
        var x = cursor.X + 12;
        var y = cursor.Y + 12;
        if (x + width > workArea.Right)
        {
            x = cursor.X - width - 12;
        }
        if (y + height > workArea.Bottom)
        {
            y = cursor.Y - height - 12;
        }

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
