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

// The buttons that pick a place or a rectangle on screen, and the fields they fill.
public partial class ActionDialogTests
{
    [Fact]
    public void A_position_picked_on_screen_is_stored_relative_to_the_window_client()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.capture");
            viewModel.Windows = new OneOpenWindow();
            Anchor(viewModel, "window");

            Assert.True(viewModel.ApplyRegion(1008, 530, 30, 40));

            // The rectangle a step reads is stored the way the engine will read it back: counted
            // from the client-area corner rather than from the screen.
            Assert.Equal("8", Value(viewModel, "x"));
            Assert.Equal("10", Value(viewModel, "y"));
            Assert.Equal("30", Value(viewModel, "width"));
            Assert.Equal("40", Value(viewModel, "height"));
        });
    }

    [Fact]
    public void The_coordinate_bases_are_only_screen_and_window()
    {
        var modes = Ui.Run(() => ActionCatalog.Definitions
            .SelectMany(definition => definition.Parameters)
            .Where(parameter => parameter.Name == "anchorMode")
            .Select(parameter => parameter.Options)
            .Distinct()
            .ToList());

        Assert.All(modes, options => Assert.Equal(["screen", "window"], options));
    }

    /// <summary>Turns the coordinate picker of a dialog onto a named window.</summary>
    private static void Choose(AddActionViewModel viewModel, string name, string value)
    {
        var choice = viewModel.Parameters.First(parameter => parameter.Definition.Name == name);
        choice.Option = choice.Choices.First(option => option.Value == value);
    }

    /// <summary>Turns the coordinate picker of a dialog onto a named window.</summary>
    private static void Anchor(AddActionViewModel viewModel, string mode)
    {
        var choice = viewModel.Parameters
            .First(parameter => parameter.Definition.Name == "anchorMode");

        choice.Option = choice.Choices.First(option => option.Value == mode);
        viewModel.Parameters
            .First(parameter => parameter.Definition.Name == "window")
            .ApplyWindowRule(new WindowRule(Title: "Notepad"));
    }

    /// <summary>
    /// One window open at 1000,500 with its client area 20 pixels further down, which is what the
    /// picture of a step's numbers is taken against.
    /// </summary>
    private sealed class OneOpenWindow : IWindowDevice
    {
        private static readonly WindowInfo Notepad = new(1, "Notepad - notes.txt",
            new ScreenPoint(1000, 500), new ScreenSize(800, 600), false, false);

        public IReadOnlyList<WindowInfo> List() => [Notepad];

        public IReadOnlyList<WindowInfo> Filter(string value, WindowMatch match, string compare,
            bool everyWhenEmpty = false, TextCompareOptions options = default)
            => Find(value, match, compare, options) is { } window ? [window] : [];

        public WindowInfo? Find(string value, WindowMatch match, string compare,
            TextCompareOptions options = default)
            => Notepad.Title.Contains(value, StringComparison.OrdinalIgnoreCase) ? Notepad : null;

        public string ProcessOf(long handle) => "notepad";

        public int ProcessIdOf(long handle) => 0;

        public string ClassOf(long handle) => "Notepad";

        public bool Activate(long handle) => true;

        public bool Minimize(long handle) => true;

        public bool Maximize(long handle) => true;

        public bool Restore(long handle) => true;

        public bool Close(long handle) => true;

        public bool Move(long handle, int x, int y, int width, int height) => true;

        public ScreenPoint ClientOrigin(long handle) => new(1000, 520);

        public ScreenSize ClientSize(long handle) => new(784, 560);
    }

    /// <summary>
    /// One control on screen at 1000,500, which is what a step anchored to a control measures from.
    /// Everything asked of it except the search is answered with "yes", because this stand-in
    /// exists to show where the numbers land rather than to drive anything.
    /// </summary>
    private sealed class OneFoundElement : IUiDevice
    {
        private static readonly UiElementInfo Save = new("Save", "saveButton", "Button",
            "ButtonClass", new ScreenPoint(1000, 500), new ScreenSize(80, 24), "Notepad");

        public bool Exists(UiQuery query, int timeoutMs) => true;

        public UiElementInfo? ElementAt(int x, int y) => Save;

        public bool Click(UiQuery query, string button) => true;

        public bool Invoke(UiQuery query) => true;

        public UiElementFacts? Facts(UiQuery query) => new(true, true, Save.Name, Save.Name,
            Save.AutomationId, Save.ControlType, Save.Location, Save.Size);

        public bool FocusWindow(UiQuery query) => true;

        public bool SetText(UiQuery query, string text, bool clearFirst) => true;

        public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit) => [Save];

        public bool Select(UiQuery query, string text, int itemIndex) => true;

        public bool SetChecked(UiQuery query, bool? state) => true;

        public bool SetExpanded(UiQuery query, string action) => true;

        public bool ScrollIntoView(UiQuery query) => true;

        public UiTable ReadTable(UiQuery query, int limit) => UiTable.Empty;
    }
    /// <summary>
    /// A step that hands a result over writes a variable of that name, and the macro's list of
    /// variables includes it — the step being edited is one of the macro's steps. Opening the dialog
    /// on that step used to tell the name was taken by another variable, which is the step calling
    /// itself another variable.
    /// </summary>
    [Fact]
    public void Editing_a_step_does_not_call_its_own_result_taken()
    {
        Ui.Run(() =>
        {
            var handles = new List<VariableChoice>
            {
                new() { Name = "video", Scope = VariableScope.Local },
            };
            var step = new MacroStep
            {
                Id = "ev01",
                Type = "browser.evaluate",
                Parameters =
                [
                    new StepParameter { Name = "script", Kind = ActionParameterKind.Text, Written = Fields.Shape("() => 1") },
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
            ["key"] = Formula.Text("result"),
            ["name"] = Formula.Text("video"),
        },
                            },
                        ],
                    },
                ],
            };

            var window = new ActionEditorWindow(step, ActionCatalog.Definitions, handles);
            window.Show();
            var form = (AddActionViewModel)window.DataContext!;

            var row = form.ReturnsRows.Single(candidate => candidate.Key == "result");
            Assert.True(row.Ticked);
            Assert.Equal("video", row.Name);
            Assert.False(row.IsNameTaken);
            Assert.Empty(row.NameWarning);

            window.Close();
        });
    }

    [Fact]
    public void A_vision_step_opens_with_the_position_it_finds_ticked()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var result = viewModel.ReturnsRows.First(row => row.Key == "match");

            // The position is the answer such a step nearly always wants, so it is the one ticked
            // when the step is written, and it is named after the note, the action and the result.
            Assert.True(result.Ticked);
            Assert.NotEmpty(result.Name);

            // The rest are there to be asked for, not handed over by default.
            Assert.All(viewModel.ReturnsRows.Where(row => !row.IsPrimary),
                row => Assert.False(row.Ticked));
        });
    }

    [Theory]
    [InlineData("vision.capture", "x")]
    [InlineData("input.mouseDrag", "startX")]
    public void The_region_picker_sits_on_the_row_that_names_a_rectangle(string key, string anchor)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(key);

            Assert.True(viewModel.HasRegion);

            // Where the button is written down is the line that names the rectangle, and it stands
            // on the basic page: a rectangle is dragged out on the screen, and that is what the
            // step is about rather than how it is tuned.
            var field = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == anchor);
            Assert.True(field.IsRegionAnchor);
            Assert.Contains(anchor, viewModel.Rows.SelectMany(Names));
        });
    }

    /// <summary>
    /// A drag names two points rather than one rectangle. Its picker button therefore says where
    /// the drag goes, while the other region fields keep the ordinary rectangle wording.
    /// </summary>
    [Fact]
    public void A_drag_picker_button_names_its_start_and_end()
    {
        Ui.Run(() =>
        {
            var (window, drag) = OpenWindow("input.mouseDrag");
            var dragRow = drag.Rows.First(row => row.First.Definition.Name == "startX");

            Assert.Equal(Strings.Get("Add.PickRegionDrag"), dragRow.RegionPickerLabel);
            Assert.Equal(Strings.Get("Add.PickRegionDragHint"), dragRow.RegionPickerHint);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(),
                button => button.IsEffectivelyVisible
                    && Equals(button.Content, Strings.Get("Add.PickRegionDrag")));

            var region = Open("vision.capture");
            var regionRow = region.Rows.First(row => row.First.Definition.Name == "x");

            Assert.Equal(Strings.Get("Add.PickRegion"), regionRow.RegionPickerLabel);
            Assert.Contains("RegionPickerWindow.PickDragAsync", ViewSource.All("ActionEditorView.axaml.cs"),
                StringComparison.Ordinal);
            window.Close();
        });
    }

    [Theory]
    [InlineData("input.mouseClick")]
    [InlineData("vision.getPixel")]
    [InlineData("input.keyPress")]
    public void An_action_that_only_needs_a_point_has_no_region_picker(string key)
    {
        Ui.Run(() => Assert.False(Open(key).HasRegion));
    }

    [Fact]
    public void A_rectangle_fills_the_four_numbers_a_window_step_works_on()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("window.move");

            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));
            Assert.Equal("10", Value(viewModel, "x"));
            Assert.Equal("20", Value(viewModel, "y"));
            Assert.Equal("30", Value(viewModel, "width"));
            Assert.Equal("40", Value(viewModel, "height"));
        });
    }

    /// <summary>
    /// A window bounds where a rectangle may be drawn; it is not a rectangle already drawn. The
    /// ordinary region buttons therefore pass no starting rectangle even when the step reads a
    /// window.
    /// </summary>
    [Fact]
    public void A_window_bound_region_picker_starts_with_nothing_selected()
    {
        var code = ViewSource.All("ActionEditorView.axaml.cs");

        Assert.Contains(
            "RegionPickerWindow.PickAsync(HostWindow!, Devices.Screen, Devices.Windows,",
            code, StringComparison.Ordinal);
        Assert.DoesNotContain("RegionPickerWindow.PickAsync(HostWindow!, Devices.Screen, Devices.Windows, bounds, bounds)", code,
            StringComparison.Ordinal);
    }

    /// <summary>The mover's picker hands the target window to the frame, which moves it as it is dragged.</summary>
    [Fact]
    public void The_window_bounds_picker_moves_the_target_window()
    {
        var code = ViewSource.All("ActionEditorView.axaml.cs");

        Assert.Contains(
            "RegionPickerWindow.PickAsync(HostWindow!, Devices.Screen, Devices.Windows, initial,",
            code, StringComparison.Ordinal);
    }


    /// <summary>
    /// A step that reads or copies one part of the screen has the four numbers as ordinary fields,
    /// so what the region picker dragged out lands in them one by one and each of them can still be
    /// written as a formula of its own.
    /// </summary>
    [Theory]
    [InlineData("vision.capture")]
    public void A_rectangle_fills_the_one_region_a_step_reads(string key)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(key);

            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));

            Assert.Equal("10", Value(viewModel, "x"));
            Assert.Equal("20", Value(viewModel, "y"));
            Assert.Equal("30", Value(viewModel, "width"));
            Assert.Equal("40", Value(viewModel, "height"));
            Assert.Contains("x", viewModel.Rows.SelectMany(Names));
        });
    }

    /// <summary>
    /// 识别文字的范围是一张列表：拖一次就多一处，与找色、找字一样。
    /// </summary>
    [Fact]
    public void A_rectangle_adds_a_place_to_what_a_reading_step_reads()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("ocr.recognize");
            var region = Field(viewModel, "region")!;

            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));

            var row = Assert.Single(region.Regions);
            Assert.Equal("10", row.X.Text);
            Assert.Equal("20", row.Y.Text);
            Assert.Equal("30", row.Width.Text);
            Assert.Equal("40", row.Height.Text);
        });
    }

    /// <summary>
    /// The pattern a reading step cuts its text down by is asked for only while the reading is set to
    /// keep what a pattern matches, and a step that keeps the writing as it stands writes nothing
    /// about a pattern: one field belongs to one way of doing something, and travels with it.
    /// </summary>
    [Fact]
    public void The_pattern_a_reading_keeps_its_text_by_is_asked_for_only_when_it_is_used()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("ocr.recognize");
            var pattern = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "contentPattern");

            // Nothing about a pattern until the reading says it keeps what one matches.
            Assert.False(Shown(viewModel, "contentPattern"));

            Choose(viewModel, "content", "letters");
            Dispatcher.UIThread.RunJobs();
            Assert.False(Shown(viewModel, "contentPattern"));

            Choose(viewModel, "content", "regex");
            Dispatcher.UIThread.RunJobs();
            Assert.True(Shown(viewModel, "contentPattern"));

            // It is tuning rather than what the step is, so it is behind the fold.
            Assert.Contains("contentPattern", viewModel.AdvancedRows.SelectMany(Names));

            pattern.Text = @"单号：(\d+)";
            Dispatcher.UIThread.RunJobs();

            MacroStep? kept = null;
            viewModel.CloseRequested += step => kept = step;
            viewModel.SaveCommand.Execute(null);

            var written = kept!.Parameters.ToDictionary(parameter => parameter.Name,
                parameter => parameter.Value, StringComparer.Ordinal);
            Assert.Equal("regex", written["content"]);
            Assert.Equal(@"单号：(\d+)", written["contentPattern"]);

            // Told to keep the writing as it stands, the step carries no pattern at all.
            Choose(viewModel, "content", "text");
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain("contentPattern", viewModel.JsonPreview, StringComparison.Ordinal);

            MacroStep? plain = null;
            viewModel.CloseRequested += step => plain = step;
            viewModel.SaveCommand.Execute(null);

            Assert.DoesNotContain(plain!.Parameters,
                parameter => parameter.Name == "contentPattern");
        });
    }

    /// <summary>A search that may look in several places takes the rectangle as one more place.</summary>
    [Fact]
    public void A_rectangle_adds_a_place_to_a_search()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("ocr.findText");
            var region = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "region");

            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));

            var row = Assert.Single(region.Regions);
            Assert.Equal("10", row.X.Text);
            Assert.Equal("20", row.Y.Text);
            Assert.Equal("30", row.Width.Text);
            Assert.Equal("40", row.Height.Text);
        });
    }

    [Fact]
    public void A_rectangle_fills_both_ends_of_a_drag()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseDrag");

            // 框选一次填满四格：起点是矩形的左上角，终点是右下角，两头照同一个「坐标基准」量。
            Assert.True(viewModel.ApplyRegion(10, 20, 30, 40));
            Assert.Equal("10", Value(viewModel, "startX"));
            Assert.Equal("20", Value(viewModel, "startY"));
            Assert.Equal("40", Value(viewModel, "endX"));
            Assert.Equal("60", Value(viewModel, "endY"));
        });
    }

    /// <summary>A drag is a direction, so its two ends stay in the order they were dragged.</summary>
    [Fact]
    public void A_drag_keeps_the_direction_it_was_dragged()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("input.mouseDrag");

            Assert.True(viewModel.ApplyDrag(90, 80, 10, 20));
            Assert.Equal("90", Value(viewModel, "startX"));
            Assert.Equal("80", Value(viewModel, "startY"));
            Assert.Equal("10", Value(viewModel, "endX"));
            Assert.Equal("20", Value(viewModel, "endY"));
        });
    }

    /// <summary>
    /// 一个位置只有一个答案：填了记录，旁边的数字就清空；写了数字，记录格就清空。两个一起留着，
    /// 读的人就得自己猜运行时按哪一个走。
    /// </summary>
    [Theory]
    [InlineData("input.mouseMove", "place", "x")]
    [InlineData("input.mouseClick", "place", "x")]
    [InlineData("input.mouseDoubleClick", "place", "x")]
    [InlineData("input.mouseDown", "place", "x")]
    [InlineData("input.mouseUp", "place", "x")]
    [InlineData("input.mouseScroll", "place", "x")]
    [InlineData("vision.getPixel", "place", "x")]
    [InlineData("vision.waitColor", "place", "x")]
    [InlineData("vision.capture", "rect", "x")]
    [InlineData("window.move", "rect", "x")]
    [InlineData("input.mouseDrag", "startPlace", "startX")]
    [InlineData("input.mouseDrag", "endPlace", "endX")]
    public void A_place_and_its_numbers_empty_each_other(string action, string place, string number)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(action);
            var record = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == place);
            var numbers = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == number);

            numbers.SetNumber(120);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("120", numbers.CurrentText);
            Assert.Equal(string.Empty, record.CurrentText);

            record.Text = "命中";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("命中", record.CurrentText);
            Assert.Equal(string.Empty, numbers.CurrentText);
        });
    }
}
