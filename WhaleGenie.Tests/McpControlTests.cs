using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Mcp;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The picture-taking and machine-driving MCP tools, over a device layer a check hands in: what the
/// flyout's switch allows, what the tools ask the devices for, and what comes back. Nothing here
/// reads a real screen or sends a real key; the machine's own answers are left to the person.
/// </summary>
public class McpControlTests : IDisposable
{
    private readonly ChecksDevices _devices = new();
    private readonly RecordingInput _input = new();
    private readonly ChecksScreen _screen = new();
    private readonly WindowsForChecks _windows = new();
    private readonly ElementAtUi _ui = new();
    private readonly McpControlTools _tools;

    public McpControlTests()
    {
        _devices.Inputs = new SingleInputRouter(_input);
        _devices.Screen = _screen;
        _devices.Windows = _windows;
        _devices.Ui = _ui;
        _tools = new McpControlTools(() => _devices);
    }

    /// <summary>The gate is the machine's, not one check's: it goes back off whatever happened.</summary>
    public void Dispose() => McpControl.Allowed = false;

    [Fact]
    public async Task The_driving_tools_are_refused_until_the_person_allows_them()
    {
        Assert.False(McpControl.Allowed);

        var refused = await _tools.MouseMove(10, 20);
        Assert.Equal(McpErrors.DirectControlOff, Code(refused));
        Assert.Contains("Allow direct control", Text(refused), StringComparison.Ordinal);
        Assert.Empty(_input.Done);

        McpControl.Allowed = true;
        var moved = await _tools.MouseMove(10, 20);
        Assert.True(moved.IsError != true);
        Assert.Contains("move 10,20 0", _input.Done);
    }

    [Fact]
    public async Task A_picture_of_a_window_is_asked_for_by_its_rectangle()
    {
        _windows.Add(4242, "计算器", "Calculator", "CalcFrame");
        _windows.Place(4242, 100, 50, 640, 480);

        var shot = await _tools.CaptureScreen(windowHandle: 4242, maxSide: 0);

        var asked = _screen.Asked!;
        Assert.Equal(4242, asked.Window);
        Assert.Equal(100, asked.X);
        Assert.Equal(50, asked.Y);
        Assert.Equal(640, asked.Width);
        Assert.Equal(480, asked.Height);

        var image = Assert.IsType<ImageContentBlock>(shot.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, image.DecodedData.ToArray().Take(4));

        var about = Json(shot);
        Assert.Equal(4242, about.GetProperty("window").GetInt64());
        Assert.Equal(100, about.GetProperty("x").GetInt32());
        Assert.Equal(50, about.GetProperty("y").GetInt32());
        Assert.Equal(640, about.GetProperty("width").GetInt32());
        Assert.Equal(480, about.GetProperty("height").GetInt32());
        Assert.Equal("Auto", about.GetProperty("way").GetString());
    }

    [Fact]
    public async Task A_picture_of_the_desktop_asks_for_the_whole_desktop_and_may_be_scaled()
    {
        var whole = await _tools.CaptureScreen(maxSide: 0, hideWhaleGenie: false);

        var asked = _screen.Asked!;
        Assert.Equal(0, asked.Window);
        Assert.Equal(0, asked.X);
        Assert.Equal(0, asked.Y);
        Assert.Equal(800, asked.Width);
        Assert.Equal(600, asked.Height);
        Assert.Equal(JsonValueKind.Null, Json(whole).GetProperty("scaledFrom").ValueKind);

        var small = await _tools.CaptureScreen(maxSide: 400, hideWhaleGenie: false);
        var about = Json(small);
        Assert.Equal(400, about.GetProperty("width").GetInt32());
        Assert.Equal(300, about.GetProperty("height").GetInt32());
        Assert.Equal(800, about.GetProperty("scaledFrom").GetProperty("width").GetInt32());
        Assert.Equal(600, about.GetProperty("scaledFrom").GetProperty("height").GetInt32());
    }

    [Fact]
    public async Task A_window_that_has_gone_says_so()
    {
        var gone = await _tools.CaptureScreen(windowHandle: 999);

        Assert.Equal(McpErrors.BadArgument, Code(gone));
        Assert.Contains("999", Text(gone), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_colour_and_the_pointer_are_read_from_the_screen()
    {
        _screen.Colour = new PixelColor(0xAB, 0xCD, 0xEF);
        _screen.Pointer = new ScreenPoint(30, 40);

        var colour = Json(await _tools.PixelAt(3, 4));
        Assert.Equal("#ABCDEF", colour.GetProperty("color").GetString());

        var pointer = Json(await _tools.Cursor());
        Assert.Equal(30, pointer.GetProperty("x").GetInt32());
        Assert.Equal(40, pointer.GetProperty("y").GetInt32());
    }

    [Fact]
    public async Task The_open_windows_are_listed_with_their_rectangles()
    {
        _windows.Add(7, "便签", "Notepad", "Notepad");
        _windows.Add(9, "演示", "Demo", "DemoWindow");
        _windows.Place(9, 5, 6, 70, 80);

        var every = Json(await _tools.ListWindows());
        Assert.Equal(2, every.GetProperty("total").GetInt32());
        var windows = every.GetProperty("windows").EnumerateArray().ToArray();
        Assert.Equal(7, windows[0].GetProperty("handle").GetInt64());
        Assert.Equal(9, windows[1].GetProperty("handle").GetInt64());
        Assert.Equal("演示", windows[1].GetProperty("title").GetString());
        Assert.Equal(5, windows[1].GetProperty("x").GetInt32());
        Assert.Equal(70, windows[1].GetProperty("width").GetInt32());

        var found = Json(await _tools.ListWindows(value: "演示", part: WindowPart.Title))
            .GetProperty("windows").EnumerateArray().ToArray();
        var one = Assert.Single(found);
        Assert.Equal(9, one.GetProperty("handle").GetInt64());
    }

    [Fact]
    public async Task The_control_at_a_point_is_named_or_said_to_be_absent()
    {
        _ui.Element = new UiElementInfo("保存", "saveButton", "Button", "Button",
            new ScreenPoint(10, 20), new ScreenSize(80, 24), "演示");

        var found = Json(await _tools.ElementAt(10, 20));
        Assert.True(found.GetProperty("found").GetBoolean());
        Assert.Equal("保存", found.GetProperty("name").GetString());
        Assert.Equal("saveButton", found.GetProperty("automationId").GetString());
        Assert.Equal("Button", found.GetProperty("controlType").GetString());
        Assert.Equal("演示", found.GetProperty("windowTitle").GetString());
        Assert.Equal("Button[automationId='saveButton']", found.GetProperty("selector").GetString());

        _ui.Element = null;
        var absent = Json(await _tools.ElementAt(0, 0));
        Assert.False(absent.GetProperty("found").GetBoolean());
    }

    [Fact]
    public async Task Keys_chords_and_text_go_to_the_input_device()
    {
        McpControl.Allowed = true;

        await _tools.Key("Ctrl+Shift+S");
        Assert.Contains("hotkey Ctrl+Shift+S 0", _input.Done);

        await _tools.Key("Enter", holdMs: 50);
        Assert.Contains("key Enter 50", _input.Done);

        var typed = Json(await _tools.TypeText("秘密", intervalMs: 5));
        Assert.Equal(2, typed.GetProperty("characters").GetInt32());
        Assert.Contains("type 2 5", _input.Done);
    }

    [Fact]
    public async Task The_text_itself_is_not_echoed_back()
    {
        McpControl.Allowed = true;

        var answer = await _tools.TypeText("秘密", 0);

        Assert.DoesNotContain("秘密", Text(answer), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mistake_is_refused_with_a_code_and_says_what_is_allowed()
    {
        McpControl.Allowed = true;

        var key = await _tools.Key("nosuchkey");
        Assert.Equal(McpErrors.BadArgument, Code(key));

        var size = await _tools.WindowMove(55, 0, 0, 0, 100);
        Assert.Equal(McpErrors.BadArgument, Code(size));

        // A point is both numbers or neither: one number means the caller meant a point, and
        // quietly scrolling where the pointer is would turn the wrong part of the screen.
        var half = await _tools.MouseScroll(ScrollDirection.Down, 120, x: 5, y: null);
        Assert.Equal(McpErrors.BadArgument, Code(half));
        Assert.Contains("both x and y", Text(half), StringComparison.Ordinal);

        _ = await _tools.Key("+");

        Assert.Empty(_input.Done);
    }

    [Fact]
    public async Task A_click_stands_when_the_pointer_cannot_be_read_afterwards()
    {
        McpControl.Allowed = true;
        _screen.CursorFails = true;

        var clicked = Json(await _tools.MouseClick(11, 22));

        Assert.Contains("click left 11,22 1 0", _input.Done);
        Assert.Equal(JsonValueKind.Null, clicked.GetProperty("cursor").ValueKind);
    }

    [Fact]
    public async Task A_click_a_drag_and_a_scroll_reach_the_input_device()
    {
        McpControl.Allowed = true;

        var clicked = Json(await _tools.MouseClick(11, 22, MouseButtonName.Right, clicks: 2));
        Assert.Equal("right", clicked.GetProperty("button").GetString());
        Assert.Equal(2, clicked.GetProperty("clicks").GetInt32());
        Assert.Equal(30, clicked.GetProperty("cursor").GetProperty("x").GetInt32());
        Assert.Contains("click right 11,22 2 0", _input.Done);

        await _tools.MouseDrag(1, 2, 3, 4, MouseButtonName.Left);
        Assert.Contains("drag left 1,2->3,4 0", _input.Done);

        await _tools.MouseScroll(ScrollDirection.Up, 120, 5, 6);
        Assert.Contains("scroll up 120 5,6", _input.Done);

        await _tools.MouseMove(7, 8, durationMs: 100);
        Assert.Contains("move 7,8 100", _input.Done);
    }

    [Fact]
    public async Task A_window_can_be_activated_moved_and_closed()
    {
        _windows.Add(55, "演示", "Demo", "DemoWindow");

        McpControl.Allowed = true;

        var activated = Json(await _tools.WindowActivate(55));
        Assert.True(activated.GetProperty("activated").GetBoolean());

        var moved = Json(await _tools.WindowMove(55, 1, 2, 300, 200));
        Assert.True(moved.GetProperty("moved").GetBoolean());
        Assert.Equal(1, _windows.Open[0].Location.X);
        Assert.Equal(2, _windows.Open[0].Location.Y);
        Assert.Equal(300, _windows.Open[0].Size.Width);
        Assert.Equal(200, _windows.Open[0].Size.Height);

        var closed = Json(await _tools.WindowClose(55));
        Assert.True(closed.GetProperty("closed").GetBoolean());
    }

    [Fact]
    public void The_picture_is_scaled_down_to_what_a_client_will_take()
    {
        var frame = new ImageFrame(3840, 2160, new byte[3840L * 2160 * 4]);

        var scaled = McpPictures.Scale(frame, 1920);
        Assert.Equal(1920, scaled.Width);
        Assert.Equal(1080, scaled.Height);

        // A picture already small enough is the same picture, not a copy of it.
        Assert.Same(frame, McpPictures.Scale(frame, 1920 * 2));

        // Sixteen pixels, eight black and eight white, averaged into one grey.
        var pixels = new byte[]
        {
            0, 0, 0, 255,
            255, 255, 255, 255,
            0, 0, 0, 255,
            255, 255, 255, 255,
            0, 0, 0, 255,
            255, 255, 255, 255,
            0, 0, 0, 255,
            255, 255, 255, 255,
            0, 0, 0, 255,
            255, 255, 255, 255,
            0, 0, 0, 255,
            255, 255, 255, 255,
            0, 0, 0, 255,
            255, 255, 255, 255,
            0, 0, 0, 255,
            255, 255, 255, 255,
        };
        var grey = McpPictures.Scale(new ImageFrame(4, 4, pixels), 1);
        Assert.Equal(1, grey.Width);
        Assert.Equal(1, grey.Height);
        Assert.Equal(new PixelColor(127, 127, 127), grey[0, 0]);
    }

    [Fact]
    public void The_direct_control_switch_follows_the_server()
    {
        Ui.RunAsync(async () =>
        {
            McpControl.Allowed = false;
            var viewModel = new MainViewModel();
            Assert.False(viewModel.Mcp.AllowControl);

            await viewModel.Mcp.ToggleCommand.ExecuteAsync(null);
            Assert.True(viewModel.Mcp.IsRunning);

            viewModel.Mcp.ToggleControlCommand.Execute(null);
            Assert.True(viewModel.Mcp.AllowControl);
            Assert.True(McpControl.Allowed);

            await viewModel.Mcp.ToggleCommand.ExecuteAsync(null);
            Assert.False(viewModel.Mcp.AllowControl);
            Assert.False(McpControl.Allowed);
            return true;
        });
    }

    /// <summary>
    /// The control at a point is not only what it is: what it holds and whether it can be used are
    /// the two things a step has to check after typing into it.
    /// </summary>
    [Fact]
    public async Task The_control_at_a_point_says_what_it_holds()
    {
        _ui.Element = new UiElementInfo("姓名", "txtName", "Edit", "Edit",
            new ScreenPoint(10, 20), new ScreenSize(100, 24), "记事本");
        _ui.Said = new UiElementFacts(true, true, "张三", "姓名", "txtName", "Edit",
            new ScreenPoint(10, 20), new ScreenSize(100, 24));

        var read = Json(await _tools.ElementAt(15, 25));
        Assert.True(read.GetProperty("found").GetBoolean());
        Assert.Equal("张三", read.GetProperty("value").GetString());
        Assert.True(read.GetProperty("enabled").GetBoolean());
        Assert.True(read.GetProperty("visible").GetBoolean());
        Assert.True(read.GetProperty("hasValue").GetBoolean());

        // A control that cannot be asked says so rather than pretending the value is empty.
        _ui.Said = null;
        var silent = Json(await _tools.ElementAt(15, 25));
        Assert.False(silent.GetProperty("hasValue").GetBoolean());
    }

    /// <summary>A selector found once can be found again, which is what a step needs.</summary>
    [Fact]
    public async Task Controls_can_be_found_by_selector()
    {
        _ui.Element = new UiElementInfo("保存", "btnSave", "Button", "Button",
            new ScreenPoint(10, 20), new ScreenSize(60, 24), "记事本");

        var found = Json(await _tools.FindElements("Button[name='保存']"));
        Assert.Equal(1, found.GetProperty("count").GetInt32());
        Assert.Equal("保存", found.GetProperty("elements")[0].GetProperty("name").GetString());
    }

    /// <summary>The clipboard is a place a step can read from and write to, and writing needs the switch.</summary>
    [Fact]
    public async Task The_clipboard_can_be_read_and_written()
    {
        var clipboard = new RecordingClipboard();
        clipboard.WriteText("读到的");
        _devices.Clipboard = clipboard;

        var read = Json(await _tools.ClipboardRead());
        Assert.Equal("读到的", read.GetProperty("text").GetString());

        Assert.Equal(McpErrors.DirectControlOff, Code(await _tools.ClipboardWrite("写进去的")));

        McpControl.Allowed = true;
        var wrote = await _tools.ClipboardWrite("写进去的");
        Assert.True(wrote.IsError != true);
        Assert.Equal("写进去的", clipboard.Text);
    }

    /// <summary>One window can be asked about by handle, including which program owns it.</summary>
    [Fact]
    public async Task One_window_can_be_read_by_handle()
    {
        _windows.Add(4242, "计算器", "Calculator", "CalcFrame");
        _windows.Place(4242, 100, 200, 320, 240);
        _windows.ClientAt(4242, 108, 232);

        var info = Json(await _tools.WindowInfo(4242));
        Assert.True(info.GetProperty("found").GetBoolean());
        Assert.Equal("Calculator", info.GetProperty("process").GetString());
        Assert.Equal("CalcFrame", info.GetProperty("className").GetString());
        Assert.Equal(108, info.GetProperty("clientX").GetInt32());

        var missing = Json(await _tools.WindowInfo(9));
        Assert.False(missing.GetProperty("found").GetBoolean());
    }

    /// <summary>A listing can carry the program and class of every window, which is what a filter matched.</summary>
    [Fact]
    public async Task A_window_listing_can_carry_each_window_s_program()
    {
        _windows.Add(4242, "计算器", "Calculator", "CalcFrame");

        var plain = Json(await _tools.ListWindows());
        Assert.Equal(string.Empty, plain.GetProperty("windows")[0].GetProperty("process").GetString());

        var detailed = Json(await _tools.ListWindows(details: true));
        Assert.Equal("Calculator", detailed.GetProperty("windows")[0].GetProperty("process").GetString());
        Assert.Equal("CalcFrame", detailed.GetProperty("windows")[0].GetProperty("className").GetString());
    }

    private static string Text(CallToolResult result)
        => result.Content.OfType<TextContentBlock>().Last().Text;

    private static JsonElement Json(CallToolResult result) => JsonDocument.Parse(Text(result)).RootElement;

    /// <summary>
    /// The code a refusal carries. The sentence is for a person; the code is what a client branches
    /// on, so that is what a check asserts.
    /// </summary>
    private static string Code(CallToolResult result)
        => Refusal(result).GetProperty("code").GetString()!;

    private static JsonElement Refusal(CallToolResult result)
    {
        Assert.True(result.IsError == true, "the call was expected to be refused");
        return JsonDocument.Parse(Text(result)).RootElement.GetProperty("error");
    }
}

/// <summary>The input a check was sent, written down instead of sent to the machine.</summary>
internal sealed class RecordingInput : IInputDevice
{
    /// <summary>What was sent, one line per call, in the order the calls came.</summary>
    public List<string> Done { get; } = [];

    public ScreenPoint Cursor => new(7, 9);

    public void KeyPress(string key, int holdMs) => Done.Add($"key {key} {holdMs}");

    public void KeyDown(string key) => Done.Add($"down {key}");

    public void KeyUp(string key) => Done.Add($"up {key}");

    public void Hotkey(IReadOnlyList<string> keys, int holdMs)
        => Done.Add($"hotkey {string.Join("+", keys)} {holdMs}");

    public void TypeText(string text, int intervalMs) => Done.Add($"type {text.Length} {intervalMs}");

    public void MoveMouse(int x, int y, int durationMs) => Done.Add($"move {x},{y} {durationMs}");

    public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
        => Done.Add($"moveAlong {path.Count} {durationMs}");

    public void MoveMouseRelative(int dx, int dy, int durationMs)
        => Done.Add($"moveRelative {dx},{dy} {durationMs}");

    public void MouseDown(string button, int x, int y) => Done.Add($"mouseDown {button} {x},{y}");

    public void MouseUp(string button, int x, int y) => Done.Add($"mouseUp {button} {x},{y}");

    public void Click(string button, int x, int y, int clicks, int intervalMs)
        => Done.Add($"click {button} {x},{y} {clicks} {intervalMs}");

    public void Scroll(string direction, int delta, int x, int y)
        => Done.Add($"scroll {direction} {delta} {x},{y}");

    public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs,
        int steps) => Done.Add($"drag {button} {startX},{startY}->{endX},{endY} {steps}");

    public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
        => Done.Add($"dragAlong {button} {path.Count}");
}

/// <summary>
/// A screen of one size, one pointer and one colour, that remembers the capture it was asked for:
/// the rectangle a picture was taken of is the whole question a capture tool asks.
/// </summary>
internal sealed class ChecksScreen : IScreenDevice
{
    /// <summary>The last capture request, which is what the tool really asked for.</summary>
    public ScreenCaptureRequest? Asked { get; private set; }

    public PixelColor Colour { get; set; } = new(0x12, 0x34, 0x56);

    public ScreenPoint Pointer { get; set; } = new(30, 40);

    public ScreenLayout Desktop { get; set; } = new(
        new ScreenArea(0, 0, 800, 600),
        [new ScreenDisplay(new ScreenArea(0, 0, 800, 600), true)]);

    public PixelColor PixelAt(int x, int y) => Colour;

    /// <summary>Set to make reading the pointer fail, the way a screen that has gone does.</summary>
    public bool CursorFails { get; set; }

    public ScreenPoint Cursor => CursorFails
        ? throw new DeviceUnavailableException("screen")
        : Pointer;

    public ImageFrame Capture(int x, int y, int width, int height)
        => Blank(width, height);

    public ScreenShot Capture(ScreenCaptureRequest request)
    {
        Asked = request;
        return new ScreenShot(Blank(request.Width, request.Height),
            new ScreenPoint(request.X, request.Y), request.Method);
    }

    private static ImageFrame Blank(int width, int height)
        => new(Math.Max(1, width), Math.Max(1, height),
            new byte[Math.Max(1, (long)Math.Max(1, width) * Math.Max(1, height) * 4)]);
}

/// <summary>
/// A desktop whose UI Automation names one element, or none: the picker's question is what stands
/// at a point, and everything else the device offers is none of a capture check's business.
/// </summary>
internal sealed class ElementAtUi : IUiDevice
{
    public UiElementInfo? Element { get; set; }

    public UiElementInfo? ElementAt(int x, int y) => Element;

    public bool Exists(UiQuery query, int timeoutMs) => Element is not null;

    public bool Click(UiQuery query, string button) => false;

    public bool Invoke(UiQuery query) => false;

    /// <summary>What the control says about itself; null is a control that cannot be asked.</summary>
    public UiElementFacts? Said { get; set; }

    public UiElementFacts? Facts(UiQuery query) => Said;

    public bool FocusWindow(UiQuery query) => false;

    public bool SetText(UiQuery query, string text, bool clearFirst) => false;

    public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit)
        => Element is null ? [] : [Element];

    public bool Select(UiQuery query, string text, int itemIndex) => false;

    public bool SetChecked(UiQuery query, bool? state) => false;

    public bool SetExpanded(UiQuery query, string action) => false;

    public bool ScrollIntoView(UiQuery query) => false;

    public UiTable ReadTable(UiQuery query, int limit) => UiTable.Empty;
}
