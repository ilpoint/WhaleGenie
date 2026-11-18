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

// The cells themselves: empty ones, filled ones, their icons and what a press on one means.
public sealed partial class MacroMenuWindow
{
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
                    child.BorderBrush = ThemeBrush.Of("AccentFill");
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
            Stroke = ThemeBrush.Of("TextTertiary"),
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
        AttachContextMenu(button, CellMenu(slot), () => CellAnswers(slot));
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
            Foreground = ThemeBrush.Of("TextPrimary"),
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
                Stroke = ThemeBrush.Of("TextPrimary"),
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
            Background = ThemeBrush.Of("SurfaceField") ?? Brushes.Transparent,
            BorderBrush = ThemeBrush.Of("BorderControl"),
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
}
