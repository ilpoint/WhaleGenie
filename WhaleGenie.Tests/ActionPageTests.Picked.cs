using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// A rectangle picked on the basic page, and the colour read after its pixel.
public partial class ActionPageTests
{

    /// <summary>
    /// How a step lands and how it beats is one question: how far the click may miss, whether the
    /// pointer is put back, how long the press is held, how many times it happens, how long it waits
    /// in between and whether the place still has to be right a moment later. Tuning a step that is
    /// nearly right used to mean reading that question off two pages, because one field of the run
    /// happened to be folded away; the run is read together on the basic page and stands in one
    /// piece there.
    /// </summary>
    [Theory]
    [InlineData("input.keyPress", "holdMs", "repeat", "intervalMs")]
    [InlineData("input.hotkey", "holdMs", "repeat", "intervalMs")]
    [InlineData("input.mouseClick", "variance", "moveBack", "holdMs", "repeat", "intervalMs")]
    [InlineData("input.mouseDoubleClick", "variance", "moveBack", "repeat", "intervalMs")]
    [InlineData("input.mouseScroll", "repeat", "intervalMs")]
    [InlineData("input.mouseDrag", "settleMs")]
    [InlineData("gamepad.button", "holdMs", "repeat", "intervalMs")]
    [InlineData("vision.clickImage", "variance", "confirmMs")]
    [InlineData("ocr.clickText", "variance", "confirmMs")]
    public void How_a_step_lands_and_how_it_beats_are_read_as_one_run(string action,
        params string[] run)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(action);
            var basic = viewModel.Rows.SelectMany(Names).ToList();
            var advanced = viewModel.AdvancedRows.SelectMany(Names).ToList();

            // Not one field of the run is folded away, and on the basic page they follow each other
            // with nothing in between.
            Assert.Empty(run.Intersect(advanced, StringComparer.Ordinal));

            var at = basic.IndexOf(run[0]);
            Assert.True(at >= 0, $"{action}: {run[0]} is not on the basic page");
            Assert.Equal(run, basic.GetRange(at, run.Length));
        });
    }

    /// <summary>
    /// The four numbers of a place to look are ordinary fields on the basic page, beside the things
    /// that say what the step is: the rectangle is dragged out and made exact in one place rather
    /// than read on two pages that could disagree.
    /// </summary>
    [Theory]
    [InlineData("vision.capture")]
    [InlineData("ocr.recognize")]
    public void A_rectangle_is_picked_and_written_on_the_basic_page(string action)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(action);
            var basic = viewModel.Rows.SelectMany(Names).ToList();
            var advanced = viewModel.AdvancedRows.SelectMany(Names).ToList();

            if (action == "ocr.recognize")
            {
                // 识别文字与"搜索区域"那几行同一个结构：一处一行，四个数、或者一个变量。
                Assert.Contains("region", basic);
                Assert.DoesNotContain("x", basic);
                Assert.DoesNotContain("region", advanced);
                return;
            }

            Assert.Contains("x", basic);
            Assert.Contains("y", basic);
            Assert.Contains("width", basic);
            Assert.Contains("height", basic);
            Assert.DoesNotContain("x", advanced);
            Assert.DoesNotContain("width", advanced);
        });
    }

    /// <summary>
    /// A pixel is asked two things — what colour it shows and where it is — and the colour is asked
    /// first, on the line above: the magnifier answers both in one go by being aimed at the pixel,
    /// and two lines that are answered together read as one question rather than as a question and
    /// an afterthought.
    /// </summary>
    [Theory]
    [InlineData("vision.waitColor")]
    [InlineData("vision.matchColor")]
    public void The_colour_of_a_pixel_is_asked_for_after_the_pixel(string action)
    {
        Ui.Run(() =>
        {
            var rows = Open(action).Rows.ToList();

            var colour = rows.FindIndex(row => row.First.Definition.Name == "color");
            var where = rows.FindIndex(row =>
                row.First.Definition.Name == "x" || row.Second?.Definition.Name == "x");

            Assert.True(colour >= 0, "the colour is not on a line of its own");
            Assert.True(where >= 0, "the pixel is not on a line of its own");
            // 分块之后先问“看哪一个点”，再问“什么颜色”：一块只问一件事，位置是它的第一件事。
            Assert.True(where < colour, "the colour is asked for before the pixel it belongs to");
        });
    }

    /// <summary>
    /// 怎么看屏幕已经不是步骤的字段了：每秒取几次画面、已经取到的画面能不能再读一次，都由设置
    /// 里的「取帧」决定，同一次运行的每一步共用同一个答案。这里把「搬干净了」变成能失败的断言：
    /// 任何一个看屏幕的动作只要还带着这两个名字之一，就说明还有人按老位置读它。
    /// </summary>
    [Fact]
    public void How_often_the_screen_is_read_is_no_longer_a_field_of_a_step()
    {
        var carried = new List<string>();

        foreach (var action in ActionSemanticsCatalog.All.Where(action => action.LooksAtScreen))
        {
            foreach (var name in new[] { "frameMaxAgeMs", "intervalMs" })
            {
                if (action.Parameters.Any(parameter => parameter.Name == name))
                {
                    carried.Add($"{action.Key}: {name}");
                }
            }
        }

        Assert.Empty(carried);
    }
}
