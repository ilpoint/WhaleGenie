using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Tests;

// The machine itself: environment, registry, displays, power, sound and brightness.
public partial class DeviceActionTests
{
    [Fact]
    public async Task System_info_and_environment_variables_reach_variables()
    {
        var devices = new FakeDeviceLayer();
        devices.System.Values["desktopFolder"] = @"C:\Users\me\Desktop";
        devices.System.EnvironmentValues["TEMP"] = @"C:\Temp";
        var (_, _, store) = await RunAsync(
        [
            Step("system.info", Param("field", "desktopFolder"), Returns("value=desk")),
            Step("system.environment", Param("environmentName", "TEMP"), Returns("value=temp")),
        ], devices);

        Assert.Equal(@"C:\Users\me\Desktop", store.Local.Values["desk"].AsText());
        Assert.Equal(@"C:\Temp", store.Local.Values["temp"].AsText());
    }


    /// <summary>写进去的环境变量，下一步就该读得到：这也是「新开的程序看得见」在测试里能说的那一半。</summary>
    [Fact]
    public async Task An_environment_variable_is_written_and_read_back_by_the_next_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, store) = await RunAsync(
        [
            Step("system.setEnvironment", Param("environmentName", "BENCH"),
                Param("environmentValue", "one")),
            Step("system.environment", Param("environmentName", "BENCH"), Returns("value=back")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("setEnv User BENCH one", devices.Calls);
        Assert.Equal("one", store.Local.Values["back"].AsText());
    }

    /// <summary>写给这台机器是另一件事：没人在场问、或者人说了不要，设备根本不该被叫到。</summary>
    [Fact]
    public async Task Writing_the_machine_environment_without_agreement_does_not_reach_the_device()
    {
        var devices = new FakeDeviceLayer();
        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
            .RunAsync(
            [
                Step("system.setEnvironment", Param("environmentScope", "machine"),
                    Param("environmentName", "BENCH"), Param("environmentValue", "one")),
            ]);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NotConfirmed", result.Key);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("setEnv", StringComparison.Ordinal));
    }

    /// <summary>机器不肯写的时候，失败里说的是哪个变量、写给谁。</summary>
    [Fact]
    public async Task An_environment_variable_the_machine_refuses_says_which_one()
    {
        var devices = new FakeDeviceLayer();
        devices.System.EnvironmentRefusal =
            new DeviceActionException("Run.EnvironmentRefused", "BENCH (machine)");

        var (result, _, _) = await RunAsync(
        [
            Step("system.setEnvironment", Param("environmentScope", "machine"),
                Param("environmentName", "BENCH"), Param("environmentValue", "one")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.EnvironmentRefused", result.Key);
    }

    /// <summary>
    /// 写进去的注册表值要能原样读回来，读回来的那一次也交到变量里：宏写完不必再猜自己写成了什么。
    /// </summary>
    [Fact]
    public async Task A_registry_value_written_is_read_back_by_the_next_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, store) = await RunAsync(
        [
            Step("system.writeRegistry", Param("registryPath", "Software\\Bench"),
                Param("valueName", "Setting"), Param("registryType", "string"),
                Param("registryData", "one")),
            Step("system.readRegistry", Param("registryPath", "Software\\Bench"),
                Param("valueName", "Setting"), Returns("value=back")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(@"writeRegistry CurrentUser\Software\Bench\Setting String one", devices.Calls);
        Assert.Contains(@"readRegistry CurrentUser\Software\Bench\Setting", devices.Calls);
        Assert.Equal("one", store.Local.Values["back"].AsText());
    }

    /// <summary>32 位数字按目录那一档交下去，宏不必自己把它当文本再算一遍。</summary>
    [Fact]
    public async Task A_registry_number_is_written_as_a_number()
    {
        var devices = new FakeDeviceLayer();

        await RunAsync(
        [
            Step("system.writeRegistry", Param("registryPath", "Software\\Bench"),
                Param("valueName", "Count"), Param("registryType", "dword"),
                Param("registryData", "7")),
        ], devices);

        Assert.Contains(@"writeRegistry CurrentUser\Software\Bench\Count DWord 7", devices.Calls);
    }

    /// <summary>
    /// 写到这台机器是回不去的一步：没人在场、或者人说了不要，设备根本不该被叫到。
    /// </summary>
    [Fact]
    public async Task Writing_the_machine_hive_without_agreement_does_not_reach_the_device()
    {
        var devices = new FakeDeviceLayer();
        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
            .RunAsync(
            [
                Step("system.writeRegistry", Param("hive", "localMachine"),
                    Param("registryPath", "Software\\Bench"), Param("valueName", "Setting"),
                    Param("registryData", "one")),
            ]);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NotConfirmed", result.Key);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("writeRegistry", StringComparison.Ordinal));
    }

    /// <summary>删一个本来就不在的值是清理脚本最常见的写法，不该算失败。</summary>
    [Fact]
    public async Task Deleting_a_registry_value_that_is_not_there_is_left_alone()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("system.deleteRegistry", Param("registryPath", "Software\\Bench"),
                Param("valueName", "Gone")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(@"deleteRegistry CurrentUser\Software\Bench\Gone", devices.Calls);
    }

    /// <summary>机器不肯改的时候，失败里说的是哪一处被拒，而不是一句「失败了」。</summary>
    [Fact]
    public async Task A_registry_the_machine_refuses_says_where()
    {
        var devices = new FakeDeviceLayer();
        devices.System.RegistryRefusal =
            new DeviceActionException("Run.RegistryRefused", @"HKEY_CURRENT_USER\Software\Bench");

        var (result, _, _) = await RunAsync(
        [
            Step("system.readRegistry", Param("registryPath", "Software\\Bench"),
                Param("valueName", "Setting"), Returns("value=back")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.RegistryRefused", result.Key);
    }

    [Fact]
    public async Task Listing_the_displays_hands_over_one_record_to_a_display()
    {
        // Two displays side by side, the second one to the right of the primary: a macro that has
        // to work over there reads where "over there" is instead of having one machine's numbers
        // written into it. A display to the left of the primary one would start at a negative
        // number, and that is exactly what the step has to hand over.
        var devices = new FakeDeviceLayer();
        devices.Screen.Desktop = new ScreenLayout(
                new ScreenArea(0, 0, 300, 100),
                [
                    new ScreenDisplay(new ScreenArea(0, 0, 100, 100), true),
                    new ScreenDisplay(new ScreenArea(100, 0, 200, 100), false),
                ]);
        var (_, _, store) = await RunAsync(
            [Step("screen.list", Returns("displays=desk"))], devices);

        var displays = store.Local.Values["desk"].AsList();
        Assert.Equal(2, displays.Count);
        Assert.Equal("0,0,100,100", displays[0].Field("box")!.AsText());
        Assert.Equal("100,0,200,100", displays[1].Field("box")!.AsText());
        Assert.Equal("100", displays[1].Field("left")!.AsText());
        Assert.Equal("200", displays[1].Field("width")!.AsText());
        Assert.Equal("300", displays[1].Field("right")!.AsText());
        Assert.True(displays[0].Field("primary")!.AsBool());
        Assert.False(displays[1].Field("primary")!.AsBool());

        // 每一块显示器也带着自己的中心，跟在别处从矩形算出来的一样。
        Assert.Equal("50,50", MiddleOf(displays[0]));
        Assert.Equal("200,50", MiddleOf(displays[1]));
    }

    [Fact]
    public async Task The_power_action_asks_for_the_thing_the_macro_picked()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("system.power", Param("powerAction", "shutDown"), Param("graceSeconds", "30")),
            Step("system.power", Param("powerAction", "monitorOff")),
        ], devices);

        Assert.Equal(["power ShutDown 30", "power MonitorOff 0"], devices.Calls);
    }

    [Fact]
    public async Task The_power_action_locks_the_screen_when_it_is_not_told_what_to_do()
    {
        // The gentlest of the lot, because a step that was added and left alone should not be able
        // to close anything down.
        var devices = new FakeDeviceLayer();
        await RunAsync([Step("system.power")], devices);

        Assert.Equal(["power Lock 0"], devices.Calls);
    }

    [Fact]
    public async Task The_power_action_refuses_a_name_it_does_not_know()
    {
        // Locking the screen would be a strange thing to do on the way to shutting a machine down,
        // so a mistyped name stops the step instead of quietly turning into a different action.
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.power", Param("powerAction", "shutdownnow"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownPowerAction", result.Key);
        Assert.Empty(Asked(devices));
    }

    [Fact]
    public async Task The_volume_action_leaves_behind_the_level_the_machine_ended_at()
    {
        var devices = new FakeDeviceLayer();
        devices.System.SpeakerVolume = 30;
        var (_, _, store) = await RunAsync(
        [
            Step("system.volume", Param("volumeAction", "set"), Param("levelPercent", "80"),
                Returns("volume=was")),
            Step("system.volume", Param("volumeAction", "down"), Param("stepPercent", "15"),
                Returns("volume=now")),
        ], devices);

        Assert.Equal(80d, store.Local.Values["was"].Number);
        Assert.Equal(65d, store.Local.Values["now"].Number);
        Assert.Contains("setVolume 80", devices.Calls);
        Assert.Contains("setVolume 65", devices.Calls);
    }

    [Fact]
    public async Task Turning_the_volume_down_never_goes_below_nothing()
    {
        var devices = new FakeDeviceLayer();
        devices.System.SpeakerVolume = 4;
        var (_, _, store) = await RunAsync(
        [
            Step("system.volume", Param("volumeAction", "down"), Param("stepPercent", "20"),
                Returns("volume")),
        ], devices);

        // The device clamps as well, so a macro cannot talk the machine into a volume it cannot have.
        Assert.Equal(0d, store.Local.Values["volume"].Number);
    }

    [Fact]
    public async Task Muting_says_which_way_the_sound_went()
    {
        var devices = new FakeDeviceLayer();
        var host = new SilentRunHost();

        await new MacroRunner(new VariableStore(), host, devices).RunAsync(
        [
            Step("system.volume", Param("volumeAction", "toggleMute")),
            Step("system.volume", Param("volumeAction", "toggleMute")),
        ]);

        // The level does not change when the sound is switched off, so each way is said out loud
        // rather than leaving a reader to work it out from a number that never moved.
        Assert.Single(host.Entries, entry => entry.Key == "Log.SoundOff");
        Assert.Single(host.Entries, entry => entry.Key == "Log.SoundOn");
    }

    [Fact]
    public async Task The_volume_action_refuses_a_name_it_does_not_know()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.volume", Param("volumeAction", "louder"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownVolumeAction", result.Key);
        Assert.Empty(Asked(devices));
    }

    [Fact]
    public async Task The_sound_action_plays_the_sound_the_macro_picked()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("system.sound", Param("sound", "error")),
            Step("system.sound"),
        ], devices);

        // A step added and left alone plays the machine's own default sound rather than a particular
        // one, so nothing in a macro shouts by accident.
        Assert.Equal(["sound Error", "sound Default"], devices.Calls);
    }

    [Fact]
    public async Task The_sound_action_refuses_a_name_it_does_not_know()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.sound", Param("sound", "fanfare"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownSound", result.Key);
        Assert.Empty(Asked(devices));
    }

    /// <summary>
    /// 宏自己带的 wav 文件也放得了。相对路径按与别处相同的规则解析——这一步收到的应该是解析之后的
    /// 全路径，而不是把相对路径原样交给 Windows，那会按程序自己的工作目录去找，找不到。
    /// </summary>
    [Fact]
    public async Task A_sound_can_be_the_file_the_macro_brings()
    {
        var devices = new FakeDeviceLayer();

        await RunAsync(
        [
            Step("system.sound", Param("soundSource", "file"), Param("soundFile", @".\done.wav")),
        ], devices);

        Assert.Contains(devices.Calls, call =>
            call.StartsWith("soundFile ", StringComparison.Ordinal)
            && call.EndsWith("done.wav", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_sound_file_named_nowhere_fails_the_step_instead_of_playing_nothing()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.sound", Param("soundSource", "file"), Param("soundFile", "  "))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingPath", result.Key);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("soundFile", StringComparison.Ordinal));
    }

    /// <summary>
    /// 显示桌面：把没收起来的都收起来，已经收起来的不必再叫一次；反过来做就是把收起来的放回去。
    /// 任务是按窗口一个一个做的，所以每一下都要有回答，而没有标题的窗口不在名单里。
    /// </summary>
    [Fact]
    public async Task The_desktop_can_be_cleared_and_the_windows_brought_back()
    {
        var devices = new FakeDeviceLayer();
        devices.Window.Open.Add(new WindowInfo(1, "Editor",
            new ScreenPoint(0, 0), new ScreenSize(800, 600), false, false));
        devices.Window.Open.Add(new WindowInfo(2, "Browser",
            new ScreenPoint(0, 0), new ScreenSize(800, 600), false, false));
        devices.Window.Open.Add(new WindowInfo(3, "Already small",
            new ScreenPoint(0, 0), new ScreenSize(800, 600), true, false));

        await RunAsync([Step("system.showDesktop")], devices);

        Assert.Equal(["listWindows", "minimizeWindow 1", "minimizeWindow 2"], devices.Calls);

        devices.Window.Open.Clear();
        devices.Calls.Clear();
        devices.Window.Open.Add(new WindowInfo(4, "Editor",
            new ScreenPoint(0, 0), new ScreenSize(800, 600), true, false));
        devices.Window.Open.Add(new WindowInfo(5, "Browser",
            new ScreenPoint(0, 0), new ScreenSize(800, 600), false, false));

        await RunAsync([Step("system.showDesktop", Param("desktopAction", "restoreAll"))], devices);

        Assert.Equal(["listWindows", "restoreWindow 4"], devices.Calls);
    }

    [Fact]
    public async Task Clearing_the_desktop_refuses_a_word_it_does_not_know()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.showDesktop", Param("desktopAction", "tidy"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownDesktopAction", result.Key);

        // 认不出这个词时什么都没有碰：窗口一个也没动。
        Assert.Empty(Asked(devices));
    }

    [Fact]
    public async Task The_notify_action_shows_what_the_macro_asked_for()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("system.notify",
                Param("heading", "Backup"), Param("notificationText", "All files copied."),
                Param("notificationKind", "warning")),
            Step("system.notify", Param("notificationText", "Done.")),
        ], devices);

        // A step added and left alone is an ordinary note rather than a warning, and its heading is
        // the program's own name, which the notification area fills in.
        Assert.Equal(
            ["notify Warning [Backup] All files copied.", "notify Information [] Done."],
            devices.Calls);
    }

    [Fact]
    public async Task The_notify_action_refuses_a_kind_it_does_not_know()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.notify", Param("notificationText", "Hello"), Param("notificationKind", "fanfare"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownNotification", result.Key);
        Assert.Empty(Asked(devices));
    }

    [Fact]
    public async Task The_input_method_action_reads_the_layout_the_focused_window_is_using()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("system.ime", Param("imeAction", "get"), Returns("ime=which")),
            Step("system.ime", Param("imeAction", "list"), Returns("ime=all", "layouts=names")),
        ], devices);

        Assert.Equal("中文(简体) - 微软拼音", store.Local.Values["which"].AsText());

        // The list is what the layout field can be filled in from, so it is the same wording.
        Assert.Equal("中文(简体) - 微软拼音;英语(美国)", store.Local.Values["all"].AsText());
        Assert.Equal(["中文(简体) - 微软拼音", "英语(美国)"],
            store.Local.Values["names"].Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task The_input_method_action_switches_to_a_layout_by_part_of_its_name()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("system.ime", Param("imeAction", "switch"), Param("layout", "英语"),
                Returns("ime=now")),
        ], devices);

        // The variable holds the layout the window really ended up on, which is the name the device
        // matched rather than the part the macro typed.
        Assert.Equal("英语(美国)", store.Local.Values["now"].AsText());
        Assert.Contains("switchInputMethod 英语", devices.Calls);
    }

    [Fact]
    public async Task The_input_method_action_says_so_when_no_layout_matches()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.ime", Param("imeAction", "switch"), Param("layout", "法语"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.LayoutNotFound", result.Key);
        Assert.Contains("法语", result.Detail);
    }

    [Fact]
    public async Task The_input_method_action_refuses_a_name_it_does_not_know()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.ime", Param("imeAction", "chinese"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownInputMethodAction", result.Key);
        Assert.Empty(Asked(devices));
    }

    [Fact]
    public async Task The_brightness_action_leaves_behind_the_brightness_the_screens_ended_at()
    {
        var devices = new FakeDeviceLayer();
        devices.System.ScreenBrightness = 20;
        var (_, _, store) = await RunAsync(
        [
            Step("system.brightness", Param("brightnessAction", "up"), Param("stepPercent", "30"),
                Returns("brightness=up")),
            Step("system.brightness", Param("brightnessAction", "down"), Param("stepPercent", "15"),
                Returns("brightness=down")),
        ], devices);

        Assert.Equal(50d, store.Local.Values["up"].Number);
        Assert.Equal(35d, store.Local.Values["down"].Number);
        Assert.Contains("setBrightness 50", devices.Calls);
        Assert.Contains("setBrightness 35", devices.Calls);
    }

    [Fact]
    public async Task The_brightness_action_reads_without_changing_anything()
    {
        var devices = new FakeDeviceLayer();
        devices.System.ScreenBrightness = 65;
        var (_, _, store) = await RunAsync(
            [Step("system.brightness", Returns("brightness"))], devices);

        Assert.Equal(65d, store.Local.Values["brightness"].Number);

        // Reading is the whole step, so the screens are asked exactly once.
        Assert.Equal(["brightness"], devices.Calls);
    }

    [Fact]
    public async Task The_brightness_action_refuses_a_name_it_does_not_know()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("system.brightness", Param("brightnessAction", "darker"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownBrightnessAction", result.Key);
        Assert.Empty(Asked(devices));
    }
}
