using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// 设置窗的两处强调色：左栏选中的那个分组，和内容列顶上选中的那一页页签。两者都铺 `AccentFill`，
/// 字与图标都必须是 `TextOnAccent`，并且这一对要过 4.5:1。
/// </summary>
public class SettingsChromeTests
{
    [Fact]
    public void The_selected_row_paints_the_accent_with_white_text_and_icon()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var nav = Assert.IsType<FANavigationView>(window.FindControl<FANavigationView>("Nav"));
            var item = nav.MenuItems.OfType<FANavigationViewItem>()
                .Single(candidate => (string?)candidate.Tag == nameof(SettingsRoot.System));

            var root = item.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "LayoutRoot");
            var background = Assert.IsType<SolidColorBrush>(root.Background);
            Assert.Equal(Token("AccentFill"), background.Color);

            var icon = Assert.Single(item.GetVisualDescendants().OfType<IconView>());
            var stroke = Assert.IsType<SolidColorBrush>(icon.Stroke);
            Assert.Equal(Token("TextOnAccent"), stroke.Color);

            window.Close();
        });
    }

    [Fact]
    public void The_selected_group_tab_is_a_filled_capsule()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var tab = window.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("Capsule") && button.Classes.Contains("selected"));

            Assert.Equal(Token("AccentFill"), Assert.IsType<SolidColorBrush>(tab.Background).Color);
            Assert.Equal(Token("TextOnAccent"), Assert.IsType<SolidColorBrush>(tab.Foreground).Color);

            window.Close();
        });
    }

    [Fact]
    public void White_text_on_the_accent_stays_readable()
    {
        Ui.Run(() =>
        {
            Assert.True(
                ThemePalette.Contrast(Token("AccentFill"), Token("TextOnAccent")) >= 4.5,
                "white on the fill has to stay at 4.5:1");
        });
    }

    private static Color Token(string key)
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource(key, app.ActualThemeVariant, out var value));
        return value switch
        {
            Color colour => colour,
            ISolidColorBrush brush => brush.Color,
            _ => throw new InvalidOperationException(key + " is not a colour"),
        };
    }
}
