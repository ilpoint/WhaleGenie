using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Tests.Support;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// 行内说明的视觉基线：气泡正文取主文字色并对卡片底保持可读，说明图标与同行文字共用一条中线，
/// 命中区 24×24、行内标签不再带走字段下边距。读数全部来自真实控件矩形与主题画刷，
/// 不写第二套期望色值。
/// </summary>
public class SettingsHintVisualTests
{
    private static readonly SettingsSubPage[] Pages =
    [
        SettingsSubPage.General, SettingsSubPage.Hotkeys, SettingsSubPage.Screen,
        SettingsSubPage.Input, SettingsSubPage.Devices,
        SettingsSubPage.MenuLauncher, SettingsSubPage.WindowRules,
        SettingsSubPage.WheelMenu, SettingsSubPage.WheelActions,
        SettingsSubPage.Theme,
    ];

    [Fact]
    public void The_bubble_body_paints_the_primary_text_on_the_card()
    {
        Ui.Run(() =>
        {
            var setup = OpenPinned();
            Assert.NotNull(setup.Host.Panel.Parent);
            Assert.Equal(Token(setup.Window, "TextPrimary"), Colour(setup.Host.Body.Foreground));
            Assert.Equal(Token(setup.Window, "SurfaceCard"), Colour(setup.Host.Panel.Background));
            setup.Window.Close();
        });
    }

    [Fact]
    public void The_bubble_body_uses_the_small_body_size_and_line()
    {
        Ui.Run(() =>
        {
            var setup = OpenPinned();
            Assert.Equal(12, setup.Host.Body.FontSize);
            Assert.Equal(16, setup.Host.Body.LineHeight);
            setup.Window.Close();
        });
    }

    [Fact]
    public void The_bubble_body_stays_readable_in_both_themes()
    {
        Ui.Run(() =>
        {
            var dark = OpenPinned(ThemeVariant.Dark);
            var darkContrast = Contrast(dark.Host.Body.Foreground, dark.Host.Panel.Background);
            Assert.True(darkContrast >= 10, $"dark contrast {darkContrast:0.0}:1");
            dark.Window.Close();

            var light = OpenPinned(ThemeVariant.Light);
            var lightContrast = Contrast(light.Host.Body.Foreground, light.Host.Panel.Background);
            Assert.True(lightContrast >= 12, $"light contrast {lightContrast:0.0}:1");
            Assert.True(lightContrast >= 7);
            light.Window.Close();

            var window = new SettingsWindow { RequestedThemeVariant = ThemeVariant.Dark };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Application.Current!.TryGetResource(
                "TextSecondary", ThemeVariant.Dark, out var secondaryValue), "TextSecondary");
            Assert.True(Application.Current.TryGetResource(
                "SurfaceCard", ThemeVariant.Dark, out var cardValue), "SurfaceCard");
            Assert.True(
                Contrast(
                    Assert.IsAssignableFrom<IBrush>(secondaryValue),
                    Assert.IsAssignableFrom<IBrush>(cardValue)) < 10,
                "the secondary text must fail the dark floor, or the assertion is wrong");
            window.Close();
        });
    }

    [Fact]
    public void Switching_the_theme_repaints_an_open_bubble()
    {
        Ui.Run(() =>
        {
            var setup = OpenPinned(ThemeVariant.Dark);
            var foreground = Colour(setup.Host.Body.Foreground);
            var background = Colour(setup.Host.Panel.Background);
            var opened = setup.Host.OpenCount;
            var rect = setup.Host.PanelRect;

            setup.Window.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Token(setup.Window, "TextPrimary"), Colour(setup.Host.Body.Foreground));
            Assert.Equal(Token(setup.Window, "SurfaceCard"), Colour(setup.Host.Panel.Background));
            Assert.NotEqual(foreground, Colour(setup.Host.Body.Foreground));
            Assert.NotEqual(background, Colour(setup.Host.Panel.Background));
            Assert.Equal(HintBubbleState.Pinned, setup.Host.State);
            Assert.Equal(opened, setup.Host.OpenCount);
            Assert.Equal(rect, setup.Host.PanelRect);
            setup.Window.Close();
        });
    }

    [Fact]
    public void Every_inline_hint_row_is_twenty_four_high()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            var seen = 0;

            foreach (var page in Pages)
            {
                viewModel.SetPage(page);
                Dispatcher.UIThread.RunJobs();

                var heights = new List<double>();
                foreach (var hint in Hints(window))
                {
                    var row = Assert.IsAssignableFrom<Panel>(hint.Parent);
                    var parts = string.Join("; ", row.Children.Select(child => child switch
                    {
                        TemplatedControl templated =>
                            $"{child.GetType().Name} h={child.Bounds.Height} min={templated.MinHeight} pad={templated.Padding}",
                        _ => $"{child.GetType().Name} h={child.Bounds.Height}",
                    }));
                    Assert.True(
                        row.Bounds.Height is >= 23 and <= 25,
                        $"{page}: {hint.Text} row is {row.Bounds.Height} [{parts}]");
                    heights.Add(row.Bounds.Height);
                    seen++;
                }

                if (heights.Count > 0)
                {
                    Assert.InRange(heights.Max() - heights.Min(), 0, 1);
                }
            }

            // 第十八处是外观页的自定义主题行，只有当前设置选中自定义主题时才出现；默认两套内置
            // 主题下是 17 处，选中自定义时是 18 处。XAML 里的 18 处由下一个用例固定计数。
            Assert.InRange(seen, 17, 18);
            window.Close();
        });
    }

    [Fact]
    public void The_icon_center_sits_on_the_text_line()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);

            foreach (var page in Pages)
            {
                viewModel.SetPage(page);
                Dispatcher.UIThread.RunJobs();
                foreach (var hint in Hints(window))
                {
                    AssertCentered(hint);
                }
            }

            window.Close();
        });

        Ui.Run(() =>
        {
            var text = new TextBlock { Text = "标题" };
            var icon = new HintInfo { Text = "说明" };
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            stack.Children.Add(text);
            stack.Children.Add(icon);

            var gridText = new TextBlock { Text = "标题" };
            var gridIcon = new HintInfo { Text = "说明" };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 6 };
            grid.Children.Add(gridText);
            Grid.SetColumn(gridIcon, 1);
            grid.Children.Add(gridIcon);

            var page = new StackPanel { Spacing = 12 };
            page.Children.Add(stack);
            page.Children.Add(grid);
            var window = new Window { Width = 400, Height = 200, Content = page };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            AssertCentered(icon);
            AssertCentered(gridIcon);
            window.Close();
        });
    }

    [Fact]
    public void The_hit_area_is_24_and_does_not_cover_the_text()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            foreach (var hint in Hints(window))
            {
                Assert.True(hint.Bounds.Width >= 24, $"{hint.Bounds.Width} wide");
                Assert.True(hint.Bounds.Height >= 24, $"{hint.Bounds.Height} high");

                var row = Assert.IsAssignableFrom<Control>(hint.Parent);
                var line = LineOf(hint) ?? throw new InvalidOperationException("no text beside the icon");
                var iconArea = Area(hint, row);
                Assert.False(iconArea.Intersects(Area(line, row)), "the hit area covers the text");

                var item = hint.GetVisualAncestors().OfType<FASettingsExpanderItem>().FirstOrDefault();
                if (item?.Footer is Control footer)
                {
                    Assert.False(iconArea.Intersects(Area(footer, row)), "the hit area covers the control");
                }
            }

            window.Close();
        });
    }

    [Fact]
    public void An_inline_label_drops_its_bottom_margin_only_in_the_row()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            foreach (var hint in Hints(window))
            {
                if (LineOf(hint) is { } line)
                {
                    Assert.Equal(0, line.Margin.Bottom);
                }
            }

            Assert.True(Application.Current!.TryGetResource(
                "GapField", window.ActualThemeVariant, out var value), "GapField");
            var gap = Assert.IsType<Thickness>(value);
            var ordinary = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(block => block.Classes.Contains("FieldLabel") && block.IsEffectivelyVisible)
                .First(block => block.Margin.Bottom == gap.Bottom);
            Assert.Equal(gap, ordinary.Margin);
            window.Close();
        });
    }

    [Fact]
    public void The_text_edge_does_not_drift_and_does_not_wrap()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);

            foreach (var page in Pages)
            {
                viewModel.SetPage(page);
                Dispatcher.UIThread.RunJobs();

                var ordinaryLeft = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(block => block.Classes.Contains("FieldLabel")
                        && block.IsEffectivelyVisible
                        && !block.GetVisualAncestors().OfType<HintInfo>().Any())
                    .Select(block => block.TranslatePoint(default, window)!.Value.X)
                    .FirstOrDefault();

                foreach (var hint in Hints(window))
                {
                    var row = Assert.IsAssignableFrom<Control>(hint.Parent);
                    var line = LineOf(hint) ?? throw new InvalidOperationException("no text beside the icon");
                    var textRight = line.TranslatePoint(new Point(line.Bounds.Width, 0), row)!.Value.X;
                    var iconLeft = hint.TranslatePoint(default, row)!.Value.X;
                    Assert.InRange(iconLeft - textRight, 5.5, 6.5);

                    if (ordinaryLeft > 0)
                    {
                        var left = line.TranslatePoint(default, window)!.Value.X;
                        Assert.InRange(Math.Abs(left - ordinaryLeft), 0, 0.5);
                    }

                    if (line is TextBlock label && label.Classes.Contains("FieldLabel"))
                    {
                        Assert.True(label.Bounds.Height <= 23, $"{label.Text} wrapped");
                    }
                }
            }

            window.Close();
        });
    }

    [Fact]
    public void Every_hint_icon_is_still_on_its_row()
    {
        var expected = new Dictionary<string, int>
        {
            ["SettingsSystemView.axaml"] = 14,
            ["SettingsTriggersView.axaml"] = 3,
            ["SettingsAppearanceView.axaml"] = 1,
            ["ActionEditorView.axaml"] = 3,
            ["ParameterScalarView.axaml"] = 2,
            ["ParameterStructuredView.axaml"] = 1,
            ["MacroSettingsSpecificView.axaml"] = 1,
        };

        foreach (var (file, count) in expected)
        {
            var markup = ViewSource.Markup(file);
            var tags = Regex.Matches(
                markup, @"<views:HintInfo\b[^>]*>", RegexOptions.Singleline);
            Assert.Equal(count, tags.Count);
            foreach (Match tag in tags)
            {
                Assert.DoesNotContain("Margin=", tag.Value, StringComparison.Ordinal);
                Assert.DoesNotContain("VerticalAlignment=", tag.Value, StringComparison.Ordinal);
                Assert.DoesNotContain("Width=", tag.Value, StringComparison.Ordinal);
                Assert.DoesNotContain("Height=", tag.Value, StringComparison.Ordinal);
            }
        }
    }

    private static HintSetup OpenPinned(ThemeVariant? theme = null)
    {
        var window = new SettingsWindow();
        if (theme is not null)
        {
            window.RequestedThemeVariant = theme;
        }

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var hint = Hints(window).First();
        var host = HintBubbleHost.For(window);
        host.Delays = new ManualHintDelay();
        hint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(hint.IsBubblePinned);
        return new HintSetup(window, hint, host);
    }

    private static List<HintInfo> Hints(SettingsWindow window)
        => window.GetVisualDescendants()
            .OfType<HintInfo>()
            .Where(hint => hint.IsEffectivelyVisible
                && !string.IsNullOrWhiteSpace(hint.Text))
            .ToList();

    private static Control? LineOf(HintInfo hint)
    {
        if (hint.Parent is not Panel row)
        {
            return null;
        }

        Control? line = row.Children.OfType<TextBlock>().FirstOrDefault();
        line ??= row.Children.OfType<CheckBox>().FirstOrDefault();
        line ??= row.Children.OfType<Panel>()
            .FirstOrDefault(panel => panel.Children.Count > 0
                && panel.Children.All(child => child is TextBlock));
        return line;
    }

    private static void AssertCentered(HintInfo hint)
    {
        var row = Assert.IsAssignableFrom<Control>(hint.Parent);
        var line = LineOf(hint) ?? throw new InvalidOperationException("no text beside the icon");
        var icon = hint.GetVisualDescendants().OfType<IconView>().First();
        var iconCenter = icon.TranslatePoint(
            new Point(icon.Bounds.Width / 2, icon.Bounds.Height / 2), row)!.Value.Y;
        var textCenter = line.TranslatePoint(
            new Point(0, line.Bounds.Height / 2), row)!.Value.Y;
        Assert.True(
            Math.Abs(iconCenter - textCenter) <= 1,
            $"icon {iconCenter:0.0} vs text {textCenter:0.0}");
    }

    private static Rect Area(Control control, Visual origin)
        => new(control.TranslatePoint(default, origin)!.Value, control.Bounds.Size);

    private static double Contrast(IBrush? foreground, IBrush? background)
        => ThemePalette.Contrast(Colour(foreground), Colour(background));

    private static Color Colour(IBrush? brush)
        => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color Token(Visual host, string key)
    {
        Assert.True(Application.Current!.TryGetResource(
            key, host.ActualThemeVariant, out var value), key);
        return Colour(Assert.IsAssignableFrom<IBrush>(value));
    }

    private sealed record HintSetup(Window Window, HintInfo Hint, HintBubbleHost Host);
}
