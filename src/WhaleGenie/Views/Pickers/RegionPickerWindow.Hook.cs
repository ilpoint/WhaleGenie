using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// The keyboard: the hook that hears Escape while another window is in front, and confirming.
public partial class RegionPickerWindow : Window
{
    /// <summary>
    /// 勾住键盘：这个窗口不一定拿得到前台，拿不到时 F1 与 Esc 会落到别的窗口上。两处都收，
    /// 谁先到谁做，`Finish` 自己只做一次。
    /// </summary>
    private void AttachHook()
    {
        try
        {
            if (_hookSource is not { } hook)
            {
                return;
            }

            hook.KeyDown += OnHookKeyDown;
            _hook = hook;
        }
        catch (Exception)
        {
            // 没有钩子时窗口自己那份按键处理还在，只是要求它拿得到焦点。
            _hook = null;
        }
    }

    private void DetachHook()
    {
        if (_hook is not { } hook)
        {
            return;
        }

        hook.KeyDown -= OnHookKeyDown;
        _hook = null;
    }

    private void OnHookKeyDown(KeyHookEvent e)
    {
        if (e.Key == Escape)
        {
            e.Suppress = true;
            Dispatcher.UIThread.Post(() => Finish(null));
        }
        else if (e.Key == F1)
        {
            e.Suppress = true;
            Dispatcher.UIThread.Post(Confirm);
        }
    }

    /// <summary>F1 背后那件事：框够大、框得住，就把它交出去。</summary>
    private void Confirm()
    {
        if (Picked is { } region && Ready(region))
        {
            Finish(region);
        }
    }

    /// <summary>F1 takes the rectangle, Esc leaves without one.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Finish(null);
                break;

            case Key.F1:
                e.Handled = true;
                Confirm();
                break;
        }
    }
}
