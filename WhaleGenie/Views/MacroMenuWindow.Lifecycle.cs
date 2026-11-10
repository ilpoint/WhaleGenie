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

// Opening without taking focus, closing for an action, and where the menu is put on screen.
public sealed partial class MacroMenuWindow
{
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
