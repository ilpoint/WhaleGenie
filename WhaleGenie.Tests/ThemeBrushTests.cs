using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// 令牌取画笔的两条路。**这条用例是为了防"窗口透明"那个线上 bug 再回来**——
/// 那个 bug 的特征是：所有测试都绿，只有真机上窗口没有底。
///
/// - <c>ThemeBrush.Of</c> 必须带 `ActualThemeVariant` 才查得到 `ThemeDictionaries` 里的键；
/// - <c>ThemeBrush.Assign</c> 走 `DynamicResource`，设属性用它。
/// </summary>
public class ThemeBrushTests
{
    [Fact]
    public void A_token_resolves_to_a_brush_and_a_bound_property_gets_one()
    {
        Ui.Run(() =>
        {
            // ① 快照：不带 variant 的旧写法取不到，带 variant 的能取到。
            var snapshot = Views.ThemeBrush.Of("SurfaceWindow");
            Assert.NotNull(snapshot);

            // ② 绑到属性上：`DynamicResource` 这条路在**代码窗**里也要生效。
            var window = new Window();
            Views.ThemeBrush.Assign(window, Window.BackgroundProperty, "SurfaceWindow");
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.NotNull(window.Background);
            Assert.IsType<SolidColorBrush>(window.Background);

            window.Close();
        });
    }
}
