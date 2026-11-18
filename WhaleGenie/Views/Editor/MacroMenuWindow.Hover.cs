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

// What a cell shows while the pointer is over it, and the buttons along the edge.
public sealed partial class MacroMenuWindow
{
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
            button.Background = ThemeBrush.Of("SurfaceHover") ?? Brushes.Transparent;
            button.BorderBrush = ThemeBrush.Of("AccentFill");
            button.BorderThickness = new Thickness(2);
        };
        button.PointerExited += (_, _) =>
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            button.Background = ThemeBrush.Of("SurfaceField") ?? Brushes.Transparent;
            var selected = _selected?.Key == slot.Key;
            button.BorderBrush = ThemeBrush.Of(selected ? "AccentFill" : "BorderControl");
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
                Stroke = ThemeBrush.Of("TextTertiary"),
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
            Background = ThemeBrush.Of("SurfaceField") ?? Brushes.Transparent,
            BorderBrush = ThemeBrush.Of("BorderControl"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Cursor = ButtonCursor,
            Content = new IconView
            {
                Width = 14,
                Height = 14,
                Data = icon,
                Stroke = ThemeBrush.Of("TextPrimary"),
                StrokeThickness = 1.5,
            },
        };
        ToolTip.SetTip(button, tip);
        return button;
    }
}
