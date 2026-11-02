using SharpHook.Data;
using WhaleGenie.Core.Devices.Platform;
namespace WhaleGenie.Core.Tests;

/// <summary>
/// 停止录制的快捷键由设置决定，写法与别处的热键一样。读错的代价是录制停不下来，所以这里单独把
/// "写出来的一串字是什么意思"量一遍：哪个键结束、哪些键要一起按住、写坏了怎么办。
/// </summary>
public class RecorderShortcutTests
{
    [Fact]
    public void A_chord_stops_on_its_last_key_with_the_others_held()
    {
        var (stop, held) = InputHookRecorder.ReadShortcut("Ctrl+F10");

        Assert.Equal(SharpKeys.Key(KeyCode.VcF10), stop);
        Assert.Equal([SharpKeys.Key(KeyCode.VcLeftControl)], held);
    }

    [Fact]
    public void A_key_on_its_own_has_nothing_held_with_it()
    {
        var (stop, held) = InputHookRecorder.ReadShortcut("F9");

        Assert.Equal(SharpKeys.Key(KeyCode.VcF9), stop);
        Assert.Empty(held);
    }

    /// <summary>
    /// 写坏的或空的快捷键没有可按的键：面板上的按钮仍然能停，而录制器不该自己发明一个键 ——
    /// 那样用户按的是设置里写的那个，停下来的却是别的。
    /// </summary>
    [Fact]
    public void A_shortcut_nothing_can_read_leaves_nothing_to_press()
    {
        Assert.Null(InputHookRecorder.ReadShortcut(string.Empty).Stop);
        Assert.Null(InputHookRecorder.ReadShortcut("   ").Stop);
        Assert.Null(InputHookRecorder.ReadShortcut("没有这个键").Stop);
    }
}
