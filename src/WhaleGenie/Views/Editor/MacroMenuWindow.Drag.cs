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

// Picking up a cell, dropping it somewhere else, and what a drop onto a slot does.
public sealed partial class MacroMenuWindow
{
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
        _dragButton.BorderBrush = ThemeBrush.Of("AccentFill");
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
}
