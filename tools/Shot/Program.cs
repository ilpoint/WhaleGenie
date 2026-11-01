using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using WhaleGenie;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

// Draws one of the program's real windows into a PNG and says where it went:
//
//     dotnet run --project tools/Shot -- settings [--dark]
//
// The window is the same class the program opens, with the same styles, so an arrangement that is
// wrong comes out wrong here. What it cannot show is anything that needs the machine: a device, a
// real screen, another program's window.

var what = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))
    ?? "settings";
var dark = args.Contains("--dark");

// Which sub-page of the settings window to draw, by enum name: General, CaptureInput, Devices,
// MenuLauncher, WindowRules, Theme.
var page = args.FirstOrDefault(argument => argument.StartsWith("--page=", StringComparison.Ordinal))
    is { } named && Enum.TryParse<SettingsSubPage>(named["--page=".Length..], true, out var chosen)
        ? chosen
        : SettingsSubPage.General;

var folder = Path.Combine(AppContext.BaseDirectory, "shots");
Directory.CreateDirectory(folder);

// SetupWithoutStarting rather than a lifetime: this run opens no window of its own, and the one it
// does draw is chosen here. The application still loads its styles, which is the point.
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

if (dark)
{
    Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
}

// The whole desk, as this machine reads it: what the program itself would see. This is the one mode
// that works on the real screen rather than on a window drawn here, so it is how a running instance
// is looked at.
if (what == "desktop")
{
    var folderForDesk = Path.Combine(AppContext.BaseDirectory, "shots");
    Directory.CreateDirectory(folderForDesk);

    using var screen = new WhaleGenie.Core.Devices.Platform.WindowsScreenDevice();
    var area = screen.Desktop.Bounds;

    // A part of the desk can be asked for, which is how one window is looked at closely enough to
    // read: the whole desk shrunk into one picture makes a status bar a smudge.
    if (args.FirstOrDefault(argument => argument.StartsWith("--at=", StringComparison.Ordinal))
        is { } where)
    {
        var parts = where["--at=".Length..].Split(',');
        area = new WhaleGenie.GraphicsCapture.ScreenArea(
            int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
    }

    var picture = screen.Capture(area.Left, area.Top, area.Width, area.Height);
    var bitmap = WhaleGenie.Storage.ImageAssets.ToBitmap(picture);
    var deskPath = Path.Combine(folderForDesk, "desktop.png");
    bitmap.Save(deskPath, PngBitmapEncoderOptions.Default);
    Console.WriteLine(deskPath);
    return;
}

// A package of the current format, with one macro in it: something for a start to open. Writing one
// here rather than by hand is what keeps it loadable — the reader refuses any other version.
if (what == "sample")
{
    var into = args.FirstOrDefault(argument => argument.StartsWith("--out=", StringComparison.Ordinal))
        is { } place ? place["--out=".Length..] : Path.Combine(AppContext.BaseDirectory, "sample.wgmacro");
    // --two writes a stopped macro before a running one, which is what tells a list that puts the
    // enabled ones first from a list that keeps the order the project was made in.
    var macros = args.Contains("--two")
        ? new List<MacroItem>
        {
            new() { Name = "停用的示例", IsEnabled = false },
            new() { Name = "启用的示例" },
        }
        : [new MacroItem { Name = "示例宏", Description = "用来验证启动时打开上次的项目" }];

    WhaleGenie.Storage.MacroPackage.Save(into, macros, [], "v0.1.0");
    Console.WriteLine(into);
    return;
}

// One window of a running program, by process name: the picture is of that window whatever else is
// on the desk and wherever it has been put. This is how a running instance is looked at.
if (what == "window")
{
    var app = args.FirstOrDefault(argument => argument.StartsWith("--app=", StringComparison.Ordinal))
        is { } given ? given["--app=".Length..] : "WhaleGenie";
    var process = args.FirstOrDefault(argument => argument.StartsWith("--pid=", StringComparison.Ordinal))
        is { } numbered
            ? System.Diagnostics.Process.GetProcessById(int.Parse(numbered["--pid=".Length..]))
            : System.Diagnostics.Process.GetProcessesByName(app).FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"'{app}' is not running; saw "
                    + string.Join(", ", System.Diagnostics.Process.GetProcesses()
                        .Select(one => one.ProcessName)
                        .Where(name => name.Contains("Whale", StringComparison.OrdinalIgnoreCase))
                        .Distinct()));

    // The program's own top-level window, found rather than assumed: MainWindowHandle answers zero
    // for a window the runtime does not consider the main one, which a window with its own chrome
    // can be. The largest visible window of the process is the one that was opened.
    var handle = Native.LargestWindow(process.Id);
    if (handle == IntPtr.Zero)
    {
        throw new InvalidOperationException($"'{app}' has no visible window");
    }
    if (!Native.GetWindowRect(handle, out var box))
    {
        throw new InvalidOperationException("the window has no rectangle");
    }

    using var viewer = new WhaleGenie.Core.Devices.Platform.WindowsScreenDevice();
    var picture = viewer.Capture(new WhaleGenie.Core.Devices.ScreenCaptureRequest(
        box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top,
        WhaleGenie.GraphicsCapture.CaptureWay.Auto, (long)handle));

    var into = Path.Combine(AppContext.BaseDirectory, "shots");
    Directory.CreateDirectory(into);
    var windowPath = Path.Combine(into, $"{app}.png");
    WhaleGenie.Storage.ImageAssets.ToBitmap(picture.Frame).Save(windowPath, PngBitmapEncoderOptions.Default);
    Console.WriteLine(windowPath);
    return;
}

var window = what switch
{
    "settings" => (Window)new SettingsWindow(null, null, SettingsRootPage.System, page),
    "editor" => new ActionEditorWindow(null, ActionCatalog.Definitions, [], []),
    "variables" => new GlobalVariablesWindow(),
    _ => throw new ArgumentException($"unknown window '{what}'"),
};

window.Width = window.Width is > 200 ? window.Width : 980;
window.Height = window.Height is > 200 ? window.Height : 720;

window.Show();
Dispatcher.UIThread.RunJobs();

// Two ticks: the first lays out and paints, the second paints what the first measured.
AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
Dispatcher.UIThread.RunJobs();

var shot = window.CaptureRenderedFrame()
    ?? throw new InvalidOperationException("nothing was rendered");
var suffix = what == "settings" && page != SettingsSubPage.General ? $"-{page}" : string.Empty;
var path = Path.Combine(folder, $"{what}{suffix}{(dark ? "-dark" : string.Empty)}.png");
shot.Save(path, PngBitmapEncoderOptions.Default);

Console.WriteLine(path);
window.Close();

/// <summary>The one rectangle the program does not already have a way to ask for.</summary>
internal static class Native
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr window, out Box rectangle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool EnumWindows(Watch watch, IntPtr parameter);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    private delegate bool Watch(IntPtr window, IntPtr parameter);

    /// <summary>The biggest visible window a process owns.</summary>
    internal static IntPtr LargestWindow(int process)
    {
        var best = IntPtr.Zero;
        var bestArea = 0L;

        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)process || !IsWindowVisible(window) || !GetWindowRect(window, out var box))
            {
                return true;
            }

            var area = (long)(box.Right - box.Left) * (box.Bottom - box.Top);
            if (area > bestArea)
            {
                best = window;
                bestArea = area;
            }

            return true;
        }, IntPtr.Zero);

        return best;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct Box
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
