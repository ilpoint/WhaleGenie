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

// Calls, returns, scripts and the clipboard: what the dialog shows for each kind of step.
public partial class ActionDialogTests
{
    /// <summary>
    /// A call to another macro is a form of that macro's own declarations: naming a macro is what
    /// puts its arguments and its results on the form, and naming another one replaces them. What
    /// was filled in for a macro is still there on the way back, so looking at another one does not
    /// throw away what the author had written.
    /// </summary>
    [Fact]
    public void A_calls_fields_are_the_declarations_of_the_macro_it_names()
    {
        Ui.Run(() =>
        {
            var window = CallWindow();
            var viewModel = (AddActionViewModel)window.DataContext!;

            // Nothing is asked for until a macro is named: what a call fills in is that macro's list.
            Assert.Empty(viewModel.CallArgumentRows);
            Assert.Empty(viewModel.ReturnsRows);

            Field(viewModel, "macro")!.WriteValue(Fields.Shape("整理"));
            Dispatcher.UIThread.RunJobs();

            var rows = viewModel.CallArgumentRows;
            var times = Assert.Single(rows, row => row.Name == "次数");
            // The default belongs to the declaration, not to the call: an untouched row stays empty
            // so a changed default is what every call without a value reads.
            Assert.Equal(string.Empty, times.Value);
            Assert.Contains("3", times.Hint, StringComparison.Ordinal);
            Assert.False(times.IsRequired);
            Assert.True(Assert.Single(rows, row => row.Name == "标签").IsRequired);
            Assert.Equal("答案", Assert.Single(viewModel.ReturnsRows).Key);

            times.Value = "n";

            // Another macro is another form...
            Field(viewModel, "macro")!.WriteValue(Fields.Shape("清空"));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(viewModel.CallArgumentRows);
            Assert.Empty(viewModel.ReturnsRows);

            // ...and coming back does not empty the values that were filled in.
            Field(viewModel, "macro")!.WriteValue(Fields.Shape("整理"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("n", Assert.Single(viewModel.CallArgumentRows,
                row => row.Name == "次数").Value);
        });
    }

    [Fact]
    public void A_call_saves_what_was_filled_in_and_opens_again_the_same_way()
    {
        Ui.Run(() =>
        {
            var window = CallWindow();
            var viewModel = (AddActionViewModel)window.DataContext!;

            Field(viewModel, "macro")!.WriteValue(Fields.Shape("整理"));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(viewModel.CallArgumentRows, row => row.Name == "次数").Value = "n";
            Assert.Single(viewModel.CallArgumentRows, row => row.Name == "标签").Value = "开始";
            viewModel.ReturnsRows[0].Ticked = true;
            viewModel.ReturnsRows[0].Name = "答案";

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal("整理", saved!.Text("macro"));

            // What a call passes is one list of rows, written the way its results are asked for.
            var passed = Assert.Single(saved.Parameters,
                parameter => parameter.Name == FieldNames.Arguments).Rows;
            Assert.Equal(2, passed.Count);
            Assert.Equal("n", Assert.Single(passed, row => row.Text(FieldNames.Name) == "次数")
                .Text(FieldNames.Value));
            Assert.Equal("开始", Assert.Single(passed, row => row.Text(FieldNames.Name) == "标签")
                .Text(FieldNames.Value));

            // An argument nobody filled in is not written at all, which is how the engine tells
            // "use the default" from "pass this value".
            Assert.DoesNotContain(passed, row => row.Text(FieldNames.Name) == "可省");
            var row = Assert.Single(saved.Parameters.First(parameter => parameter.Name == "returns").Rows);
            Assert.Equal("答案", row.Text("key"));
            Assert.Equal("答案", row.Text("name"));

            var reopened = new ActionEditorWindow(saved, ActionCatalog.Definitions, [], Macros());
            reopened.Show();
            Dispatcher.UIThread.RunJobs();

            var again = (AddActionViewModel)reopened.DataContext!;
            Assert.Equal("整理", Field(again, "macro")!.CurrentText);
            Assert.Equal("n", Assert.Single(again.CallArgumentRows, row => row.Name == "次数").Value);
            Assert.Equal("开始", Assert.Single(again.CallArgumentRows, row => row.Name == "标签").Value);
            Assert.True(Assert.Single(again.ReturnsRows).Ticked);
        });
    }

    /// <summary>
    /// 名字旁边那片空说的是「它装的是什么」：一个开关给 true | false，一条记录给它的字段骨架，
    /// 字段名就是 field(变量, "x") 里要写的那个词。行里只放前几个，全的在悬停里。
    /// </summary>
    [Fact]
    public void A_return_line_shows_the_shape_of_what_it_hands_over()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findColor");

            Assert.Equal("true | false",
                viewModel.ReturnsRows.First(row => row.Key == "found").Structure);

            // 命中是一像素，记录里前四个字段就是它的位置；十一个字段在行里放不下，用省略号收尾。
            Assert.Equal("{x, y, left, top, …}",
                viewModel.ReturnsRows.First(row => row.Key == "match").Structure);
            Assert.Equal("[{x, y, left, top, …}, …]",
                viewModel.ReturnsRows.First(row => row.Key == "every").Structure);

            // 悬停里是全的：每个字段都带类型，取法也写在里面。
            var tip = viewModel.ReturnsRows.First(row => row.Key == "match").StructureTip;
            Assert.Contains("box: text", tip, StringComparison.Ordinal);
            Assert.Contains("label: text", tip, StringComparison.Ordinal);
            Assert.Contains("11", tip, StringComparison.Ordinal);

            // 表格不一样：列名是运行时从表头读到的，所以这一格写「一行一条、键是列名」，
            // 而不是某几个具体的字段名。四个读表的动作都是这一种形状。
            var table = Open("uia.readTable").ReturnsRows.First(row => row.Key == "table");
            Assert.Equal(
                $"[{{{Strings.Get("Add.ReturnShapeColumn")}: {Strings.Get("Add.ReturnShapeText")}}}, …]",
                table.Structure);
            Assert.Contains("field(get(", table.StructureTip, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// 脚本正文用的是一种自己的框：等宽、更高、长行不折（缩进是脚本的一部分，折了就换成两行），
    /// 其余多行字段还是普通的带折行的小框。这是唯一一处「改了字段种类但界面没跟上」不会让别处报错的
    /// 地方，所以它单独有一条。
    /// </summary>
    [Fact]
    public void A_script_is_written_in_a_code_box_and_nothing_else_is()
    {
        Ui.Run(() =>
        {
            string[] scripts =
            [
                "script.run", "script.python", "script.csharp", "script.ahk", "browser.evaluate",
            ];

            foreach (var key in scripts)
            {
                var field = Open(key).Parameters.First(parameter =>
                    parameter.Definition.Kind is ActionParameterKind.Code);
                Assert.True(field.IsCode, key);
                // 框还在：Kind 换了而 IsMultiline 没跟上时，整块编辑区会消失且没有别处报警。
                Assert.True(field.IsMultiline, key);
                Assert.Equal(240, field.MultilineHeight);
                Assert.Equal(Avalonia.Media.TextWrapping.NoWrap, field.Wrap);
            }

            var note = Open("control.note").Parameters.First(parameter =>
                parameter.Definition.Name == "note");
            Assert.False(note.IsCode);
            Assert.Equal(76, note.MultilineHeight);
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, note.Wrap);
        });
    }

    /// <summary>
    /// 取剪贴板图片这一步没有可填的字段，它问的就是「此刻剪贴板上是什么」，所以那张图直接摆出来：
    /// 打开表单时读一次，之后按「刷新」。空剪贴板与被别的程序占着各说各的，不是同一句「没有」。
    /// </summary>
    [Fact]
    public void A_step_that_takes_the_clipboard_picture_shows_the_one_that_is_there()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("clipboard.readImage");
            Assert.True(viewModel.ShowsClipboardPreview);

            var clipboard = new RecordingClipboard { Image = new ImageFrame(4, 3, new byte[48]) };
            viewModel.Clipboard = clipboard;

            Assert.True(viewModel.HasClipboardPreview);
            Assert.Equal(new PixelSize(4, 3), viewModel.ClipboardPreview!.PixelSize);
            Assert.Contains("4", viewModel.ClipboardPreviewNote, StringComparison.Ordinal);
            Assert.Contains("3", viewModel.ClipboardPreviewNote, StringComparison.Ordinal);

            // 勾一项结果、改一处设置只是填表，不该再去问剪贴板；问一次要等另一个程序让开。
            viewModel.ReturnsRows.First(row => row.Key == "width").Ticked = true;
            Assert.Equal(1, clipboard.Reads);

            viewModel.RefreshClipboardCommand.Execute(null);
            Assert.Equal(2, clipboard.Reads);
        });
    }

    /// <summary>
    /// 空与被占用是两件事：一个是「现在没有图可拿」，一个是「现在问不到」。前者按「刷新」也还是空，
    /// 后者等一会儿就会变；写成同一句会让人以为剪贴板真的空了。
    /// </summary>
    [Fact]
    public void An_empty_clipboard_and_a_busy_one_say_which_they_are()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("clipboard.readImage");

            viewModel.Clipboard = new RecordingClipboard();
            Assert.False(viewModel.HasClipboardPreview);
            Assert.Equal(Strings.Get("Add.ClipboardPreviewEmpty"), viewModel.ClipboardPreviewNote);

            viewModel.Clipboard = new RecordingClipboard { Busy = true };
            Assert.False(viewModel.HasClipboardPreview);
            Assert.Equal(Strings.Get("Add.ClipboardPreviewBusy"), viewModel.ClipboardPreviewNote);

            // 没有设备时什么都不说，也不抛：表单照样开得出来。
            viewModel.Clipboard = null;
            Assert.Null(viewModel.ClipboardPreview);
        });
    }

    /// <summary>
    /// 只有结果、没有可填字段的动作（读剪贴板图片就是一例）原来连基础设置页都没有：返回值只能在这一页
    /// 勾，于是那个勾永远点不到，预览也没有地方站。现在这一页在「有结果」或「有预览」时照样在。
    /// </summary>
    [Fact]
    public void An_action_whose_answer_is_a_result_still_has_a_page_to_pick_it_on()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("clipboard.readImage");

            Assert.Equal(AddActionViewModel.BasePage, viewModel.Page!.Key);
            Assert.NotEmpty(viewModel.ReturnsRows);
            Assert.True(viewModel.ShowsClipboardPreview);
        });
    }

    /// <summary>
    /// 别的动作不摆这一块：它们的答案在字段里，多一块预览只是多一段要读的字。
    /// </summary>
    [Fact]
    public void The_clipboard_preview_belongs_only_to_the_action_that_takes_a_picture_from_it()
    {
        Ui.Run(() =>
        {
            Assert.False(Open("clipboard.readText").ShowsClipboardPreview);
            Assert.False(Open("clipboard.writeImage").ShowsClipboardPreview);
        });
    }

    /// <summary>
    /// 早先勾过、后来不再列出的结果：编辑器不画那一行，但保存时照原样写回去。打开一步再存一次
    /// 就悄悄少一个结果，读它的步骤会在运行时找不到名字，而错不在那一步上。
    /// </summary>
    [Fact]
    public void A_result_no_longer_offered_is_written_back_as_it_was()
    {
        Ui.Run(() =>
        {
            var step = new MacroStep
            {
                Id = "k3f9",
                Type = "vision.findColor",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "returns",
                        Kind = ActionParameterKind.Returns,
                        Rows =
                        [
                            new StepParameterRow
                            {
                                Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = Formula.Text("method"),
            ["name"] = Formula.Text("取法"),
        },
                            },
                        ],
                    },
                ],
            };

            var window = new ActionEditorWindow(step, ActionCatalog.Definitions, []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var form = (AddActionViewModel)window.DataContext!;

            // 不列出来：这一步用哪种方式取的画面，宏拿着做不了什么。
            Assert.DoesNotContain(form.ReturnsRows, row => row.Key == "method");

            MacroStep? saved = null;
            form.CloseRequested += written => saved = written;
            form.SaveCommand.Execute(null);

            var kept = Assert.Single(saved!.Parameters
                .First(parameter => parameter.Name == "returns").Rows);
            Assert.Equal("method", kept.Text("key"));
            Assert.Equal("取法", kept.Text("name"));
        });
    }

    /// <summary>The dialog open on a call, with the project's macros to point it at.</summary>
    private static ActionEditorWindow CallWindow()
    {
        var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], Macros());
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction("control.runMacro");
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>The macros a call can be pointed at, with what each of them declares.</summary>
    private static IReadOnlyList<MacroCallable> Macros() =>
    [
        new MacroCallable("整理",
            [
                new MacroParameter
                {
                    Name = "次数",
                    Default = Formula.Number(3),
                    Required = false,
                    Description = "要做多少遍。",
                },
                new MacroParameter { Name = "标签", Description = "写在每一项前面的文字。" },
                new MacroParameter { Name = "可省", Required = false },
            ],
            [new MacroResult
            {
                Name = "答案",
                Description = "整理完留下的数。",
                Type = ValueKind.Number,
            }]),
        new MacroCallable("清空", [], []),
    ];

    /// <summary>One field of the open form, or null when this action has no such field.</summary>
    private static StepParameterViewModel? Field(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.FirstOrDefault(parameter => parameter.Definition.Name == name);

    /// <summary>The run button under the form, found the way a user finds it: by what it says.</summary>
    private static Button RunButton(Window window)
        => window.GetVisualDescendants().OfType<Button>()
            .First(item => item.IsEffectivelyVisible
                && Equals(item.Content, Strings.Get("Add.RunStep")));

    /// <summary>
    /// A device layer with a notebook behind it: what a trial sent is written down, and everything
    /// else is refused the way a machine with no devices would refuse it. It is how the test button
    /// can be checked without a real keyboard, controller or screen being touched.
    /// </summary>
    private sealed class RecordingDevices : IDeviceLayer, IInputDevice, IGamepadDevice
    {
        public RecordingDevices() => Inputs = new SingleInputRouter(this);

        public List<string> Calls { get; } = [];

        /// <summary>Nothing watches this machine but the trial itself: the hook and the word about
        /// windows changing answer the way a machine with nothing to say does.</summary>
        public IInputHook Hook => NullDeviceLayer.Instance.Hook;

        public IInputRecorder CreateRecorder() => NullDeviceLayer.Instance.CreateRecorder();

        public IWindowEvents WindowEvents { get; } = new NoWindowEvents();

        public IGamepadReading GamepadReading { get; } = new NoGamepadReading();

        public IDriverInput Driver => NullDeviceLayer.Instance.Driver;

        public IInputRouter Inputs { get; }

        public IInputDevice Input => this;

        public IGamepadDevice Gamepad => this;

        /// <summary>Automatic, like a machine nobody has set up to read the screen another way.</summary>
        public CaptureChoice Ways { get; } = new();

        ICaptureChoice IDeviceLayer.Capture => Ways;

        public IScreenDevice Screen => NullDeviceLayer.Instance.Screen;

        public IVisionDevice Vision => NullDeviceLayer.Instance.Vision;

        public IDetectorDevice Detector => NullDeviceLayer.Instance.Detector;

        public IOcrDevice Ocr => NullDeviceLayer.Instance.Ocr;

        public IUiDevice Ui => NullDeviceLayer.Instance.Ui;

        public IFileDevice Files => NullDeviceLayer.Instance.Files;

        public IClipboardDevice Clipboard => NullDeviceLayer.Instance.Clipboard;

        public IProcessDevice Processes => NullDeviceLayer.Instance.Processes;


        /// <summary>Scripts are not part of what these checks hand in.</summary>

        public IScriptDevice Scripts => NullDeviceLayer.Instance.Scripts;

        public ISystemDevice System => NullDeviceLayer.Instance.System;

        public IWindowDevice Windows => NullDeviceLayer.Instance.Windows;

        public IBrowserDevice Browser => NullDeviceLayer.Instance.Browser;

        public ScreenPoint Cursor => new(0, 0);

        public void KeyPress(string key, int holdMs) => Calls.Add($"keyPress {key} {holdMs}");

        public void KeyDown(string key) => Calls.Add($"keyDown {key}");

        public void KeyUp(string key) => Calls.Add($"keyUp {key}");

        public void Hotkey(IReadOnlyList<string> keys, int holdMs) => Calls.Add("hotkey");

        public void TypeText(string text, int intervalMs) => Calls.Add("typeText");

        public void MoveMouse(int x, int y, int durationMs) => Calls.Add("moveMouse");

        public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
            => Calls.Add("moveMouseAlong");

        public void MoveMouseRelative(int dx, int dy, int durationMs)
            => Calls.Add("moveMouseRelative");

        public void MouseDown(string button, int x, int y) => Calls.Add("mouseDown");

        public void MouseUp(string button, int x, int y) => Calls.Add("mouseUp");

        public void Click(string button, int x, int y, int clicks, int intervalMs) => Calls.Add("click");

        public void Scroll(string direction, int delta, int x, int y) => Calls.Add("scroll");

        public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs,
            int steps) => Calls.Add("drag");

        public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
            => Calls.Add("dragAlong");

        public void Connect() => Calls.Add("gamepadConnect");

        public void Button(string button, bool down) => Calls.Add($"gamepadButton {button} {down}");

        public void Stick(string stick, int x, int y) => Calls.Add($"gamepadStick {stick} {x} {y}");

        public void Trigger(string trigger, int amount)
            => Calls.Add($"gamepadTrigger {trigger} {amount}");

        public void ReleaseAll() => Calls.Add("gamepadRelease");

        /// <summary>Waits for something to have been sent, so a check can see a click's work.</summary>
        public async Task<bool> WaitUntil(Func<bool> done)
        {
            // The trial runs off this thread, and a machine busy with the rest of the checks can
            // take a while to get to it: how long that takes says nothing about whether the click
            // worked, so there is room here rather than a queue that turns into a failure.
            var step = TimeSpan.FromMilliseconds(25);
            for (var waited = TimeSpan.Zero; waited < TimeSpan.FromSeconds(30); waited += step)
            {
                if (done())
                {
                    return true;
                }

                await Task.Delay(step);
            }

            return done();
        }
    }
}
