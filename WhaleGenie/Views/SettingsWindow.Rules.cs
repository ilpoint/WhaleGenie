using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class SettingsWindow
{
    /// <summary>
    /// The menu rules: choosing one, moving it, and dragging it to a new place in the
/// list.
    /// </summary>
    // The row being dragged and where the drag started, kept for the length of one gesture.
    private MenuRuleRow? _ruleDragSource;
    private Point _ruleDragOrigin;
    private bool _ruleDragging;

    private async Task AddMenuRuleAsync()
    {
        if (await WindowRuleWindow.PickAsync(this, SharedDevices.Current.Windows,
            SharedDevices.Current.Hook) is { } rule)
        {
            _viewModel.AddMenuRule(rule);
        }
    }

    private async Task EditMenuRuleAsync()
    {
        if (_triggers.MenuRuleList?.SelectedItem is not MenuRuleRow row
            || row.IsDefault)
        {
            return;
        }

        if (await WindowRuleWindow.PickAsync(this, SharedDevices.Current.Windows,
            SharedDevices.Current.Hook, row.Rule) is { } rule)
        {
            _viewModel.UpdateMenuRule(row, rule);
        }
    }

    internal void OnAddMenuRule(object? sender, RoutedEventArgs e) => _ = AddMenuRuleAsync();

    internal void OnEditMenuRule(object? sender, RoutedEventArgs e) => _ = EditMenuRuleAsync();

    internal void OnRemoveMenuRule(object? sender, RoutedEventArgs e)
    {
        if (_triggers.MenuRuleList?.SelectedItem is MenuRuleRow row)
        {
            _viewModel.RemoveMenuRule(row);
        }
    }

    internal void OnAddMenuRulePage(object? sender, RoutedEventArgs e)
    {
        if (_triggers.MenuRuleList?.SelectedItem is MenuRuleRow row)
        {
            _viewModel.AddMenuPage(row);
        }
    }

    internal void OnMoveMenuRuleUp(object? sender, RoutedEventArgs e)
        => MoveMenuRule(-1);

    internal void OnMoveMenuRuleDown(object? sender, RoutedEventArgs e)
        => MoveMenuRule(1);

    private void MoveMenuRule(int step)
    {
        if (_triggers.MenuRuleList?.SelectedItem is MenuRuleRow row)
        {
            _viewModel.MoveMenuRule(row, step);
        }
    }

    private void OnRulePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list
            || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _ruleDragSource = RowAt(e.Source as Visual);
        _ruleDragOrigin = e.GetPosition(list);
        _ruleDragging = false;
    }

    private void OnRulePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_ruleDragSource is null || sender is not ListBox list
            || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(list);
        _ruleDragging = Math.Abs(point.X - _ruleDragOrigin.X) > 5
                        || Math.Abs(point.Y - _ruleDragOrigin.Y) > 5;
    }

    private void OnRulePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ListBox list || !_ruleDragging || _ruleDragSource is not { } source)
        {
            _ruleDragSource = null;
            _ruleDragging = false;
            return;
        }

        var target = RowAt(list.InputHitTest(e.GetPosition(list)) as Visual);
        _ruleDragSource = null;
        _ruleDragging = false;
        if (target is not null)
        {
            _viewModel.MoveMenuRuleTo(source, target);
        }
    }

    private static MenuRuleRow? RowAt(Visual? visual)
    {
        var item = visual?.FindAncestorOfType<ListBoxItem>(true);
        return item?.DataContext as MenuRuleRow;
    }
}
