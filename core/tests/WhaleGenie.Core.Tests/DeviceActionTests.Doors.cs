using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

// The doors a run takes before input or a browser: holding, waiting and being let through.
public partial class DeviceActionTests
{

    /// <summary>One run that presses a key and holds it.</summary>
    private static Task<RunResult> Pressing(FakeDeviceLayer devices, string key, string holdMs,
        string name)
        => new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
        {
            RunName = name,
        }.RunAsync([Step("input.keyPress", Param("key", key), Param("holdMs", holdMs))]);

    /// <summary>The same key, inside the block that is what takes the one input door.</summary>
    private static Task<RunResult> Holding(FakeDeviceLayer devices, string key, string holdMs,
        string name)
        => new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
        {
            RunName = name,
        }.RunAsync(
        [
            Step("control.holdInput", Body("steps",
                Step("input.keyPress", Param("key", key), Param("holdMs", holdMs)))),
        ]);

    /// <summary>
    /// 「独占输入」块会等在门外：别人还在发的时候，它不会硬挤进去。谁先谁后不能靠运气 —— 收到
    /// 输入的那一方会看到两个宏的按键穿插在一起。
    /// </summary>
    [Fact]
    public async Task A_hold_waits_at_the_door_while_another_run_is_inside()
    {
        var devices = new FakeDeviceLayer();
        var inside = await SharedDoors.Input.Take("别人", default);
        var run = Holding(devices, "A", "10", "第一个");

        try
        {
            await Task.Delay(100);

            Assert.False(run.IsCompleted);
            Assert.Empty(devices.Calls);
            Assert.Equal("别人", SharedDoors.Input.Owner);
        }
        finally
        {
            inside.Dispose();
        }

        await run;

        Assert.Equal(["keyPress A 10"], devices.Calls);

        // 发完就把门交回去，下一个才进得来。
        Assert.Equal(string.Empty, SharedDoors.Input.Owner);
    }

    /// <summary>
    /// 没有「独占输入」块的运行照发：门只拦写了块的那一段，两条宏同时跑时谁也不为谁停下来。
    /// </summary>
    [Fact]
    public async Task A_run_without_a_hold_goes_straight_through()
    {
        var devices = new FakeDeviceLayer();
        using var inside = await SharedDoors.Input.Take("别人", default);

        await Pressing(devices, "A", "10", "第一个");

        Assert.Equal(["keyPress A 10"], devices.Calls);
        Assert.Equal("别人", SharedDoors.Input.Owner);
    }

    /// <summary>
    /// 浏览器只有一份，当前页也只有一份：另一条宏正在切页时，这一步要等，不能在别人两件事中间
    /// 把页面换掉。
    /// </summary>
    [Fact]
    public async Task A_browser_step_waits_at_the_browser_door_while_another_run_is_inside()
    {
        var devices = new FakeDeviceLayer();
        using var inside = await SharedDoors.Browser.Take("别人", default);
        var run = new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
            .RunAsync([Step("browser.switchTab", Param("tabHow", "newest"))]);

        try
        {
            await Task.Delay(100);

            Assert.False(run.IsCompleted);
            Assert.DoesNotContain(devices.Calls, call => call.StartsWith("browserSwitchTab"));
            Assert.Equal("别人", SharedDoors.Browser.Owner);
        }
        finally
        {
            inside.Dispose();
        }

        await run;

        Assert.Contains(devices.Calls, call => call.StartsWith("browserSwitchTab"));
        Assert.Equal(string.Empty, SharedDoors.Browser.Owner);
    }

    /// <summary>
    /// 输入门与浏览器门是两件事：一条宏在用浏览器时，另一条仍可以发键鼠，不必等它看完页面。
    /// </summary>
    [Fact]
    public async Task The_browser_door_does_not_hold_back_input()
    {
        var devices = new FakeDeviceLayer();
        using var browser = await SharedDoors.Browser.Take("别人", default);

        await Pressing(devices, "A", "10", "第一个");

        Assert.Equal(["keyPress A 10"], devices.Calls);
        Assert.Equal("别人", SharedDoors.Browser.Owner);
    }

    /// <summary>
    /// 反过来也一样：别人在发输入时，浏览器步骤照样进去，不需要那条输入门。
    /// </summary>
    [Fact]
    public async Task The_input_door_does_not_hold_back_the_browser()
    {
        var devices = new FakeDeviceLayer();
        using var input = await SharedDoors.Input.Take("别人", default);

        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
        {
            RunName = "第一个",
        }.RunAsync([Step("browser.switchTab", Param("tabHow", "newest"))]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains(devices.Calls, call => call.StartsWith("browserSwitchTab"));
        Assert.Equal("别人", SharedDoors.Input.Owner);
    }

    /// <summary>
    /// 脚本整段放在「独占输入」块里时跟着块走：块在门口等一次，脚本不会自己再取一次门。脚本在
    /// 别的进程里直接发的键鼠仍旧拦不住，那是脚本自己的事。
    /// </summary>
    [Fact]
    public async Task A_script_inside_a_hold_runs_under_the_same_door()
    {
        var devices = new FakeDeviceLayer();
        devices.Script.AutoHotkey = new ScriptResult(0, string.Empty, string.Empty, Value.Null);
        devices.Files.PathExists = true;

        var run = new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
        {
            RunName = "脚本",
        }.RunAsync(
        [
            Step("control.holdInput", Body("steps",
                Step("script.ahk", Param("sendsInput", "true"), Param("script", "Print(\"hi\")")))),
        ]);

        // A deadlock would never finish; five seconds is far longer than the step needs.
        var finished = await Task.WhenAny(run, Task.Delay(5000));
        Assert.Same(run, finished);
        Assert.True((await run).Succeeded);
        Assert.Equal(string.Empty, SharedDoors.Input.Owner);
    }

    [Fact]
    public async Task A_held_input_runs_its_whole_body_without_taking_the_door_again()
    {
        var devices = new FakeDeviceLayer();
        var store = new VariableStore();
        var result = await new MacroRunner(store, new SilentRunHost(), devices)
            .RunAsync(
        [
            Step("control.holdInput", Body("steps",
                Step("input.keyPress", Param("key", "F5")),
                Step("input.keyPress", Param("key", "F6")))),
        ]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("keyPress F5 0", devices.Calls);
        Assert.Contains("keyPress F6 0", devices.Calls);
    }

    [Fact]
    public async Task A_hold_inside_the_same_hold_does_not_wait_for_itself()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("control.holdInput", Body("steps",
                Step("control.holdInput", Body("steps",
                    Step("input.keyPress", Param("key", "F7")))))),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains("keyPress F7 0", devices.Calls);
    }

    [Fact]
    public async Task A_held_browser_runs_its_page_steps_as_one_turn()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("control.holdBrowser", Body("steps",
                Step("browser.click", Param("pageSelector", "#one")),
                Step("browser.click", Param("pageSelector", "#two")))),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(2, devices.Calls.Count(call =>
            call.StartsWith("browserClick", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_step_can_wait_for_a_key_to_be_pressed()
    {
        var devices = new FakeDeviceLayer();
        var store = new VariableStore();
        var run = new MacroRunner(store, new SilentRunHost(), devices).RunAsync(
        [
            Within(5000, Step("input.waitKey", Param("key", "F8"), Param("swallow", "true"),
                Returns("key=pressed", "injected=byTool"))),
        ]);

        for (var look = 0; look < 200 && !devices.HookEvents.HasKeyDown; look++)
        {
            await Task.Delay(5);
        }

        if (!devices.HookEvents.HasKeyDown)
        {
            var stopped = await run;
            Assert.Fail($"{stopped.Status} {stopped.Key} {stopped.Detail}");
        }

        devices.HookEvents.Press("F8");

        var result = await run;
        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("F8", store.Local.Values["pressed"].AsText());
        Assert.False(store.Local.Values["byTool"].AsBool());
        Assert.True(devices.HookEvents.Last!.Suppress);
    }
}
