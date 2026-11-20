using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Core.Devices;

// The machine: environment, registry, sound, display and power.
/// <summary>Facts about this machine that are not a file or a device.</summary>
public interface ISystemDevice
{
    /// <summary>One named fact about the machine, such as <c>userName</c> or <c>tempFolder</c>.</summary>
    string Info(string field);

    /// <summary>An environment variable, or an empty string when it is not set.</summary>
    string Environment(string name);

    /// <summary>
    /// 写一个环境变量，回答写完之后读回来的内容。写给这台机器要管理员权限，而且会影响此后
    /// 在这台机器上启动的每一个程序；已经开着的程序读不到新值。
    /// </summary>
    string SetEnvironment(string name, string value, EnvironmentScope scope);

    /// <summary>
    /// 一个注册表值，按文本交回来；键或值不在时是空字符串。读不到不是失败：「它现在是什么」
    /// 这个问题里，「没有」也是一个答案。
    /// </summary>
    string ReadRegistry(RegistryBranch branch, string path, string valueName);

    /// <summary>
    /// 写一个注册表值，回答写完之后读回来的内容。路径上还没有的键会建出来；同一个键下别的值
    /// 不动。
    /// </summary>
    string WriteRegistry(RegistryBranch branch, string path, string valueName,
        RegistryValueType type, string data);

    /// <summary>删掉一个值。本来就不在时什么也不做，那是清理脚本最常见的写法，不是失败。</summary>
    void DeleteRegistry(RegistryBranch branch, string path, string valueName);

    /// <summary>
    /// Asks the machine to do one of the things the Start menu's power button does.
    /// <paramref name="graceSeconds"/> is how long Windows warns before restarting or shutting
    /// down, and is ignored by the rest.
    /// </summary>
    void Power(PowerAction action, int graceSeconds);

    /// <summary>The volume of the speakers Windows is using, from 0 to 100.</summary>
    int Volume();

    /// <summary>Turns the speakers up or down to a level from 0 to 100.</summary>
    void SetVolume(int percent);

    /// <summary>Whether the sound is switched off.</summary>
    bool IsMuted();

    /// <summary>Switches the sound off, or back on.</summary>
    void SetMuted(bool muted);

    /// <summary>
    /// Plays one of the sounds this machine plays for an event, so a macro that has finished, or
    /// gone wrong, can say so while nobody is looking at the screen. A machine told to keep quiet
    /// plays nothing and is left alone; one with no sound device refuses, and the step says so.
    /// </summary>
    void PlaySound(SoundKind kind);

    /// <summary>
    /// 放一个 wav 文件。宏自己带的声音比系统那五声能说的话多得多：一句「做完了」、一段提示音、
    /// 一声只有自己认得的响。文件不在就失败并说明是哪一个，而不是安静地什么都不放。
    /// </summary>
    void PlaySoundFile(string path);

    /// <summary>
    /// Shows a notification beside the notification area, so a macro can say something happened
    /// while the window is out of the way. A machine with its notifications switched off shows
    /// nothing and is left alone; one where the program has nowhere to show it refuses, and the
    /// step says so.
    /// </summary>
    void Notify(string title, string text, NotificationKind kind);

    /// <summary>The keyboard layout the focused window is typing in, said the way a person would.</summary>
    string InputMethod();

    /// <summary>Every keyboard layout installed on this machine, said the same way.</summary>
    IReadOnlyList<string> InputMethods();

    /// <summary>
    /// Switches the focused window to the layout whose name matches and says which one it ended up
    /// on, or null when the machine has no such layout.
    /// </summary>
    string? SwitchInputMethod(string layout);

    /// <summary>The brightness of the screens, from 0 to 100.</summary>
    int Brightness();

    /// <summary>Turns the screens up or down to a brightness from 0 to 100.</summary>
    void SetBrightness(int percent);
}
