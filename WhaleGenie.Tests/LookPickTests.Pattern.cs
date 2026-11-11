using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The colour pattern the step already holds: its rows, its file form and its field.
public partial class LookPickTests
{
    /// <summary>
    /// 拾取器带着这一步已有的那几处打开，交回的也是整张表：改了、删了、挪了都在这一扇窗里定下来，
    /// 表单里没有第二份需要对齐的副本。
    /// </summary>
    [Fact]
    public void The_picker_starts_from_the_pattern_the_step_already_holds()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("vision.findColors");
            Dispatcher.UIThread.RunJobs();
            viewModel.SetColourPoints(["0,1,#00FF00", "1,0,#FF0000"]);

            Assert.Equal(["0,1,#00FF00", "1,0,#FF0000"], viewModel.ColourPoints());

            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var picker = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10), [], viewModel.ColourPoints());
            picker.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["0,1,#00FF00", "1,0,#FF0000"], picker.Added);

            // 删掉一处再写回：这一步的图案就是窗里看到的那两条。
            picker.RemoveEntry(0);
            viewModel.SetColourPoints(picker.Added);
            Assert.Equal(["1,0,#FF0000"], viewModel.ColourPoints());
            picker.Close();
            window.Close();
        });
    }

    /// <summary>
    /// 图案是这一步自己的一个字段：一处一行，三格分别是往右、往下和颜色。表单里的行就是这个字段的
    /// 值，改一格就是改这一步，没有另一份副本要跟着改。
    /// </summary>
    [Fact]
    public void The_pattern_is_a_list_of_rows_the_step_keeps()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("vision.findColors");
            Dispatcher.UIThread.RunJobs();

            var points = viewModel.Parameters.First(parameter => parameter.Definition.Name == "points");
            Assert.True(points.IsColourPoints);
            Assert.Empty(points.ColourPointRows);

            // 一处都没写的时候这一步是缺项：图案至少要有一处和基准色一起出现，否则它找的就只是
            // 基准色本身，和「找色」没有区别。
            Assert.True(points.IsMissing);

            viewModel.SetColourPoints(["1,0,#00FF00", "0, 1, #0000FF"]);

            Assert.Equal(2, points.ColourPointRows.Count);
            Assert.Equal(["1", "0"], points.ColourPointRows.Select(row => row.Dx));
            Assert.Equal(["0", "1"], points.ColourPointRows.Select(row => row.Dy));
            Assert.Equal(["#00FF00", "#0000FF"], points.ColourPointRows.Select(row => row.Colour));
            Assert.False(points.IsMissing);

            // 拾取窗口读到的就是这些行：它读的和它写的是同一份。
            Assert.Equal(["1,0,#00FF00", "0,1,#0000FF"], viewModel.ColourPoints());

            points.RemoveColourPoint(points.ColourPointRows[0]);
            Assert.Equal(["0"], points.ColourPointRows.Select(row => row.Dx));

            window.Close();
        });
    }

    /// <summary>
    /// 图案存进宏文件时是一处一行，不是空字符串。这一条堵的是「界面能填、存下去就没了」：
    /// 行型字段要在写文件那一侧有一份，读回来才还是那几行，而不是跑起来才发现一处都没有。
    /// </summary>
    [Fact]
    public void The_pattern_survives_a_trip_through_the_macro_file()
    {
        var step = Ui.Run(() =>
        {
            var viewModel = new AddActionViewModel(ActionCatalog.Definitions, [], []);
            viewModel.SelectAction("vision.findColors");
            viewModel.SetColourPoints(["1,0,#00FF00", "0,1,#0000FF"]);

            // 第一处勾「必须不是」并写两个备选色，第二处自己带容差：留空的格子应当不带，
            // 写了/勾了的都要跟着走。
            var rows = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "points").ColourPointRows;
            rows[0].Colour = "#00FF00|#00FF01";
            rows[0].MustNot = true;
            rows[1].Tolerance = "20";
            return viewModel.BuildStep();
        });

        var written = step.ToJson().ToJsonString();
        var back = MacroStep.FromJson((System.Text.Json.Nodes.JsonObject)
            System.Text.Json.Nodes.JsonNode.Parse(written)!);

        var points = back.Parameters.Single(parameter => parameter.Name == "points");
        Assert.Equal(
            ["1,0,#00FF00|#00FF01,true,", "0,1,#0000FF,,20"],
            points.Rows.Select(row =>
                $"{row.Text("dx").Trim()},{row.Text("dy").Trim()},{row.Text("colour").Trim()}," +
                $"{row.Text("mustNot").Trim()},{row.Text("tolerance").Trim()}"));
    }

    /// <summary>
    /// 一处都还没有的时候，拾取窗口仍然要能开：它先要量出基准色在哪，而那件事不需要图案。
    /// 这一眼按「找色」看，图案带着的话就还是这一步自己。
    /// </summary>
    [Fact]
    public void The_picker_looks_for_the_base_colour_when_the_pattern_is_empty()
    {
        ExecutableStep Step(string type, bool withPoints) => new()
        {
            Type = type,
            Parameters = withPoints
                ? [Fields.Text("color", "#FF0000"),
                   new()
                   {
                       Name = "points",
                       Rows =
                       [
                           Cells.Of(("dx", "1"), ("dy", "0"), ("colour", "#00FF00")),
                       ],
                   }]
                : [Fields.Text("color", "#FF0000")],
        };

        var empty = ActionEditorView.WhatThePickerLooksFor(Step("vision.findColors", false));
        Assert.Equal("vision.findColor", empty.Type);
        Assert.DoesNotContain(empty.Parameters, parameter => parameter.Name == "points");
        Assert.Equal("#FF0000", empty.Text("color"));

        // 已经有了几处，就按这一步本来的样子看：图案是它的一部分，不该被掉包。
        var filled = ActionEditorView.WhatThePickerLooksFor(Step("vision.findColors", true));
        Assert.Equal("vision.findColors", filled.Type);
        Assert.Single(filled.Rows("points"));
    }

    /// <summary>三格真的在界面上：两列数字和一格颜色各有自己的框，还有把一处拿掉的按钮。</summary>
    [Fact]
    public void The_pattern_rows_are_drawn_as_three_cells()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("vision.findColors");
            viewModel.SetColourPoints(["1,0,#00FF00"]);
            Dispatcher.UIThread.RunJobs();

            var texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .Select(text => text.Text ?? string.Empty)
                .ToList();

            Assert.Contains(Strings.Get("Add.ColourPointDx"), texts);
            Assert.Contains(Strings.Get("Add.ColourPointDy"), texts);
            Assert.Contains(Strings.Get("Add.ColourPointColour"), texts);
            Assert.Contains(Strings.Get("Add.ColourPointAdd"), texts);

            var written = window.GetVisualDescendants().OfType<TextBox>()
                .Where(box => box.IsEffectivelyVisible)
                .Select(box => box.Text ?? string.Empty)
                .ToList();

            Assert.Contains("#00FF00", written);
            window.Close();
        });
    }

    /// <summary>
    /// 「周围要对的颜色」是表单上的一格，不再是靠拾取窗口旁敲侧击写进去的：窗口改的就是这一格，
    /// 两边读写同一份。它排在颜色与容差之后，因为那两处偏移是从基准色数起的。
    /// </summary>
    [Fact]
    public void The_pattern_has_a_field_of_its_own_and_the_picker_writes_into_it()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;

            viewModel.SelectAction("vision.findColors");
            Dispatcher.UIThread.RunJobs();

            var rows = viewModel.Rows.Concat(viewModel.AdvancedRows).ToList();
            var shown = rows.SelectMany(row => new[] { row.First, row.Second, row.Third })
                .OfType<StepParameterViewModel>()
                .Select(parameter => parameter.Definition.Name)
                .ToList();

            Assert.Contains("points", shown);

            var pattern = rows.FindIndex(row => row.First.Definition.Name == "points"
                || row.Second?.Definition.Name == "points");
            Assert.True(pattern >= 0, "图案没有落在任何一行上");

            foreach (var before in new[] { "color", "tolerance" })
            {
                var at = rows.FindIndex(row => row.First.Definition.Name == before
                    || row.Second?.Definition.Name == before);
                Assert.True(at >= 0, $"{before} 不在表单上");
                Assert.True(pattern > at, $"图案排到了 {before} 前面");
            }

            // 拾取窗口写回来的是行，不是一行标点。
            viewModel.SetColourPoints(["2,2,#FF0000"]);
            var points = viewModel.Parameters.First(parameter => parameter.Definition.Name == "points");
            Assert.Equal("#FF0000", Assert.Single(points.ColourPointRows).Colour);

            viewModel.SelectAction("vision.findColor");
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(viewModel.Rows.Concat(viewModel.AdvancedRows),
                row => row.First.IsColourPoints || row.Second?.IsColourPoints == true);
            window.Close();
        });
    }
}
