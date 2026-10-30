// 截图工具：把几个窗口按深/浅两套主题各渲染一次，存成 PNG。
//
// 为什么单独一个工具而不放进测试：这里要 **真画笔**（`UseHeadlessDrawing = false` + Skia），
// 而 `WhaleGenie.Tests` 全程用无头的假画笔。两套全局配置在同一个进程里会打架。
//
// 跑法：dotnet run --project tools/Screenshots
// 输出：artifacts/screens/<窗口>-<dark|light>.png（已在 .gitignore 里忽略）

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.ViewModels;
using WhaleGenie.Models;
using WhaleGenie.Views;

var outputDir = Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "screens");
outputDir = Path.GetFullPath(outputDir);
Directory.CreateDirectory(outputDir);

AppBuilder.Configure<WhaleGenie.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions
    {
        UseHeadlessDrawing = false,
        ShouldRenderOnUIThread = true,
    })
    .SetupWithoutStarting();

var failures = new List<string>();
var saved = new List<(string Path, Color Sample)>();

foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
{
    Application.Current!.RequestedThemeVariant = variant;

    foreach (var (name, make) in Screens())
    {
        try
        {
            var window = make();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var frame = window.CaptureRenderedFrame();
            if (frame is null)
            {
                failures.Add($"{name}-{variant.Key}: 没截到帧");
                window.Close();
                continue;
            }

            var path = Path.Combine(outputDir, $"{name}-{variant.Key}.png");
            using (var stream = File.Create(path))
            {
                frame.Save(stream, PngBitmapEncoderOptions.Default);
            }

            // **读渲染出来的像素**，不读 `window.Background`：有些窗（菜单、拾取器）窗口底本来就是
            // `Transparent`，真正的底色画在内层面板上。像素是唯一不骗人的证据。
            var sample = SamplePixel(frame);

            if (name == "MacroEditor")
            {
                ReportSeams(frame);
            }

            if (name is "ActionEditor" or "MacroEditor")
            {
                ReportPitch(window);
            }
            saved.Add((path, sample));

            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
        catch (Exception error)
        {
            failures.Add($"{name}-{variant.Key}: {error.GetType().Name}: {error.Message}");
        }
    }
}

Console.WriteLine($"输出目录：{outputDir}");
foreach (var (path, sample) in saved)
{
    Console.WriteLine($"  {Path.GetFileName(path),-34} 取样像素={Sample_Text(sample)}");
}

// 同一窗口两套主题的**渲染结果**必须不同——这就是 `DynamicResource` 生效的证据。
// 读像素而不是读 `window.Background`：菜单/拾取器那类窗的窗口底本来就是 `Transparent`，
// 真正的底色画在内层面板上。
foreach (var group in saved.GroupBy(item => Path.GetFileNameWithoutExtension(item.Path).Split('-')[0]))
{
    var colours = group.Select(item => item.Sample).Distinct().ToList();
    Console.WriteLine(colours.Count > 1
        ? $"  ✓ {group.Key}: 两套主题渲染结果不同（DynamicResource 生效）"
        : $"  ✗ {group.Key}: 两套主题渲染结果相同（{Sample_Text(colours[0])}）—— 可能用了快照，没跟着主题变");
}

if (failures.Count > 0)
{
    Console.WriteLine("\n失败：");
    foreach (var failure in failures)
    {
        Console.WriteLine("  " + failure);
    }

    return 1;
}

return 0;

static IEnumerable<(string Name, Func<Window> Make)> Screens()
{
    yield return ("MacroEditor", () =>
    {
        var window = new MacroEditorWindow();
        window.Opened += (_, _) =>
        {
            // 动作卡片要有分类才渲染。
            var rail = window.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => button.Classes.Contains("Rail"));
            if (rail is not null)
            {
                rail.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }
        };
        window.Opened += (_, _) =>
        {
            // 分隔条到底多宽：**读渲染后的 Bounds**，不读 `Width`（那是"要多少"，
            // Bounds 是"实际多少"，两者不同就说明有别的东西在管它——列宽、MinWidth、样式）。
            foreach (var splitter in window.GetVisualDescendants()
                         .OfType<Avalonia.Controls.GridSplitter>())
            {
                Console.WriteLine(
                    $"    · 分隔条 [{string.Join(' ', splitter.Classes)}] "
                    + $"Width={splitter.Width} MinWidth={splitter.MinWidth} "
                    + $"Bounds={splitter.Bounds.Width:0.#}×{splitter.Bounds.Height:0.#}");
            }
        };
        return window;
    });
    yield return ("MacroMenu", () =>
    {
        // 一个格子里放一个宏：空页面会让它按下标取格子时越界。
        var macro = new WhaleGenie.Models.MacroItem { Name = "one", Description = "截图用" };
        var entry = new WhaleGenie.Models.TriggerEntry
        {
            Kind = WhaleGenie.Models.TriggerKind.Menu,
            MacroName = macro.Name,
        };
        var cell = new WhaleGenie.Models.MenuRuntimeCell(
            0, 0, null, entry, macro);

        var layout = new MenuRuntimeLayout(
            [new MenuRuntimePage("common", false, string.Empty, [cell])],
            [new MenuRuntimePage("default", true, "Default", [])]);

        return new MacroMenuWindow(layout, NoActions(), "notepad");
    });
    yield return ("ActionEditor", () =>
    {
        var window = new ActionEditorWindow();
        window.Opened += (_, _) =>
        {
            // 动作卡片要有分类才渲染：点第一个分类栏钮（`Rail`），否则列表是空的、量不到行距。
            var rail = window.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => button.Classes.Contains("Rail"));
            if (rail is not null)
            {
                rail.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }
        };
        return window;
    });
    yield return ("Main", () => new MainWindow());
}

// 取窗口内一个固定的内部点（画布中心偏上），它的颜色就是"这个窗看起来是什么颜色"。
static Color SamplePixel(Avalonia.Media.Imaging.WriteableBitmap frame)
{
    using var buffer = frame.Lock();
    var x = Math.Min(buffer.Size.Width - 1, Math.Max(0, buffer.Size.Width / 2));
    var y = Math.Min(buffer.Size.Height - 1, Math.Max(0, buffer.Size.Height / 4));
    unsafe
    {
        var row = (byte*)buffer.Address + (y * buffer.RowBytes);
        var pixel = row + (x * 4);          // BGRA8888
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
}

static string Sample_Text(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

// 两栏的行距：**相邻两格上沿之间的 y 差**，取中位数。
// 判据不是"必须相等"——按钮多一道缝、一条边框，就该比行大一点；
// 判据是"这个差能不能用一个原因解释"。
static void ReportPitch(Window window)
{
    static double? Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(value => value).ToList();
        return sorted.Count == 0 ? null : sorted[sorted.Count / 2];
    }

    static IEnumerable<double> Pitch(IEnumerable<Control> controls, Visual root)
    {
        var tops = controls
            .Select(control => control.TranslatePoint(default, root))
            .Where(point => point is not null)
            .Select(point => Math.Round(point!.Value.Y, 1))
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        for (var index = 1; index < tops.Count; index++)
        {
            yield return tops[index] - tops[index - 1];
        }
    }

    var blocks = window.GetVisualDescendants()
        .OfType<Button>()
        .Where(button => button.Classes.Contains("Block"))
        .ToList();

    var rows = window.GetVisualDescendants()
        .OfType<Border>()
        .Where(border => border.Classes.Contains("Row"))
        .ToList();

    // 变量片：`Block` 管缝、`Chip` 管高度（28）——两者分开，才好说清"哪一段来自哪个类"。
    var wallRows = window.GetVisualDescendants()
        .OfType<Button>()
        .Where(button => button.Classes.Contains("Chip"))
        .ToList();

    blocks = blocks.Where(button => !button.Classes.Contains("Chip")).ToList();

    var blockPitch = Median(Pitch(blocks, window));
    var rowPitch = Median(Pitch(rows, window));
    var wallPitch = Median(Pitch(wallRows, window));

    Console.WriteLine($"    · 左栏动作卡片（Block）{blocks.Count} 个，行距中位数 = "
        + (blockPitch is { } b ? $"{b:0.#} px" : "取不到"));
    Console.WriteLine($"    · 右栏行（Row）{rows.Count} 个，行距中位数 = "
        + (rowPitch is { } r ? $"{r:0.#} px" : "取不到"));
    static double? MedianHeight(IEnumerable<Control> controls)
    {
        var heights = controls.Select(control => Math.Round(control.Bounds.Height, 1)).OrderBy(h => h).ToList();
        return heights.Count == 0 ? null : heights[heights.Count / 2];
    }

    Console.WriteLine($"    · 左栏动作卡片高度中位数 = {(MedianHeight(blocks) is { } bh ? $"{bh:0.#} px" : "取不到")}；右栏变量片高度中位数 = {(MedianHeight(wallRows) is { } wh ? $"{wh:0.#} px" : "取不到")}");
    Console.WriteLine($"    · 右栏变量片（Chip）{wallRows.Count} 个，行距中位数 = "
        + (wallPitch is { } w ? $"{w:0.#} px" : "取不到"));
}

// 在窗口中部扫一行，把连续同色的段打出来。
// 用来回答"那条缝到底多宽"——**缝是"列宽"和"条宽"里更大的那个**，
// 而 `GridSplitter.Bounds` 只告诉你条多宽。
static void ReportSeams(Avalonia.Media.Imaging.WriteableBitmap frame)
{
    using var buffer = frame.Lock();
    var y = buffer.Size.Height / 2;
    var runs = new List<(Color Colour, int Length)>();
    Color? previous = null;
    var length = 0;

    unsafe
    {
        var row = (byte*)buffer.Address + (y * buffer.RowBytes);
        for (var x = 0; x < buffer.Size.Width; x++)
        {
            var pixel = row + (x * 4);
            var colour = Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
            if (previous is { } before && before == colour)
            {
                length++;
                continue;
            }

            if (previous is { } finished && length >= 4)
            {
                runs.Add((finished, length));
            }

            previous = colour;
            length = 1;
        }

        if (previous is { } last && length >= 4)
        {
            runs.Add((last, length));
        }
    }

    Console.WriteLine($"    · y={y} 的缝隙（≥4px 的段）：");
    foreach (var (colour, count) in runs)
    {
        Console.WriteLine($"        {Sample_Text(colour)} × {count}");
    }
}

static MenuWindowActions NoActions() => new(
    OpenMacroEditor: _ => { },
    DebugMacro: _ => { },
    OpenSettings: () => { },
    RestoreForeground: () => { },
    AddCurrentWindowRule: () => Task.FromResult<MenuRuntimeLayout?>(null),
    BlockCurrentWindow: () => Task.FromResult(false),
    AddCommonPage: () => Task.FromResult<MenuRuntimeLayout?>(null),
    RemoveCommonPage: _ => Task.FromResult<MenuRuntimeLayout?>(null),
    AddContextPage: _ => Task.FromResult<MenuRuntimeLayout?>(null),
    RemoveContextPage: _ => Task.FromResult<MenuRuntimeLayout?>(null),
    ChooseMacro: _ => Task.FromResult<MenuRuntimeLayout?>(null),
    ChooseIcon: _ => Task.FromResult<MenuRuntimeLayout?>(null),
    Move: (_, _) => Task.FromResult<MenuRuntimeLayout?>(null));
