#if DEBUG
using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace WhaleGenie.Web;

/// <summary>
/// The S0 gate of the Lit overlay migration: a transparent, topmost window holding a WebView, with
/// a page that paints its own cards over whatever is behind. Nothing of the ordinary start-up runs
/// with it — no project, no devices, no bridge — because the question is only whether the machine
/// can show a see-through page over another program and how long the first frame takes.
/// </summary>
/// <remarks>
/// Keys: F1 turns the window into a full-screen overlay, F2 toggles topmost, F3 rewrites the page
/// (to measure a warm frame), Esc closes. The elapsed milliseconds on the page are the time from
/// <see cref="NativeWebView.Navigate"/> to the completed navigation, which is the number the plan's
/// gate is written against.
///
/// Keys arrive twice over: the window's own tunnelling handler catches them while the window has
/// the keyboard, and the page posts every key back through <c>window.chrome.webview</c> while the
/// WebView holds it. A native WebView is its own HWND and swallows the keyboard, so without the
/// second path F1/F3 look dead — the same thing a menu search field would run into.
/// </remarks>
internal sealed class OverlayProbeWindow : Window
{
    private readonly NativeWebView _web = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _warmed;
    private bool _measuredShow;

    private OverlayProbeWindow()
    {
        Title = "WhaleGenie overlay probe";
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        CanResize = true;
        Width = 520;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(240, 160);

        // The page paints nothing behind its own cards. The control's background reaches the
        // WebView2 controller as its default background, and a transparent one is what lets the
        // window behind show through.
        _web.Background = Brushes.Transparent;

        _web.NavigationCompleted += (_, e) =>
        {
            Report($"{(_warmed ? "warm" : "cold")} frame after {_clock.ElapsedMilliseconds} ms, success={e.IsSuccess}");
            if (!_warmed)
            {
                // The real menu is a window that already exists with a page that already loaded;
                // measure that shape too, because 600 ms of first frame would rule the plan out.
                _warmed = true;
                Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                {
                    _clock.Restart();
                    Navigate();
                }, TimeSpan.FromMilliseconds(700));
            }
        };

        _web.WebMessageReceived += (_, e) =>
        {
            if (e.Body is string body && body.StartsWith(ProbeKeyPrefix, StringComparison.Ordinal))
            {
                HandleKey(body[ProbeKeyPrefix.Length..]);
            }
        };

        _web.ProcessTerminated += (_, e) => Report("webview terminated: " + e.Reason);

        Content = _web;
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Navigate();
    }

    internal static void Show(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = new OverlayProbeWindow();
        desktop.MainWindow = window;
        window.Opened += (_, _) =>
        {
            if (window._measuredShow)
            {
                return;
            }

            window._measuredShow = true;

            // Hiding and showing a window that is already built is what opening the menu costs
            // once the host exists: the number the plan's 50 ms gate is about. Kept off the Opened
            // handler itself, because hiding there interrupts the navigation that just started.
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                var clock = Stopwatch.StartNew();
                window.Hide();
                window.Show();
                Report("hide+show of a built window took " + clock.ElapsedMilliseconds + " ms");
            }, TimeSpan.FromMilliseconds(1500));
        };
        window.Show();
    }

    private const string ProbeKeyPrefix = "probe:key:";

    private void OnKey(object? sender, KeyEventArgs e)
    {
        HandleKey(e.Key.ToString());
        e.Handled = true;
    }

    /// <summary>One key, from either path; the name is the same as <see cref="Key"/> spells it.</summary>
    private void HandleKey(string name)
    {
        Report("key " + name);
        switch (name)
        {
            case "Escape":
                Close();
                break;

            case "F1":
                // Full screen rather than a manual size: the window manager then covers the whole
                // display without the page having to be laid out for a size it was not built at.
                WindowState = WindowState == WindowState.FullScreen
                    ? WindowState.Normal
                    : WindowState.FullScreen;
                Report($"window state={WindowState} bounds={Bounds} web={_web.Bounds}");
                break;

            case "F2":
                Topmost = !Topmost;
                break;

            case "F3":
                _clock.Restart();
                Navigate();
                break;
        }
    }

    private void Navigate()
    {
        var path = Path.Combine(Path.GetTempPath(), "whalegenie-overlay-probe.html");
        File.WriteAllText(path, ProbePage);
        try
        {
            _web.Navigate(new Uri(path));
            Report("navigate asked for " + path);
        }
        catch (Exception error)
        {
            Report("navigate failed: " + error);
        }
    }

    /// <summary>
    /// The probe is a WinExe, so standard output can be lost when it is started from a shortcut or
    /// a launcher; a file beside the page is what a real-machine run can be read back from.
    /// </summary>
    private static void Report(string line)
    {
        var text = $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}";
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "whalegenie-overlay-probe.log"), text);
        Console.WriteLine("[overlay-probe] " + line);
    }

    private const string ProbePage = """
        <!doctype html>
        <html lang="zh-Hans">
        <head>
        <meta charset="utf-8">
        <style>
          html, body { margin: 0; background: transparent; font: 13px "Segoe UI", sans-serif; color: #1b1b1b; }
          .card { margin: 12px; padding: 16px 18px; border-radius: 12px; background: #fff;
                  box-shadow: 0 8px 28px rgb(0 0 0 / 35%); }
          h1 { margin: 0 0 6px; font-size: 15px; }
          p { margin: 4px 0; color: #444; }
          .grid { display: grid; grid-template-columns: repeat(6, 24px); gap: 0; margin-top: 10px;
                  border-radius: 6px; overflow: hidden; width: max-content; }
          .grid i { width: 24px; height: 24px; background: #ddd; }
          .grid i:nth-child(odd) { background: #fff; }
          code { background: #f2f2f2; padding: 1px 4px; border-radius: 4px; }
          span#seen { color: #b33; }
        </style>
        </head>
        <body>
          <div class="card">
            <h1>透明浮层探针</h1>
            <p>卡片外应当能看见后面的程序。棋盘格用来确认页面背景真的透明。</p>
            <div class="grid"><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i></i></div>
            <p><code>F1</code> 全屏 · <code>F2</code> 置顶开关 · <code>F3</code> 重新装载 · <code>Esc</code> 关闭</p>
            <p>最近一次按键：<span id="seen">—</span></p>
          </div>
          <script>
            // A native WebView is its own window and takes the keyboard with it; the host's own
            // key handler never sees these. Posting them back is what makes F1/F3 work while the
            // page has focus, and it is the path a menu search field will use too.
            const watch = ['F1', 'F2', 'F3', 'Escape'];
            window.addEventListener('keydown', (event) => {
              if (!watch.includes(event.key)) return;
              event.preventDefault();
              document.getElementById('seen').textContent = event.key;
              window.chrome?.webview?.postMessage('probe:key:' + event.key);
            }, true);
          </script>
        </body>
        </html>
        """;
}
#endif
