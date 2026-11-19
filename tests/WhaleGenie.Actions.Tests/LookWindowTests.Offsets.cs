using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// A point on the picture as an offset, and a click just outside it.
public partial class LookWindowTests
{

    [Fact]
    public void Clicking_outside_the_picture_is_a_click_at_its_edge()
    {
        // Drawn four times its size, a click on the far corner is still inside the picture.
        Assert.Equal(new ScreenPoint(9, 4), LookGeometry.Inside(10, 5, 4, 38, 18));
        Assert.Equal(new ScreenPoint(9, 4), LookGeometry.Inside(10, 5, 4, 400, 400));
        Assert.Equal(new ScreenPoint(0, 0), LookGeometry.Inside(10, 5, 4, -50, -50));
    }

    /// <summary>
    /// 偏移点只在"这一步刚好看了一眼屏幕"时才有得拾取：命中的中心是偏移的原点，而那一眼看到的画面
    /// 就是可点的底图。找图、识别文字、识别图像都在此列；点一下鼠标这类动作有偏移的写法，却没有可点的
    /// 东西，不给按钮。
    /// </summary>
    [Theory]
    [InlineData("vision.clickImage", true)]
    [InlineData("ocr.clickText", true)]
    [InlineData("vision.clickDetect", true)]
    [InlineData("input.mouseClick", false)]
    public void Picking_a_click_point_is_offered_where_the_step_looked_at_the_screen(
        string key, bool offered)
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("match"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction(key);
            Dispatcher.UIThread.RunJobs();

            var pickers = viewModel.Rows.Concat(viewModel.AdvancedRows)
                .Where(row => row.ShowsOffsetPick)
                .ToList();

            Assert.Equal(offered ? 1 : 0, pickers.Count);
            if (offered)
            {
                // The two halves of the offset sit on the one line the button is under.
                Assert.Equal("offsetX", pickers[0].First.Definition.Name);
                Assert.Equal("offsetY", pickers[0].Second?.Definition.Name);
            }
        });
    }
}
