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
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Picking on the picture the step itself saw, rather than on the reference picture it was looking
/// for: an offset is counted from the middle of what it found, and a pattern's colour is read out
/// of that same picture. Both are arithmetic nobody should be doing in their head.
/// </summary>
public class LookPickTests
{
    /// <summary>A small picture, filled with one colour and dotted with others where told.</summary>
    private static ImageFrame Picture(PixelColor fill, params (int X, int Y, PixelColor Colour)[] spots)
        => Picture(20, 20, fill, spots);

    private static ImageFrame Picture(int width, int height, PixelColor fill,
        params (int X, int Y, PixelColor Colour)[] spots)
    {
        var bytes = new byte[width * height * 4];
        for (var at = 0; at < bytes.Length; at += 4)
        {
            bytes[at] = fill.B;
            bytes[at + 1] = fill.G;
            bytes[at + 2] = fill.R;
            bytes[at + 3] = 255;
        }

        foreach (var (x, y, colour) in spots)
        {
            var at = ((y * width) + x) * 4;
            bytes[at] = colour.B;
            bytes[at + 1] = colour.G;
            bytes[at + 2] = colour.R;
            bytes[at + 3] = 255;
        }

        return new ImageFrame(width, height, bytes);
    }

    private static StepLook Look(ImageFrame frame, ScreenPoint origin, ImageMatch? hit)
        => new()
        {
            StepId = "p1c2",
            StepType = "vision.clickImage",
            Kind = LookKind.Template,
            Frame = frame,
            Origin = origin,
            Boxes = hit is null ? [] : [new LookBox(hit, LookRole.Hit)],
            ChosenIndex = hit is null ? 0 : 1,
        };

    /// <summary>
    /// 图上的一点加画面的原点，就是屏幕上的那一点：拾取器底下那张图是这一步看的整块画面，
    /// 而画面自己的左上角并不在屏幕的 (0, 0)。
    /// </summary>
    [Fact]
    public void A_place_on_the_picture_is_a_place_on_the_screen()
    {
        Ui.Run(() =>
        {
            var look = Look(Picture(new PixelColor(0x10, 0x20, 0x30)), new ScreenPoint(100, 200),
                new ImageMatch(0.95, new ScreenPoint(10, 10), new ScreenSize(10, 10)));
            var window = new LookPickWindow(look, look.Frame, look.Origin, LookPickMode.Offset,
                new ScreenPoint(115, 215));

            Assert.Equal(new ScreenPoint(105, 206), window.PointAt(new ScreenPoint(5, 6)));

            // 同一个点相对命中中心的偏移：中心是 (115, 215)，所以往左 10、往上 9。
            Assert.Equal(new ScreenPoint(-10, -9),
                window.OffsetAt(window.PointAt(new ScreenPoint(5, 6))));
            window.Close();
        });
    }

    /// <summary>点在命中中心上，偏移就是 0：这正是"点中间"的那个答案，也是不补偏移时的落点。</summary>
    [Fact]
    public void The_point_picked_is_counted_from_the_middle_of_what_was_found()
    {
        Ui.Run(() =>
        {
            var look = Look(Picture(new PixelColor(0x10, 0x20, 0x30)), new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, look.Frame, look.Origin, LookPickMode.Offset,
                new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(10, 10));
            Assert.Equal(Strings.Format("LookPick.OffsetAt", 0, 0), Readout(window));

            window.Pick(new ScreenPoint(14, 3));
            Assert.Equal(Strings.Format("LookPick.OffsetAt", 4, -7), Readout(window));
            window.Close();
        });
    }

    /// <summary>
    /// 颜色从这张图上读，不从"现在的屏幕"上读：写进宏的是这一步看的那一刻，屏幕早就变了。
    /// 同一点再点一次不会多出一条。
    /// </summary>
    [Fact]
    public void Picking_colours_adds_one_line_each_and_nothing_twice()
    {
        Ui.Run(() =>
        {
            var frame = Picture(new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, look.Frame, look.Origin, LookPickMode.Colours,
                new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));
            window.Pick(new ScreenPoint(5, 6));
            window.Pick(new ScreenPoint(6, 6));

            Assert.Equal(["-5,-4,#AABBCC", "-4,-4,#102030"], window.Added);
            window.Close();
        });
    }

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
                ? [new() { Name = "color", Text = "#FF0000" },
                   new()
                   {
                       Name = "points",
                       Rows =
                       [
                           new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                           {
                               ["dx"] = "1", ["dy"] = "0", ["colour"] = "#00FF00",
                           },
                       ],
                   }]
                : [new() { Name = "color", Text = "#FF0000" }],
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

    /// <summary>
    /// 可点的画面是这一步读的整块地方，不是它搜索的那一小块：点在命中框外面照样能落到屏幕上，
    /// 这正是"往旁边挪一点"要的那一下。
    /// </summary>
    [Fact]
    public void The_whole_area_can_be_picked_on_not_only_the_part_that_was_searched()
    {
        Ui.Run(() =>
        {
            var area = Picture(60, 40, new PixelColor(0x10, 0x20, 0x30),
                (55, 35, new PixelColor(0xAA, 0xBB, 0xCC)));
            var look = Look(Picture(20, 20, new PixelColor(0x10, 0x20, 0x30)),
                new ScreenPoint(10, 10),
                new ImageMatch(0.95, new ScreenPoint(10, 10), new ScreenSize(10, 10)));

            var window = new LookPickWindow(look, area, new ScreenPoint(100, 200),
                LookPickMode.Offset, new ScreenPoint(115, 215));

            // 画面的右下角远在命中框之外，仍然是一个可以点的屏幕位置。
            Assert.Equal(new ScreenPoint(160, 240), window.PointAt(new ScreenPoint(60, 40)));
            Assert.Equal(new ScreenPoint(45, 25),
                window.OffsetAt(window.PointAt(new ScreenPoint(60, 40))));
            window.Close();
        });
    }

    /// <summary>
    /// 一条已经加进去的线要能改：选中它、在图上点一下就挪过去；也可以在框里直接改写。
    /// 改完列表跟着变，条数不变。
    /// </summary>
    [Fact]
    public void A_picked_line_can_be_moved_and_retyped()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)),
                (7, 8, new PixelColor(0x11, 0x22, 0x33)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));
            window.Pick(new ScreenPoint(7, 8));

            // 选中第一条，再点画面：它挪到那里，第二条不动。
            window.PickLine(0);
            window.Pick(new ScreenPoint(7, 8));

            Assert.Equal(["-3,-2,#112233", "-3,-2,#112233"], window.Added);

            // 框里改：算式也算得出来。
            window.ApplyEntry(0, "12+3,-4,#FF0000");
            Assert.Equal(["15,-4,#FF0000", "-3,-2,#112233"], window.Added);

            // 删掉一条，剩下的往前挪一格。
            window.RemoveEntry(1);
            Assert.Equal(["15,-4,#FF0000"], window.Added);
            window.Close();
        });
    }

    /// <summary>框里写的三种值：算式、负数、大小写不同的颜色，读出来的都是同一种写法。</summary>
    /// <summary>
    /// 一处可以写几种颜色（竖线分开），后面还可以跟着表里的那两列：不能是、单点容差。
    /// 拾取窗口原样把这两列带回来，所以在它里面改一条不会悄悄把表格里写的东西抹掉。
    /// </summary>
    [Theory]
    [InlineData("1,2,#ff0000|#00ff00", "1,2,#FF0000|#00FF00")]
    [InlineData("1,2,#ff0000|#00ff00,1,10", "1,2,#FF0000|#00FF00,1,10")]
    [InlineData("1,2,#FF0000,,12", "1,2,#FF0000,,12")]
    public void A_line_may_hold_several_colours_and_the_two_columns_behind_them(
        string written, string expected)
    {
        Assert.True(ColourPointLine.TryParse(written, out var entry));
        Assert.Equal(expected, entry);
    }

    [Theory]
    [InlineData("12+3,-4,#ff0000", "15,-4,#FF0000")]
    [InlineData(" 1 , 2 , #AABBCC ", "1,2,#AABBCC")]
    [InlineData("(2+3)*2,-(1+1),#00ff00", "10,-2,#00FF00")]
    // 变量原样留下：它值多少要等宏跑起来才知道。
    [InlineData("$dx,$dy,$side", "$dx,$dy,$side")]
    public void A_line_is_read_as_numbers_and_one_colour(string written, string expected)
    {
        Assert.True(ColourPointLine.TryParse(written, out var entry));
        Assert.Equal(expected, entry);
    }

    [Theory]
    [InlineData("12,-4")]
    [InlineData("12,-4,red")]
    [InlineData("1,2,#12345")]
    [InlineData("1,2,#GGGGGG")]
    [InlineData("1+,2,#FFFFFF")]
    public void A_line_that_is_not_two_numbers_and_a_colour_is_refused(string written)
        => Assert.False(ColourPointLine.TryParse(written, out _));

    /// <summary>
    /// 整屏铺进一扇窗里时，一个像素只有零点几个点宽：滚轮把画面放大，才瞄得到。
    /// </summary>
    [Fact]
    public void The_wheel_makes_the_picture_bigger_to_aim_at()
    {
        Ui.Run(() =>
        {
            var area = Picture(200, 120, new PixelColor(0x10, 0x20, 0x30));
            var look = Look(area, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(10, 10), new ScreenSize(8, 8)));
            var window = new LookPickWindow(look, area, new ScreenPoint(0, 0),
                LookPickMode.Offset, new ScreenPoint(14, 14));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var before = window.ZoomLevel;
            window.MouseWheel(new Point(200, 300), new Vector(0, 1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.ZoomLevel > before,
                $"滚轮之后还是 {window.ZoomLevel:0.00}，放大前是 {before:0.00}");
            window.Close();
        });
    }

    /// <summary>
    /// 按住图上的圆点拖一下，就是把那一条挪到放手的地方：不用先选中、再点一次。
    /// </summary>
    [Fact]
    public void Dragging_a_marker_moves_that_line()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)),
                (7, 8, new PixelColor(0x11, 0x22, 0x33)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));
            window.Pick(new ScreenPoint(7, 8));

            var board = window.FindControl<Canvas>("Board")!;
            var zoom = window.ZoomLevel;
            var from = board.TranslatePoint(new Point(5 * zoom, 6 * zoom), window)!.Value;
            var to = board.TranslatePoint(new Point(7 * zoom, 8 * zoom), window)!.Value;

            window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();

            // 第一条被拖到第二条那一处，条数没变。
            Assert.Equal(2, window.Added.Count);
            Assert.Equal(window.Added[1], window.Added[0]);
            window.Close();
        });
    }

    /// <summary>
    /// 取消选中之后，再点画面就是加一条新的：空白处那一下确实是"放下这条"。
    /// </summary>
    [Fact]
    public void Dropping_the_selection_makes_the_next_click_add_another_line()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)),
                (7, 8, new PixelColor(0x11, 0x22, 0x33)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));
            window.PickLine(0);
            window.ClearSelection();
            window.Pick(new ScreenPoint(7, 8));

            Assert.Equal(2, window.Added.Count);
            window.Close();
        });
    }

    /// <summary>
    /// 图上的圆点带序号，和右侧列表对得上：靠数一串同色的圆点是认不出哪条是哪条的。
    /// </summary>
    [Fact]
    public void Every_marker_carries_the_number_of_its_line()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)),
                (7, 8, new PixelColor(0x11, 0x22, 0x33)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));
            window.Pick(new ScreenPoint(7, 8));

            var numbers = window.FindControl<Canvas>("Board")!.Children
                .OfType<TextBlock>().Where(text => text.Name == "MarkerNumber")
                .Select(text => text.Text).ToList();

            Assert.Equal(["1", "2"], numbers);
            window.Close();
        });
    }

    /// <summary>
    /// 右键属于图上的点：点上有菜单（编辑、删除），空白处什么也不做——画面上的右键若是"再加一条"，
    /// 想改一条时就会先多出一条。
    /// </summary>
    [Fact]
    public void The_right_button_belongs_to_the_dot_under_it()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));

            var board = window.FindControl<Canvas>("Board")!;
            var zoom = window.ZoomLevel;
            var onDot = board.TranslatePoint(new Point(5 * zoom, 6 * zoom), window)!.Value;
            var onEmpty = board.TranslatePoint(new Point(15 * zoom, 15 * zoom), window)!.Value;

            var menu = window.MenuFor(new Point(5 * zoom, 6 * zoom));
            Assert.NotNull(menu);
            // 改、加备选色、删：一处能做的三件事都在这一份菜单里。
            Assert.Equal(3, menu!.Items.Count);

            window.MouseDown(onEmpty, MouseButton.Right, RawInputModifiers.RightMouseButton);
            window.MouseUp(onEmpty, MouseButton.Right, RawInputModifiers.RightMouseButton);
            Dispatcher.UIThread.RunJobs();

            // 空白处那一下既不加条，也不开菜单。
            Assert.Single(window.Added);
            Assert.Null(window.MenuFor(new Point(15 * zoom, 15 * zoom)));

            // 点在圆点上按右键也不加条。
            window.MouseDown(onDot, MouseButton.Right, RawInputModifiers.RightMouseButton);
            window.MouseUp(onDot, MouseButton.Right, RawInputModifiers.RightMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(window.Added);
            window.Close();
        });
    }

    /// <summary>
    /// 滚轮键不再新增一处：中键常被当成拖动画面的手势，手一滑就多出一条，而多出来的那条
    /// 不点开列表是看不出来的。只有左键是「点一处」。
    /// </summary>
    [Fact]
    public void The_middle_button_no_longer_adds_a_point()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)),
                (7, 8, new PixelColor(0x11, 0x22, 0x33)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var board = window.FindControl<Canvas>("Board")!;
            var zoom = window.ZoomLevel;
            var at = board.TranslatePoint(new Point(7 * zoom, 8 * zoom), window)!.Value;

            window.MouseDown(at, MouseButton.Middle, RawInputModifiers.MiddleMouseButton);
            window.MouseUp(at, MouseButton.Middle, RawInputModifiers.MiddleMouseButton);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(window.Added);

            // 左键照旧：点在同一个像素上就是一整条。
            window.Pick(new ScreenPoint(7, 8));
            Assert.Equal(["-3,-2,#112233"], window.Added);
            window.Close();
        });
    }

    /// <summary>
    /// 不点画面也能添一条：给出数字与颜色即可。新条目接在末尾，同一处不写两遍；有数字时圆点落在
    /// 偏移所指的地方，没有数字（算式）时留在命中中心。
    /// </summary>
    [Fact]
    public void A_line_can_be_added_by_typing_it()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.AppendEntry("2,3,#AABBCC");
            window.AppendEntry("2, 3, #aabbcc");
            window.AppendEntry("$dx,$dy,$side");

            Assert.Equal(["2,3,#AABBCC", "$dx,$dy,$side"], window.Added);
            Assert.Equal([new ScreenPoint(12, 13), new ScreenPoint(10, 10)], window.MarkerPlaces);
            window.Close();
        });
    }

    /// <summary>
    /// 一处可以有几种颜色：同一枚按钮换一套配色画出来，位置没变、颜色变了，那是「一处可以是这样
    /// 也可以是那样」，不是两处。加的时候位置不动，颜色写在竖线后面；同一个颜色不会写第二遍。
    /// </summary>
    [Fact]
    public void A_place_may_be_satisfied_by_more_than_one_colour()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30),
                (5, 6, new PixelColor(0xAA, 0xBB, 0xCC)));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));
            window.AddAlternative(0, "#112233");
            Assert.Equal(["-5,-4,#AABBCC|#112233"], window.Added);

            // 同一个颜色加第二遍：一处只留一份。
            window.AddAlternative(0, "#aabbcc");
            Assert.Equal(["-5,-4,#AABBCC|#112233"], window.Added);

            // 位置没动，圆点也没动：加的是颜色，不是另一处。
            Assert.Equal([new ScreenPoint(5, 6)], window.MarkerPlaces);

            // 再点画面是另一处，不是把颜色加到刚加的那一处上。
            window.Pick(new ScreenPoint(7, 8));
            Assert.Equal(2, window.Added.Count);
            window.Close();
        });
    }

    /// <summary>一处最多几种颜色：多到没有意义时就说清楚，而不是一直往里加。</summary>
    [Fact]
    public void A_place_says_when_it_can_hold_no_more_colours()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Pick(new ScreenPoint(5, 6));
            for (var number = 0; number < 12; number++)
            {
                window.AddAlternative(0, $"#{number:X6}");
            }

            Assert.Equal(ColourPointLine.MostColours, window.Added[0].Split('|').Length);
            window.Close();
        });
    }

    /// <summary>一条里的一处可以是函数算出来的：括号里的逗号不是分隔符。</summary>
    [Theory]
    [InlineData("max(1,3),0,#FFFFFF", "max(1,3),0,#FFFFFF")]
    [InlineData("min(2, 4),-1,#00ff00", "min(2, 4),-1,#00FF00")]
    public void A_function_in_a_line_keeps_its_own_commas(string written, string expected)
    {
        Assert.True(ColourPointLine.TryParse(written, out var entry));
        Assert.Equal(expected, entry);
    }

    /// <summary>
    /// 编辑框一行一个值：偏移 X、偏移 Y、颜色各占一行。这里是手填一个点，三格写的就是数字和颜色，
    /// 所以一格都不带表达式按钮——算式仍然可以直接打进去，引擎照读。
    /// </summary>
    [Fact]
    public void The_entry_window_gives_every_value_a_line_of_its_own()
    {
        Ui.Run(() =>
        {
            var window = new PickEntryWindow("12,-4,#FF0000", [], false);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("12", window.FindControl<TextBox>("OffsetX")!.Text);
            Assert.Equal("-4", window.FindControl<TextBox>("OffsetY")!.Text);
            Assert.Equal("#FF0000", window.FindControl<TextBox>("Colour")!.Text);

            // 三格旁边没有表达式按钮：这一窗只填一个点，写算式的能力留在框里，不占一排空间。
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
                button => button.Tag is "OffsetX" or "OffsetY" or "Colour");
            window.Close();
        });
    }

    /// <summary>
    /// 三行各占一行、互不重叠：挤在一起时数值就分不清是哪一行的。
    /// </summary>
    [Fact]
    public void The_entry_window_rows_stand_one_above_the_next()
    {
        Ui.Run(() =>
        {
            var window = new PickEntryWindow("1,2,#FFFFFF", [], false);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var rows = new[] { "OffsetX", "OffsetY", "Colour" }
                .Select(name => window.FindControl<TextBox>(name)!)
                .Select(box => box.TranslatePoint(new Point(0, 0), window)!.Value)
                .ToList();

            for (var index = 1; index < rows.Count; index++)
            {
                Assert.True(rows[index].Y > rows[index - 1].Y + 24,
                    $"第 {index + 1} 行与上一行重叠：{rows[index - 1].Y:0.#} → {rows[index].Y:0.#}");
            }

            window.Close();
        });
    }

    /// <summary>
    /// 还没有一条时，那一栏也要看得出一张列表：标题、加号，和一句"尚未加入"；加进一条后那句话让位。
    /// </summary>
    [Fact]
    public void The_list_shows_what_it_is_before_anything_is_in_it()
    {
        Ui.Run(() =>
        {
            var frame = Picture(20, 20, new PixelColor(0x10, 0x20, 0x30));
            var look = Look(frame, new ScreenPoint(0, 0),
                new ImageMatch(0.95, new ScreenPoint(0, 0), new ScreenSize(20, 20)));
            var window = new LookPickWindow(look, frame, new ScreenPoint(0, 0),
                LookPickMode.Colours, new ScreenPoint(10, 10));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var entries = window.FindControl<StackPanel>("Entries")!;
            var hint = Strings.Get("LookPick.NothingAdded");

            Assert.True(window.FindControl<Border>("EntriesPanel")!.IsVisible);
            Assert.True(window.FindControl<Button>("AddEntry")!.IsVisible);
            Assert.Contains(entries.Children.OfType<TextBlock>(), text => text.Text == hint);

            window.AppendEntry("1,1,#FFFFFF");
            Assert.DoesNotContain(entries.Children.OfType<TextBlock>(), text => text.Text == hint);
            window.Close();
        });
    }

    private static string Readout(LookPickWindow window)
        => window.FindControl<TextBlock>("Readout")?.Text ?? string.Empty;
}
