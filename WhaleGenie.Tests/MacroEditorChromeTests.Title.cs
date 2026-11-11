using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The title bar and the pin the window owns.
public partial class MacroEditorChromeTests
{
    [Fact]
    public void The_title_bar_keeps_the_pin_the_window_owns()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // 关闭、最小化、最大化现在是库画的系统按钮（窗口是 `AppWindow`，即 `FAAppWindow`）：
            // 这一行只剩置顶——那是这一扇窗自己的状态，不是标题栏的通用件。
            var pin = window.FindControl<Button>("PinButton")!;
            Assert.Null(window.FindControl<Button>("MinimizeButton"));
            Assert.Null(window.FindControl<Button>("MaximizeButton"));
            Assert.Null(window.FindControl<Button>("CloseButton"));

            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Topmost);
            Assert.Contains("pinned", pin.Classes);
            Assert.Equal(Strings.Get("Common.Unpin"), ToolTip.GetTip(pin));
            Assert.Equal(AppIcons.PinOff, window.FindControl<IconView>("PinIcon")!.Data);

            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.Topmost);
            Assert.DoesNotContain("pinned", pin.Classes);
            Assert.Equal(Strings.Get("Common.Pin"), ToolTip.GetTip(pin));
            Assert.Equal(AppIcons.Pin, window.FindControl<IconView>("PinIcon")!.Data);

            Ui.CloseAndAnswer(window);
        });
    }

    private static MacroItem ParentWithChild()
    {
        var child = new MacroItem { Name = "child" };
        child.Steps.Add(SetVariable("inside", "1"));

        var parent = new MacroItem { Name = "parent" };
        parent.Steps.Add(new MacroStep
        {
            Type = "control.runMacro",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter
                {
                    Name = "macro",
                    Kind = ActionParameterKind.Macro,
                    Written = Formula.FromLine("child"),
                },
            ],
        });
        parent.SubMacros.Add(child);
        return parent;
    }

    private static void Click(Window window, Button button)
    {
        var center = button.TranslatePoint(
            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        Assert.NotNull(center);
        window.MouseDown(center!.Value, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(center.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static MacroStep SetVariable(string name, string value)
        => new()
        {
            Type = "control.setVariable",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Written = Formula.FromLine(name) },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Choice, Written = Formula.FromLine("local") },
                new StepParameter { Name = "value", Kind = ActionParameterKind.Expression, Written = Formula.FromLine(value) },
            ],
        };
}
