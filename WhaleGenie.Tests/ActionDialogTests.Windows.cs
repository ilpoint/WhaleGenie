using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Key = Avalonia.Input.Key;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace WhaleGenie.Tests;

// Windows, elements and coordinates: which window a step means and where it counts from.
public partial class ActionDialogTests
{
    /// <summary>
    /// 桌面元素也能说「还要像什么样」，用的是与网页同一张行表，只是能问的事不一样：看不看得见、
    /// 能不能用、名字里有没有一段字。网页那两颗「从页面选取」「看看效果」在桌面元素这一族不画，
    /// 因为没有页面可以看。
    /// </summary>
    [Fact]
    public void A_desktop_element_can_say_what_else_it_has_to_be_like()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("uia.click");
            var narrow = Field(viewModel, "narrow")!;

            Assert.True(narrow.IsNarrow);
            Assert.Equal(NarrowFamily.Element, narrow.Definition.NarrowFamily);
            Assert.Empty(narrow.NarrowRows);
            Assert.False(narrow.IsIncluded);

            narrow.AddNarrowRow();
            var row = Assert.Single(narrow.NarrowRows);

            Assert.False(row.OnPage);
            Assert.Equal(
                ["Visible", "Enabled", "NameHas", "NameHasNot"],
                row.Choices.Select(choice => choice.Value));

            // 看得见与能用都不需要再填一段字，名字那两条才要。
            Assert.False(row.NeedsValue);
            Assert.Equal([new NarrowCondition("Visible", string.Empty)], narrow.Conditions());

            row.Way = row.Choices[2];
            row.Value = "保存";
            Assert.True(row.NeedsValue);
            Assert.Equal([new NarrowCondition("NameHas", "保存")], narrow.Conditions());

            var written = Assert.Single(narrow.ToStepParameter().Rows);
            Assert.Equal("NameHas", written.Text("how"));
            Assert.Equal("保存", written.Text("value"));
        });
    }

    /// <summary>
    /// 识别文字的"搜索范围"与找色、找字那几个动作是同一个结构：一处一行，四个数、或者一个变量，
    /// 行末可以删掉，下面可以再加一处。从前它是四个独立数字，与别处对不上，也没有"再找一处"。
    /// </summary>
    [Fact]
    public void The_search_region_of_a_reading_step_is_a_list_of_places()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("ocr.recognize");
            var region = Field(viewModel, "region")!;

            Assert.True(region.IsRegion);
            Assert.Empty(region.Regions);
            Assert.DoesNotContain(viewModel.Rows, line => line.IsQuad);

            // 一处：四个数，或者一个变量说出了整块矩形。
            region.AddRegion(new RegionRowViewModel());
            Assert.Single(region.Regions);
            Assert.True(region.HasRegions);

            region.Regions[0].OrVariable = "shot";
            Assert.Equal("shot", region.Regions[0].OrVariable);
        });
    }

    /// <summary>
    /// 一个矩形的四个数站在同一行：左上角、宽、高。原来两两一行，每个数字底下还各带一段说明，
    /// 四个数占掉一整屏；现在与"搜索区域"那一行同一个样子，短名、框、ƒ，说明在提示里。
    /// 格子里写的东西要真的回到步骤的参数上，反过来也一样。
    /// </summary>
    [Fact]
    public void The_four_numbers_of_a_rectangle_share_one_line()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.capture");

            var row = Assert.Single(viewModel.Rows, line => line.IsQuad);
            Assert.Equal(["x", "y", "width", "height"],
                new[] { row.First, row.Second!, row.Third!, row.Fourth! }
                    .Select(parameter => parameter.Definition.Name));
            Assert.Equal(4, row.QuadCells.Count);
            Assert.Equal(["X", "Y",
                    Strings.Get("Add.RegionWidthShort"), Strings.Get("Add.RegionHeightShort")],
                row.QuadCells.Select(cell => cell.ShortLabel));

            // 格子里写一个数，参数跟着变；参数改了，格子也跟着变。
            row.QuadCells[0].Write("120");
            Assert.Equal("120", row.First.CurrentText);
            Assert.Same(row.First, Field(viewModel, "x"));

            Field(viewModel, "width")!.SetNumber(80);
            Assert.Equal("80", row.QuadCells[2].Text);
        });
    }

    /// <summary>
    /// 窗口那一格说得清自己现在是哪一种：没选就是前台窗口，选了「沿用上一次」就把这句话写出来，
    /// 而不是显示一串没有标题的括号。
    /// </summary>
    [Fact]
    public void A_window_field_says_which_window_it_stands_for()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var window = viewModel.Parameters.First(parameter => parameter.Definition.Name == "window");

            Assert.False(window.HasWindowRule);
            Assert.False(window.IsFollowingWindow);
            Assert.Equal(Strings.Get("WindowRule.NotSet"), window.WindowRuleSummary);

            window.FollowWindowRule();
            Assert.True(window.IsFollowingWindow);
            Assert.Equal(Strings.Get("WindowRule.Following"), window.WindowRuleSummary);
            Assert.Equal(WindowRule.FollowPrevious.ToJson().ToJsonString(), window.CurrentText);

            window.ApplyWindowRule(new WindowRule(Title: "Notepad"));
            Assert.False(window.IsFollowingWindow);
            Assert.Contains("Notepad", window.WindowRuleSummary, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// 落点之上已经有人指定过窗口时，新写的这一步默认就跟着它走：在同一个窗口里做十件事，前面九次
    /// 不必各自再绑一遍。上面一个窗口都没提过时不替人做这个决定，空着就是前台窗口。
    /// </summary>
    [Fact]
    public void A_new_step_starts_on_the_window_the_steps_above_it_use()
    {
        Ui.Run(() =>
        {
            var (_, following) = OpenWindow("vision.findImage", followsWindowAbove: true);
            var window = following.Parameters.First(parameter => parameter.Definition.Name == "window");

            Assert.True(window.IsFollowingWindow);
            Assert.Single(following.WindowRules);
            Assert.Contains("Notepad", following.WindowRules[0].Summary, StringComparison.Ordinal);

            // 摘要说得出它现在会跟到哪个窗口，而不是只说一句"沿用上一次"。
            Assert.Contains("Notepad", window.WindowRuleSummary, StringComparison.Ordinal);

            var (_, plain) = OpenWindow("vision.findImage");
            var empty = plain.Parameters.First(parameter => parameter.Definition.Name == "window");
            Assert.False(empty.HasWindowRule);
            Assert.False(empty.IsFollowingWindow);
        });
    }

    /// <summary>
    /// 表单拿到的"本宏用过的窗口"就是窗口菜单里列出来的那几条：摘要、原文、用了几处，同一条规则
    /// 只列一次。
    /// </summary>
    [Fact]
    public void The_window_menu_lists_what_the_macro_already_uses()
    {
        Ui.Run(() =>
        {
            var notepad = new WindowRule(Title: "Notepad").ToJson().ToJsonString();
            var calc = new WindowRule(Title: "Calculator").ToJson().ToJsonString();
            var steps = new List<MacroStep>
            {
                OnWindow(notepad),
                OnWindow(calc),
                OnWindow(notepad),
            };

            var used = WindowRuleChoices.Of(steps);

            Assert.Equal(2, used.Count);
            Assert.Equal(2, used[0].Count);
            Assert.Equal(1, used[1].Count);
            Assert.Equal(notepad, used[0].Rule);
            Assert.Contains("Notepad", used[0].Summary, StringComparison.Ordinal);

            var (_, viewModel) = OpenWindow("vision.findImage", used);
            Assert.Equal(2, viewModel.WindowRules.Count);
        });
    }

    /// <summary>
    /// 选窗口那颗按钮打开的就是这张菜单：勾在现在这一种上，列出本宏用过的窗口与此刻打开着的窗口，
    /// 点一条就把那段规则写进这一格。不必再按 F1 对准窗口点一下。
    /// </summary>
    [Fact]
    public void The_window_menu_offers_what_the_macro_and_the_desk_have()
    {
        Ui.Run(() =>
        {
            var notepad = new WindowRule(Title: "Notepad").ToJson().ToJsonString();
            var (window, viewModel) = OpenWindow("vision.findImage",
                WindowRuleChoices.Of([OnWindow(notepad)]), followsWindowAbove: true);

            var view = window.GetVisualDescendants().OfType<ActionEditorView>().First();
            view.Devices = new ChecksDevices
            {
                Windows = new WindowsForChecks().Add(7, "Calculator", "calc", "CalcFrame"),
            };

            // 窗口那一格只在「识别方式 = 窗口」时露面，这也是它对这一步的含义。
            Choose(viewModel, "anchorMode", "window");
            Dispatcher.UIThread.RunJobs();

            var field = viewModel.Parameters.First(parameter => parameter.Definition.Name == "window");
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(),
                button => button.IsEffectivelyVisible
                    && button.DataContext == field
                    && button.Content as string == Strings.Get("WindowRule.Choose"));

            var menu = view.BuildWindowMenu(field);
            var items = menu.Items.OfType<MenuItem>().ToList();

            Assert.Contains(items, item => (string?)item.Header == Strings.Get("WindowRule.Menu.Foreground"));
            Assert.Contains(items, item => (string?)item.Header == Strings.Get("WindowRule.Following")
                && item.IsChecked);

            var used = items.First(item => (string?)item.Header == Strings.Get("WindowRule.Menu.Used"));
            Assert.Single(used.Items.OfType<MenuItem>());

            var open = items.First(item => (string?)item.Header == Strings.Get("WindowRule.Menu.Open"));
            var row = Assert.Single(open.Items.OfType<MenuItem>());
            Assert.Contains("Calculator", (string)row.Header!, StringComparison.Ordinal);

            // 点一条就写进这一格：整条规则连程序名一起，改天标题变了也还能找回同一个窗口。
            row.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(field.IsFollowingWindow);
            Assert.True(WindowRule.TryParse(field.CurrentText, out var applied));
            Assert.Equal("Calculator", applied.Title);
            Assert.Equal("calc", applied.Process);
        });
    }

    /// <summary>
    /// 程序族问「哪一个程序」用的是与窗口同一格：菜单、F1 捕获、已打开的窗口列表都照旧，
    /// 只是标签写「程序」，而且必须答——窗口那格空着是"前台窗口"，程序那格空着是没答。
    /// </summary>
    [Fact]
    public void The_process_family_names_its_program_with_the_window_menu()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = OpenWindow("process.kill", WindowRuleChoices.Of([]));
            var view = window.GetVisualDescendants().OfType<ActionEditorView>().First();
            view.Devices = new ChecksDevices
            {
                Windows = new WindowsForChecks().Add(7, "Calculator", "calc", "CalcFrame"),
            };

            var field = Assert.Single(viewModel.Parameters,
                parameter => parameter.Definition.Name == "window");
            Assert.True(field.IsWindowRule);
            Assert.True(field.IsWindow);
            Assert.Equal("Program", field.Definition.Label);
            Assert.True(field.Definition.Required);

            field.ClearWindowRule();
            Assert.Equal(Strings.Get("WindowRule.NotChosen"), field.WindowRuleSummary);

            var items = view.BuildWindowMenu(field).Items.OfType<MenuItem>().ToList();
            Assert.Contains(items, item => (string?)item.Header == Strings.Get("WindowRule.Menu.Foreground"));
            Assert.Contains(items, item => (string?)item.Header == Strings.Get("WindowRule.Menu.Capture"));

            var open = items.First(item => (string?)item.Header == Strings.Get("WindowRule.Menu.Open"));
            Assert.Single(open.Items.OfType<MenuItem>())
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.True(WindowRule.TryParse(field.CurrentText, out var applied));
            Assert.Equal("Calculator", applied.Title);
        });
    }

    /// <summary>
    /// 新步骤跟着走的是"落点之上最近那一步"用的窗口：选中第二步时跟第一步，什么都没选（放在末尾）
    /// 时跟最后一步。判的是顺序，不是记住的上一个，所以插在中间得到的也是它上面那一条。
    /// </summary>
    [Fact]
    public void The_window_a_new_step_follows_is_the_last_one_above_the_landing_point()
    {
        var notepad = new WindowRule(Title: "Notepad").ToJson().ToJsonString();
        var calc = new WindowRule(Title: "Calculator").ToJson().ToJsonString();
        var steps = new List<MacroStep>
        {
            OnWindow(notepad, "first"),
            OnWindow(calc, "second"),
        };

        Assert.Equal(notepad, WindowRuleChoices.NearestAbove(steps, "second"));
        Assert.Equal(calc, WindowRuleChoices.NearestAbove(steps, null));
        Assert.Equal(string.Empty, WindowRuleChoices.NearestAbove([OnWindow(string.Empty)], null));
    }

    private static MacroStep OnWindow(string rule, string id = "") => new()
    {
        Id = id,
        Type = "vision.findImage",
        Parameters =
        [
            new StepParameter
            {
                Name = "window",
                Kind = ActionParameterKind.WindowRule,
                Written = Fields.Shape(rule),
            },
        ],
    };

    /// <summary>
    /// The switch that takes the place from the pointer stands on the line of the numbers it belongs
    /// to rather than taking a line of its own, and with it on the pair of numbers is not asked for:
    /// a step that works wherever the pointer is carries no coordinates for anything to read as a
    /// position of its own.
    /// </summary>
    [Fact]
    public void A_click_can_be_told_to_work_at_the_pointer()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            var row = viewModel.Rows.First(candidate =>
                candidate.HasThird && candidate.Third!.Definition.Name == "useCursor");

            // 它跟着「点哪儿」那一块：点不点在指针当前位置，问的是位置，不是按哪个键。
            Assert.Equal("where", row.First.Group);

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;

            row.Third!.Flag = true;
            Dispatcher.UIThread.RunJobs();
            viewModel.SaveCommand.Execute(null);

            Assert.Equal("true", saved!.Parameters
                .First(parameter => parameter.Name == "useCursor").Value);
            Assert.DoesNotContain(saved.Parameters, parameter => parameter.Name == "x");
            Assert.DoesNotContain(saved.Parameters, parameter => parameter.Name == "y");
        });
    }

    /// <summary>
    /// A scroll has no button to stand beside, so the switch rides the line of the two numbers it
    /// replaces; and what the numbers held is put away rather than thrown away — a step told again
    /// to work at a place keeps the place it was written with.
    /// </summary>
    [Fact]
    public void A_scroll_can_be_told_to_work_at_the_pointer_too()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseScroll");
            var x = viewModel.Parameters.First(parameter => parameter.Definition.Name == "x");
            var place = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "useCursor");
            var row = viewModel.Rows.First(candidate => candidate.First.Definition.Name == "x");

            Assert.True(row.HasThird);
            Assert.Same(place, row.Third);

            x.SetNumber(300);
            place.Flag = true;
            Dispatcher.UIThread.RunJobs();

            Assert.False(x.IsIncluded);
            Assert.False(row.IsApplicable);
            Assert.Equal("300", x.CurrentText);

            place.Flag = false;
            Dispatcher.UIThread.RunJobs();

            Assert.True(x.IsIncluded);
            Assert.True(row.IsApplicable);
            Assert.Equal("300", x.CurrentText);
        });
    }

    /// <summary>
    /// Ticking the switch takes the two numbers off the form as well as out of the step. A field
    /// still drawn while the step no longer writes it is worse than one that is not drawn: what it
    /// holds reads as the place the step acts on, and it is not.
    /// </summary>
    [Fact]
    public void Ticking_the_switch_takes_the_numbers_off_the_form()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction("input.mouseClick");
            Dispatcher.UIThread.RunJobs();

            var place = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "useCursor");
            var numbers = Line(window, "x");

            Assert.True(numbers.IsEffectivelyVisible);

            place.Flag = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(numbers.IsEffectivelyVisible);

            // A step told again to act on the numbers it was written with is drawn the way it was:
            // nothing was thrown away while they were off the form.
            place.Flag = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(numbers.IsEffectivelyVisible);
        });
    }

    /// <summary>The line of the form a field of the step is drawn on.</summary>
    private static Control Line(Window window, string name)
        => window.GetVisualDescendants().OfType<Grid>().First(grid =>
            grid.DataContext is ParameterRowViewModel row
            && row.First.Definition.Name == name);

    /// <summary>
    /// A step told to work at the pointer opens the way it was saved: the switch is on, the numbers
    /// it does not write are not drawn, and what the step will be written as says the same.
    /// </summary>
    [Fact]
    public void A_step_told_to_work_at_the_pointer_opens_that_way()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseClick");
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "x").SetNumber(120);
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "y").SetNumber(240);
            viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "useCursor").Flag = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("\"useCursor\": true", viewModel.JsonPreview, StringComparison.Ordinal);
            Assert.DoesNotContain("\"x\"", viewModel.JsonPreview, StringComparison.Ordinal);

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            var reopened = new ActionEditorWindow(saved, ActionCatalog.Definitions, [], []);
            reopened.Show();
            Dispatcher.UIThread.RunJobs();

            var again = (AddActionViewModel)reopened.DataContext!;

            Assert.True(again.Parameters
                .First(parameter => parameter.Definition.Name == "useCursor").Flag);
            Assert.False(Shown(again, "x"));
            Assert.False(Shown(again, "y"));
        });
    }
}
