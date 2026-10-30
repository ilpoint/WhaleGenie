using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WhaleGenie.Localization;
using WhaleGenie.Models;

using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>Chooses any Lucide icon or a PNG/JPEG/BMP image for a menu cell.</summary>
public sealed class MenuIconPickerWindow : Window
{
    internal const int IconsPerRow = 7;
    internal const int MaximumIconsPerPage = IconsPerRow * 20;
    private readonly TextBox _search;
    private readonly ScrollViewer _scroll;
    private readonly ItemsControl _icons;
    private readonly TextBlock _empty;
    private readonly Button _previousPage;
    private readonly Button _nextPage;
    private readonly TextBlock _pageLabel;
    private readonly StackPanel _pageBar;
    private readonly DispatcherTimer _searchDelay;
    private IReadOnlyList<MenuIconOption> _matches = [];
    private IReadOnlyList<MenuIconOption> _pageOptions = [];
    private string _appliedSearch = string.Empty;
    private bool _hasAppliedSearch;
    private int _renderRevision;
    private int _page = 1;
    private int _pageCount = 1;

    internal MenuIconPickerWindow()
    {
        Width = 620;
        Height = 540;
        MinWidth = 500;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        Title = Strings.Get("Menu.PickIcon");
        ThemeBrush.Assign(this, BackgroundProperty, "SurfaceWindow");

        _searchDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(180),
            DispatcherPriority.Input, (_, _) => ApplySearch(resetPage: true));
        _search = new TextBox
        {
            Height = 36,
            Classes = { "Field" },
            PlaceholderText = Strings.Get("Menu.IconSearch"),
        };
        _search.TextChanged += (_, _) =>
        {
            _searchDelay.Stop();
            _searchDelay.Start();
        };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
        _icons = new ItemsControl
        {
            Name = "MenuIconList",
            Focusable = false,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
        };
        _icons.ItemTemplate = new FuncDataTemplate<IReadOnlyList<MenuIconOption>>((row, _) =>
        {
            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
            };
            foreach (var option in row ?? [])
            {
                strip.Children.Add(IconButton(option));
            }
            return strip;
        });
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _icons,
        };

        _empty = new TextBlock
        {
            Text = Strings.Get("Menu.IconNoResults"),
            Classes = { "Hint" },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _previousPage = PageButton("MenuIconPreviousPage", Strings.Get("Menu.IconPreviousPage"));
        _previousPage.Click += (_, _) => ChangePage(-1);
        _nextPage = PageButton("MenuIconNextPage", Strings.Get("Menu.IconNextPage"));
        _nextPage.Click += (_, _) => ChangePage(1);
        _pageLabel = new TextBlock
        {
            Name = "MenuIconPageLabel",
            Classes = { "Caption" },
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 58,
            TextAlignment = TextAlignment.Center,
        };
        _pageBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _previousPage, _pageLabel, _nextPage },
        };

        var remove = new Button
        {
            Content = Strings.Get("Menu.RemoveIcon"),
            MinWidth = 104,
            Classes = { "Secondary" },
        };
        remove.Click += (_, _) => Close(new MenuIconReference(MenuIconKind.Builtin, string.Empty));
        var image = new Button
        {
            Content = Strings.Get("Menu.PickImage"),
            MinWidth = 120,
            Classes = { "Secondary" },
        };
        image.Click += async (_, _) => await PickImageAsync();
        var cancel = new Button
        {
            Content = Strings.Get("Common.Cancel"),
            MinWidth = 88,
            Classes = { "Secondary" },
        };
        cancel.Click += (_, _) => Close(null);

        var listArea = new Grid
        {
            Children = { _scroll, _empty },
        };
        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 8,
            Children = { remove, image, cancel },
        };
        Grid.SetColumn(image, 1);
        Grid.SetColumn(cancel, 3);

        // 整页内缩只写这一处（原来搜索框、滚动区、页码条、按钮行各写一条 `Margin`）
        Content = new Border
        {
            Padding = new Thickness(12),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                Children = { _search, _pageBar, listArea, buttons },
            },
        };
        Grid.SetRow(_pageBar, 1);
        Grid.SetRow(listArea, 2);
        Grid.SetRow(buttons, 3);

        Opened += (_, _) => _search.Focus();
        Closed += (_, _) => _searchDelay.Stop();
        ApplySearch(resetPage: true);
    }

    internal int PageForCheck => _page;

    internal int PageCountForCheck => _pageCount;

    internal int MatchCountForCheck => _matches.Count;

    internal IReadOnlyList<MenuIconOption> PageOptionsForCheck => _pageOptions;

    internal int RenderRevisionForCheck => _renderRevision;

    internal void ApplySearchForCheck() => ApplySearch(resetPage: true);

    public static Task<MenuIconReference?> ShowAsync(Window owner)
        => new MenuIconPickerWindow().ShowDialogOver<MenuIconReference?>(owner);

    private static Button PageButton(string name, string text)
        => new()
        {
            Name = name,
            Content = text,
            MinWidth = 68,
            Height = 28,
            Classes = { "Secondary" },
        };

    private static Button IconButton(MenuIconOption option)
    {
        // 图标砖：尺寸、内距、圆角、边都交给 `Button.Square`（80×80 恒等，见该类注释）。
        // **砖上没有文字**——标签是 Lucide 的枚举名（18 字符 ≈ 108px，可用只有 64px），
        // 显示出来只会是 `CircleQuestion…` 这种残词；全名走 tooltip（下面那行）。
        var button = new Button
        {
            Classes = { "Square" },
            Content = new IconView
            {
                Width = 22,
                Height = 22,
                Data = option.Kind,
                Stroke = ThemeBrush.Of("TextPrimary"),
                StrokeThickness = 1.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.Classes.Add("MenuIconChoice");
        ToolTip.SetTip(button, option.Label);
        button.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(button) is MenuIconPickerWindow picker)
            {
                picker.Close(new MenuIconReference(MenuIconKind.Builtin, option.Key));
            }
        };
        return button;
    }

    private void ChangePage(int step)
    {
        _searchDelay.Stop();
        ApplySearch(resetPage: true);
        var page = Math.Clamp(_page + step, 1, _pageCount);
        if (page == _page)
        {
            return;
        }

        _page = page;
        ShowPage();
    }

    private void ApplySearch(bool resetPage)
    {
        var wanted = _search.Text?.Trim() ?? string.Empty;
        var queryChanged = !string.Equals(_appliedSearch, wanted,
            StringComparison.Ordinal);
        if (_hasAppliedSearch && !queryChanged)
        {
            return;
        }
        _appliedSearch = wanted;
        _hasAppliedSearch = true;
        _matches = [.. MenuIconCatalog.Options
            .Where(option => wanted.Length == 0
                || option.Label.Contains(wanted, StringComparison.OrdinalIgnoreCase))];
        _pageCount = Math.Max(1,
            (_matches.Count + MaximumIconsPerPage - 1) / MaximumIconsPerPage);
        if (resetPage && queryChanged)
        {
            _page = 1;
        }
        _page = Math.Clamp(_page, 1, _pageCount);
        ShowPage();
    }

    private void ShowPage()
    {
        _pageOptions = [.. _matches
            .Skip((_page - 1) * MaximumIconsPerPage)
            .Take(MaximumIconsPerPage)];
        _icons.ItemsSource = _pageOptions.Chunk(IconsPerRow).ToList();
        _scroll.IsVisible = _pageOptions.Count > 0;
        _empty.IsVisible = _pageOptions.Count == 0;
        _pageBar.IsVisible = _pageCount > 1;
        _previousPage.IsEnabled = _page > 1;
        _nextPage.IsEnabled = _page < _pageCount;
        _pageLabel.Text = Strings.Format("Menu.IconPage", _page, _pageCount);
        _renderRevision++;
    }

    private async Task PickImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("Menu.PickImage"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.Get("Menu.ImageFiles"))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/bmp"],
                },
            ],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            Close(new MenuIconReference(MenuIconKind.Image, path));
        }
    }

}
