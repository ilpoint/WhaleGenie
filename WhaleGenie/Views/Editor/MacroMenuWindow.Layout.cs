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

// Where the parts sit: the divider, the pages, the pinned state and the keys the menu answers.
public sealed partial class MacroMenuWindow
{
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
            ? ThemeBrush.Of("SurfaceHover")
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
        PinnedChanged?.Invoke(pinned);
        _pinButton.Background = pinned
            ? ThemeBrush.Of("SurfaceHover")
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

    /// <summary>Takes a page of the menu away, which is the one answer a page tab has.</summary>
    private async Task RemovePageAsync(bool common, int page)
    {
        var pageId = common
            ? _layout.CommonPages[page].PageId
            : _layout.ContextPages[page].PageId;
        if (await (common
                ? _actions.RemoveCommonPage(pageId)
                : _actions.RemoveContextPage(pageId)) is { } updated)
        {
            SetLayout(updated);
        }
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
}
