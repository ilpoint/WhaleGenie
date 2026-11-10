using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Dragging past a block, dropping onto the middle of one, and the mark that says where input goes.
public partial class StepTreeTests
{
    [Fact]
    public void A_section_title_picks_the_block_it_belongs_to()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var choice = Step("control.switch",
                new StepParameter { Name = "cases", Kind = ActionParameterKind.Steps },
                new StepParameter { Name = "otherwise", Kind = ActionParameterKind.Steps });
            editor.AddStep(choice);
            Dispatcher.UIThread.RunJobs();

            // A section title stands for the whole block: picking it picks the block, and the
            // highlight is put back on the block's own row because that is the row commands act on.
            var title = editor.Rows.First(row => row.IsSection);
            var item = window.FindControl<TreeView>("StepList")!.GetVisualDescendants().OfType<TreeViewItem>()
                .First(box => ReferenceEquals(box.DataContext, title));
            var point = item.TranslatePoint(new Point(item.Bounds.Width * 0.1, item.Bounds.Height / 2), window)
                ?? default;

            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(choice, Assert.Single(editor.SelectedSteps));

            var list = window.FindControl<TreeView>("StepList")!;
            Assert.Contains(list.SelectedItems!.OfType<StepRow>(),
                row => row.IsStep && ReferenceEquals(row.Step, choice));
        });
    }

    [Fact]
    public void A_mouse_drag_can_carry_a_step_past_a_whole_block()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var first = Step("control.delay");
            var loop = Loop(Step("control.log"));
            editor.AddStep(first);
            editor.AddStep(loop);
            Dispatcher.UIThread.RunJobs();

            // The loop's row, its body row and the step inside it, plus the step before it.
            var rows = window.FindControl<TreeView>("StepList")!.GetVisualDescendants().OfType<TreeViewItem>().ToList();
            Assert.Equal(4, rows.Count);

            var from = Waypoint(window, rows[0], 0.5, 0.5);

            // Below the whole tree is past everything, which is where this drag ends.
            var list = window.FindControl<TreeView>("StepList")!;
            var to = list.TranslatePoint(new Point(200, list.Bounds.Height - 4), window)!.Value;
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([loop, first], editor.Steps);
            Assert.Single(Inside(loop));
        });
    }

    /// <summary>A point inside a row, as a fraction of its width and height, in window space.</summary>
    private static Point Waypoint(Window window, Visual row, double x, double y)
        => row.TranslatePoint(new Point(row.Bounds.Width * x, row.Bounds.Height * y), window) ?? default;

    [Fact]
    public void A_mouse_drag_onto_the_middle_of_a_block_puts_the_step_inside_it()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var inside = Step("control.log");
            var loop = Loop(inside);
            var outside = Step("control.delay");
            editor.AddStep(loop);
            editor.AddStep(outside);
            Dispatcher.UIThread.RunJobs();

            // The middle of the block's own row, not of the container it shares with its children.
            var header = Header(window, row => row.IsStep && ReferenceEquals(row.Step, loop));
            var from = Waypoint(window, Header(window, row => ReferenceEquals(row.Step, outside)), 0.5, 0.5);
            var to = Waypoint(window, header, 0.5, 0.5);
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([inside, outside], Inside(loop));
            Assert.Same(loop, Assert.Single(editor.Steps));
        });
    }

    /// <summary>
    /// 步骤行上写着这一步的输入往哪儿送。手点没问题、写进宏却不动，是写宏最容易踩的坑，而三种送法
    /// 的代价各不一样：前台要求窗口在最前，后台消息要求窗口名字对，驱动级要求装了驱动、开着服务端。
    /// 所以它像步骤名一样写在行上，不用打开这一步就能看见。
    /// </summary>
    [Fact]
    public void A_step_row_says_where_its_input_goes()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            editor.AddStep(Step("input.keyPress"));
            editor.AddStep(Step("input.keyPress", InputMode("background")));
            editor.AddStep(Step("control.log"));

            var rows = editor.Rows.Where(row => row.IsStep).ToList();

            // 目录里那一格默认就是前台，所以没写这个字段的按键步骤也是前台；记一行日志的动作
            // 根本没有这一格，行上什么也不画。
            Assert.Equal(Strings.Get("Editor.InputForeground"), rows[0].InputMark);
            Assert.Equal(Strings.Get("Editor.InputBackground"), rows[1].InputMark);
            Assert.Equal(Strings.Get("Editor.InputDriver"),
                Step("input.keyPress", InputMode("driver")).InputMark);
            Assert.False(rows[2].HasInputMark);
            Assert.Empty(rows[2].InputMark);
        });
    }

    /// <summary>The mark is only worth having if the list draws it.</summary>
    [Fact]
    public void The_step_list_draws_the_input_mark()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "marks" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            editor.AddStep(Step("input.keyPress", InputMode("driver")));
            Dispatcher.UIThread.RunJobs();

            var drawn = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(block => block.Text)
                .ToList();

            Assert.Contains(Strings.Get("Editor.InputDriver"), drawn);
        });
    }

    private static StepParameter InputMode(string value)
        => new() { Name = "inputMode", Kind = ActionParameterKind.Text, Value = value };
}
