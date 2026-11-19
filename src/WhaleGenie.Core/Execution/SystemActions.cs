using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The machine steps: what this computer is, its screens, environment, registry, power, sound,
/// notifications, input layout and brightness. They read or change the machine rather than a
/// window, and every one of them leaves behind what the machine ended up at where that makes
/// sense, so a macro can put it back.
/// </summary>
internal static class SystemActions
{
    /// <summary>
    /// The system actions, each answered by the method written for it. A step of another family
    /// is not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["system.info"] = static (run, step, depth, _) =>
            {
                Info(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["screen.list"] = static (run, step, depth, _) =>
            {
                Displays(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.environment"] = static (run, step, depth, _) =>
            {
                EnvironmentVariable(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.setEnvironment"] = static (run, step, depth, _) =>
            {
                SetEnvironment(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.readRegistry"] = static (run, step, depth, _) =>
            {
                ReadRegistry(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.writeRegistry"] = static (run, step, depth, _) =>
            {
                WriteRegistry(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.deleteRegistry"] = static (run, step, depth, _) =>
            {
                DeleteRegistry(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.power"] = static (run, step, depth, _) =>
            {
                Power(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.showDesktop"] = static (run, step, depth, _) =>
            {
                ShowDesktop(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.volume"] = static (run, step, depth, _) =>
            {
                Volume(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.sound"] = static (run, step, depth, _) =>
            {
                PlaySound(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.notify"] = static (run, step, depth, _) =>
            {
                Notify(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.ime"] = static (run, step, depth, _) =>
            {
                InputMethod(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["system.brightness"] = static (run, step, depth, _) =>
            {
                Brightness(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },
        };

    /// <summary>Reads a fact about the machine into a variable.</summary>
    private static void Info(IRunContext run, ExecutableStep step, int depth)
    {
        var value = run.Devices.System.Info(run.Args(step).Text("field").Trim());
        run.Store(step, "value", Value.FromText(value), depth);
    }

    /// <summary>
    /// How the desk is laid out, one record to a display, so a macro that has to work on the second
    /// screen can find out where it is instead of having the numbers written into it. The rectangle
    /// is in desktop coordinates, which is what every other coordinate in a macro is written in —
    /// a display to the left of the primary one starts at a negative number, and that is exactly the
    /// number a step aiming at it needs. The middle of it is in the record as well, so aiming at
    /// "the middle of that screen" is one field rather than four numbers and an arithmetic problem.
    /// </summary>
    private static void Displays(IRunContext run, ExecutableStep step, int depth)
    {
        var desk = run.Devices.Screen.Desktop;

        run.Give(step, "displays", Value.FromList(
        [
            .. desk.Displays.Select(display =>
            {
                var area = display.Bounds;
                var middle = ScreenMath.Middle(new ScreenPoint(area.Left, area.Top),
                    new ScreenSize(area.Width, area.Height));

                return Value.FromRecord(
                [
                    (FieldNames.X, Value.FromNumber(middle.X)),
                    (FieldNames.Y, Value.FromNumber(middle.Y)),
                    (FieldNames.Left, Value.FromNumber(area.Left)),
                    (FieldNames.Top, Value.FromNumber(area.Top)),
                    (FieldNames.Width, Value.FromNumber(area.Width)),
                    (FieldNames.Height, Value.FromNumber(area.Height)),
                    (FieldNames.Right, Value.FromNumber(area.Right)),
                    (FieldNames.Bottom, Value.FromNumber(area.Bottom)),
                    (FieldNames.Primary, Value.FromBool(display.Primary)),
                    (FieldNames.Box, Value.FromText($"{area.Left},{area.Top},{area.Width},{area.Height}")),
                ]);
            }),
        ]));

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ListedDisplays,
            desk.Displays.Count, $"{desk.Bounds.Width}x{desk.Bounds.Height}");
    }

    /// <summary>Reads an environment variable into a variable.</summary>
    private static void EnvironmentVariable(IRunContext run, ExecutableStep step, int depth)
    {
        var name = run.Args(step).ValueOf("environmentName").AsText();
        var value = run.Devices.System.Environment(name);
        run.Store(step, "value", Value.FromText(value), depth);
    }

    /// <summary>
    /// 写一个环境变量，并交回写完之后读到的内容。写给这台机器要管理员权限，而且此后在这台
    /// 机器上启动的每个程序都会看到它；已经在运行的程序读不到。
    /// </summary>
    private static void SetEnvironment(IRunContext run, ExecutableStep step, int depth)
    {
        var scope = run.Args(step).Text("environmentScope").Trim()
            .Equals("machine", StringComparison.OrdinalIgnoreCase)
            ? EnvironmentScope.Machine
            : EnvironmentScope.User;

        var name = run.Args(step).ValueOf("environmentName").AsText();
        var value = run.Args(step).ValueOf("environmentValue").AsText();

        var written = run.Devices.System.SetEnvironment(name, value, scope);
        run.Store(step, "value", Value.FromText(written), depth);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.SetEnvironment, scope.ToString(), name);
    }

    /// <summary>读一个注册表值。键或值不在时交回空字符串，不在这里失败。</summary>
    private static void ReadRegistry(IRunContext run, ExecutableStep step, int depth)
    {
        var branch = BranchOf(run, step);
        var path = run.Args(step).Text("registryPath");
        var name = run.Args(step).ValueOf("valueName").AsText();

        var value = run.Devices.System.ReadRegistry(branch, path, name);
        run.Store(step, "value", Value.FromText(value), depth);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.RegistryRead, branch.ToString(), path, name);
    }

    /// <summary>写一个注册表值，并交回写完之后读到的内容：宏不必再问一遍才知道成了什么。</summary>
    private static void WriteRegistry(IRunContext run, ExecutableStep step, int depth)
    {
        var branch = BranchOf(run, step);
        var path = run.Args(step).Text("registryPath");
        var name = run.Args(step).ValueOf("valueName").AsText();
        var type = TypeOf(run, step);
        var data = run.Args(step).ValueOf("registryData").AsText();

        var value = run.Devices.System.WriteRegistry(branch, path, name, type, data);
        run.Store(step, "value", Value.FromText(value), depth);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.RegistryWritten, branch.ToString(), path, name);
    }

    /// <summary>删一个注册表值。值本来就不在时什么也不做，那不算失败。</summary>
    private static void DeleteRegistry(IRunContext run, ExecutableStep step, int depth)
    {
        var branch = BranchOf(run, step);
        var path = run.Args(step).Text("registryPath");
        var name = run.Args(step).ValueOf("valueName").AsText();

        run.Devices.System.DeleteRegistry(branch, path, name);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.RegistryDeleted, branch.ToString(), path, name);
    }

    /// <summary>
    /// 哪一处根。目录的选项表只给两个词，所以认不出来时落到「当前用户」这一档：宏要改的是自己
    /// 看得见的那一份，改错了也改得回来。
    /// </summary>
    private static RegistryBranch BranchOf(IRunContext run, ExecutableStep step)
        => run.Args(step).Text("hive").Trim().Equals("localMachine", StringComparison.OrdinalIgnoreCase)
            ? RegistryBranch.LocalMachine
            : RegistryBranch.CurrentUser;

    /// <summary>写进去的是哪一档值。认不出来时落到文本，这是最不会把人绊住的那一档。</summary>
    private static RegistryValueType TypeOf(IRunContext run, ExecutableStep step)
        => run.Args(step).Text("registryType").Trim().ToLowerInvariant() switch
        {
            "dword" => RegistryValueType.DWord,
            "expandstring" => RegistryValueType.ExpandString,
            _ => RegistryValueType.String,
        };

    /// <summary>
    /// Asks the machine to do one of the things the Start menu's power button does. The name is
    /// read strictly rather than falling back to something: a macro that mistyped "shutdown" should
    /// not have its screen locked instead, and one that mistyped "lock" should certainly not be
    /// turning the machine off.
    /// </summary>
    private static void Power(IRunContext run, ExecutableStep step, int depth)
    {
        var wanted = run.Args(step).Text("powerAction").Trim().ToLowerInvariant();
        var action = wanted switch
        {
            "" or "lock" => PowerAction.Lock,
            "monitoroff" or "monitor" => PowerAction.MonitorOff,
            "signout" or "logoff" => PowerAction.SignOut,
            "sleep" => PowerAction.Sleep,
            "hibernate" => PowerAction.Hibernate,
            "restart" or "reboot" => PowerAction.Restart,
            "shutdown" => PowerAction.ShutDown,
            "abortshutdown" or "abort" => PowerAction.AbortShutdown,
            _ => throw new StepFailure(RunKeys.UnknownPowerAction, wanted),
        };

        var grace = run.Args(step).Number("graceSeconds");
        run.Devices.System.Power(action, grace);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.Power, wanted.Length == 0 ? "lock" : wanted,
            grace);
    }

    /// <summary>
    /// Reads or changes the volume of the speakers Windows is using. Whatever the step asked for,
    /// what it leaves behind is the level the machine ended up at, so a macro that turned the sound
    /// down can put it back the way it found it.
    /// </summary>
    private static void Volume(IRunContext run, ExecutableStep step, int depth)
    {
        var what = run.Args(step).Text("volumeAction").Trim().ToLowerInvariant();
        switch (what)
        {
            // A step written before this field existed means the same as the editor's own default:
            // read the level and leave the machine alone.
            case "":
            case "get":
                break;

            case "set":
                run.Devices.System.SetVolume(run.Args(step).Number("levelPercent"));
                break;

            case "up":
            case "down":
                var move = run.Args(step).Number("stepPercent");
                run.Devices.System.SetVolume(
                    run.Devices.System.Volume() + (what == "up" ? move : -move));
                break;

            case "mute":
                run.Devices.System.SetMuted(true);
                break;

            case "unmute":
                run.Devices.System.SetMuted(false);
                break;

            case "togglemute":
                run.Devices.System.SetMuted(!run.Devices.System.IsMuted());
                break;

            default:
                throw new StepFailure(RunKeys.UnknownVolumeAction, what);
        }

        var level = run.Devices.System.Volume();
        run.Give(step, "volume", Value.FromNumber(level));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.Volume, level);

        // Switching the sound off does not change the level, so the level alone would leave a reader
        // wondering whether the macro had done anything at all.
        if (what is "mute" or "unmute" or "togglemute")
        {
            run.Log(LogLevel.Info, depth, step.Type,
                run.Devices.System.IsMuted() ? LogKeys.SoundOff : LogKeys.SoundOn);
        }
    }

    /// <summary>
    /// Plays a sound where nobody is looking at the screen: one of the machine's own event sounds,
    /// or a wav file the macro brings with it, so a macro that has finished, or gone wrong, can say
    /// so out loud.
    /// </summary>
    private static void PlaySound(IRunContext run, ExecutableStep step, int depth)
    {
        // 放一个宏自己带的文件时，「哪一种提示音」那一格就不作数了：两者是同一个问题的两种答案，
        // 界面上也只有一个看得见。
        if (string.Equals(run.Args(step).Text("soundSource").Trim(), "file",
                StringComparison.OrdinalIgnoreCase))
        {
            var named = run.Args(step).ValueOf("soundFile").AsText().Trim();
            if (named.Length == 0)
            {
                throw new StepFailure(RunKeys.MissingPath);
            }

            run.Devices.System.PlaySoundFile(run.Devices.Files.Resolve(named));
            run.Log(LogLevel.Info, depth, step.Type, LogKeys.PlayedSound, named);
            return;
        }

        var what = run.Args(step).Text("sound").Trim().ToLowerInvariant();
        var kind = what switch
        {
            "" or "default" => SoundKind.Default,
            "information" or "info" => SoundKind.Information,
            "warning" => SoundKind.Warning,
            "error" => SoundKind.Error,
            "question" => SoundKind.Question,
            _ => throw new StepFailure(RunKeys.UnknownSound, what),
        };

        run.Devices.System.PlaySound(kind);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.PlayedSound,
            what.Length == 0 ? "default" : what);
    }

    /// <summary>
    /// 把所有窗口收起来，或者把收起来的放回去。认的是「有标题、看得见的顶层窗口」，也就是
    /// 任务栏上排着的那些；桌面本身与工具栏不算窗口，不会被收起来，所以清出来的桌面是真的空的。
    /// 窗口一个一个地叫，而不是请系统一次做完：这样每一步的成败都看得见，也不会因为某个窗口
    /// 不理会请求，整件事就无声无息。
    /// </summary>
    private static void ShowDesktop(IRunContext run, ExecutableStep step, int depth)
    {
        var what = run.Args(step).Text("desktopAction").Trim().ToLowerInvariant();
        var restore = what switch
        {
            "" or "minimizeall" => false,
            "restoreall" => true,
            _ => throw new StepFailure(RunKeys.UnknownDesktopAction, what),
        };

        var moved = 0;
        foreach (var window in run.Devices.Windows.List())
        {
            // 已经在那一边的窗口不用碰：放回去的请求发给一个没收起来的窗口，它只会闪一下。
            if (window.Minimized != restore)
            {
                continue;
            }

            var done = restore
                ? run.Devices.Windows.Restore(window.Handle)
                : run.Devices.Windows.Minimize(window.Handle);
            if (done)
            {
                moved++;
            }
        }

        run.Log(LogLevel.Info, depth, step.Type,
            restore ? LogKeys.DesktopRestored : LogKeys.DesktopCleared, moved);
    }

    /// <summary>
    /// Shows a notification beside the notification area, so a macro that has finished, or gone
    /// wrong, can say so to somebody who is not looking at the window. The words go through the
    /// usual reading, so a macro can put a variable in the title or the message.
    /// </summary>
    private static void Notify(IRunContext run, ExecutableStep step, int depth)
    {
        var title = run.Args(step).ValueOf("heading").AsText();
        var message = run.Args(step).ValueOf("notificationText").AsText();
        var what = run.Args(step).Text("notificationKind").Trim().ToLowerInvariant();

        var kind = what switch
        {
            "" or "information" or "info" => NotificationKind.Information,
            "warning" => NotificationKind.Warning,
            "error" => NotificationKind.Error,
            _ => throw new StepFailure(RunKeys.UnknownNotification, what),
        };

        run.Devices.System.Notify(title, message, kind);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.Notified,
            what.Length == 0 ? "information" : what);
    }

    /// <summary>
    /// Reads or changes what the window with the focus is typing in. A macro that types Latin keys
    /// while a Chinese layout is in use gets Chinese candidates instead, so switching the window to
    /// its English layout first is often the difference between a macro that works and one that
    /// types 你好.
    /// </summary>
    private static void InputMethod(IRunContext run, ExecutableStep step, int depth)
    {
        var what = run.Args(step).Text("imeAction").Trim().ToLowerInvariant();
        Value answer;

        switch (what)
        {
            case "get":
                answer = Value.FromText(run.Devices.System.InputMethod());
                run.Give(step, "layouts", Value.FromList([]));
                break;

            case "list":
                var installed = run.Devices.System.InputMethods();
                answer = Value.FromText(string.Join(';', installed));
                run.Give(step, "layouts", Value.FromList(installed.Select(Value.FromText)));
                break;

            case "switch":
                var wanted = run.Args(step).ValueOf("layout").AsText().Trim();
                if (wanted.Length == 0)
                {
                    throw new StepFailure(RunKeys.MissingName);
                }

                var switched = run.Devices.System.SwitchInputMethod(wanted);
                if (switched is null)
                {
                    throw new StepFailure(RunKeys.LayoutNotFound, wanted);
                }

                answer = Value.FromText(switched);
                run.Give(step, "layouts", Value.FromList([]));
                break;

            default:
                throw new StepFailure(RunKeys.UnknownInputMethodAction, what);
        }

        run.Store(step, "ime", answer, depth);
    }

    /// <summary>
    /// Reads or changes how bright the screens are. Like the volume, the result is the brightness the
    /// machine was left at rather than the one that was asked for, so a macro can put it back.
    /// </summary>
    private static void Brightness(IRunContext run, ExecutableStep step, int depth)
    {
        var what = run.Args(step).Text("brightnessAction").Trim().ToLowerInvariant();
        switch (what)
        {
            // A step written before this field existed means the same as the editor's own default:
            // read the brightness and leave the screens alone.
            case "":
            case "get":
                break;

            case "set":
                run.Devices.System.SetBrightness(run.Args(step).Number("levelPercent"));
                break;

            case "up":
            case "down":
                var move = run.Args(step).Number("stepPercent");
                run.Devices.System.SetBrightness(
                    run.Devices.System.Brightness() + (what is "up" ? move : -move));
                break;

            default:
                throw new StepFailure(RunKeys.UnknownBrightnessAction, what);
        }

        var level = run.Devices.System.Brightness();
        run.Give(step, "brightness", Value.FromNumber(level));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.Brightness, level);
    }
}
