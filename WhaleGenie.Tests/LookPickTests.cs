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
    /// 表单上列出已加入的对比色：序号、相对基准色的偏移、颜色，标题带上条数。列表读的是这一步
    /// 自己的值，值一变它就跟着变——它是那一个值的显示，不是另一份副本。
    /// </summary>
    [Fact]
    public void The_form_lists_the_compared_colours_the_step_holds()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("vision.findColors");
            Dispatcher.UIThread.RunJobs();

            // 还没有一处：标题只写名字，列表让位给一句说明。
            Assert.Empty(viewModel.PatternPoints);
            Assert.True(viewModel.PatternIsEmpty);
            Assert.Equal(Strings.Get("Add.Pattern"), viewModel.PatternTitle);

            viewModel.SetColourPoints(["1,0,#00FF00", "0, 1, #0000FF"]);

            Assert.Equal(Strings.Format("Add.PatternCount", 2), viewModel.PatternTitle);
            Assert.True(viewModel.HasPatternPoints);
            Assert.Equal(["1.", "2."], viewModel.PatternPoints.Select(row => row.Label));
            Assert.Equal(["(1, 0)", "(0, 1)"], viewModel.PatternPoints.Select(row => row.Offset));
            Assert.Equal(["#00FF00", "#0000FF"], viewModel.PatternPoints.Select(row => row.Colour));

            // 直接改这一格的值，列表同样跟上：它没有自己的存储。
            var points = viewModel.Parameters.First(parameter => parameter.Definition.Name == "points");
            points.Text = "2,2,#FF0000";
            Assert.Equal(["(2, 2)"], viewModel.PatternPoints.Select(row => row.Offset));

            window.Close();
        });
    }

    /// <summary>那一列真的在界面上：标题与每一行的偏移、颜色都画得出来。</summary>
    [Fact]
    public void The_compared_colours_are_drawn_on_the_form()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("vision.findColors");
            Dispatcher.UIThread.RunJobs();
            viewModel.SetColourPoints(["1,0,#00FF00", "0,1,#0000FF"]);
            Dispatcher.UIThread.RunJobs();

            var texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .Select(text => text.Text ?? string.Empty)
                .ToList();

            Assert.Contains(Strings.Format("Add.PatternCount", 2), texts);
            Assert.Contains("(1, 0)", texts);
            Assert.Contains("(0, 1)", texts);
            Assert.Contains("#00FF00", texts);
            Assert.Contains("#0000FF", texts);
            window.Close();
        });
    }

    /// <summary>
    /// 「周围要对的颜色」不再是表单上的一格：它由拾取写入。按钮改挂在基准色那一行，只有多点找色才有。
    /// </summary>
    [Fact]
    public void The_pattern_is_written_by_the_picker_and_has_no_field_of_its_own()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;

            viewModel.SelectAction("vision.findColors");
            Dispatcher.UIThread.RunJobs();

            // 表单里没有这一格，也不算缺项之外的东西：它仍是被校验的那个参数。
            var shown = viewModel.Rows.Concat(viewModel.AdvancedRows)
                .SelectMany(row => new[] { row.First, row.Second, row.Third })
                .OfType<StepParameterViewModel>()
                .Select(parameter => parameter.Definition.Name)
                .ToList();

            Assert.DoesNotContain("points", shown);
            Assert.Contains(viewModel.Parameters,
                parameter => parameter.Definition.Name == "points");

            // 这一节落在容差那一行：X、Y、位置、容差说的都是基准色，对比色排在它们之后。
            var rows = viewModel.Rows.Concat(viewModel.AdvancedRows).ToList();
            var pattern = rows.FindIndex(row => row.ShowsColourPointsPick);
            Assert.True(pattern >= 0, "对比色没有落在任何一行上");
            Assert.Equal("tolerance", rows[pattern].First.Definition.Name);

            foreach (var before in new[] { "color", "x", "y", "place" })
            {
                var at = rows.FindIndex(row => row.First.Definition.Name == before
                    || row.Second?.Definition.Name == before);
                Assert.True(at >= 0, $"{before} 不在表单上");
                Assert.True(at < pattern, $"对比色排到了 {before} 前面");
            }

            viewModel.SelectAction("vision.findColor");
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(viewModel.Rows.Concat(viewModel.AdvancedRows),
                row => row.ShowsColourPointsPick);
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
            Assert.Equal(2, menu!.Items.Count);

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

    /// <summary>滚轮键按一下就是新增一处：手不必离开画面。</summary>
    [Fact]
    public void The_middle_button_adds_a_point()
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
    /// 编辑框一行一个值，右边各带一个表达式按钮：偏移 X、偏移 Y、颜色各占一行，按钮才放得下、也对得准。
    /// </summary>
    [Fact]
    public void The_entry_window_gives_every_value_a_line_and_a_builder()
    {
        Ui.Run(() =>
        {
            var window = new PickEntryWindow("12,-4,#FF0000", [], false);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("12", window.FindControl<TextBox>("OffsetX")!.Text);
            Assert.Equal("-4", window.FindControl<TextBox>("OffsetY")!.Text);
            Assert.Equal("#FF0000", window.FindControl<TextBox>("Colour")!.Text);

            var builders = window.GetVisualDescendants().OfType<Button>()
                .Select(button => button.Tag)
                .OfType<string>()
                .ToList();

            Assert.Equal(["OffsetX", "OffsetY", "Colour"], builders);
            window.Close();
        });
    }

    /// <summary>
    /// 三行各占一行、互不重叠：挤在一起时表达式按钮就没有落脚的地方。
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
