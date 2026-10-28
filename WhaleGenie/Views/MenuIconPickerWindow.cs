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
        Background = Brush("AppBackdrop", Color.FromRgb(32, 32, 32));

        _searchDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(180),
            DispatcherPriority.Input, (_, _) => ApplySearch(resetPage: true));
        _search = new TextBox
        {
            Height = 36,
            Margin = new Thickness(12, 12, 12, 8),
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
            Margin = new Thickness(8, 0, 8, 8),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _icons,
        };

        _empty = new TextBlock
        {
            Text = Strings.Get("Menu.IconNoResults"),
            Foreground = Brush("AppMuted", Colors.LightGray),
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
            FontSize = 11,
            Foreground = Brush("AppMuted", Colors.LightGray),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 58,
            TextAlignment = TextAlignment.Center,
        };
        _pageBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 12, 8),
            Children = { _previousPage, _pageLabel, _nextPage },
        };

        var remove = new Button
        {
            Content = Strings.Get("Menu.RemoveIcon"),
            MinWidth = 104,
            Height = 32,
        };
        remove.Click += (_, _) => Close(new MenuIconReference(MenuIconKind.Builtin, string.Empty));
        var image = new Button
        {
            Content = Strings.Get("Menu.PickImage"),
            MinWidth = 120,
            Height = 32,
        };
        image.Click += async (_, _) => await PickImageAsync();
        var cancel = new Button
        {
            Content = Strings.Get("Common.Cancel"),
            MinWidth = 88,
            Height = 32,
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
            Margin = new Thickness(12, 0, 12, 12),
            Children = { remove, image, cancel },
        };
        Grid.SetColumn(image, 1);
        Grid.SetColumn(cancel, 3);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Children = { _search, _pageBar, listArea, buttons },
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
        };

    private static Button IconButton(MenuIconOption option)
    {
        var button = new Button
        {
            Width = 80,
            Height = 80,
            Margin = new Thickness(1),
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(5),
            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new IconView
                    {
                        Width = 30,
                        Height = 30,
                        Data = option.Kind,
                        Stroke = Brush("AppText", Colors.White),
                        StrokeThickness = 1.5,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = option.Label,
                        FontSize = 9,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
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

    private static IBrush Brush(string key, Color fallback)
        => Application.Current?.TryFindResource(key, out var found) == true && found is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
