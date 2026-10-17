using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

public sealed partial class MacroRunner
{
    // --------------------------------------------------------------------- system

    /// <summary>Reads a fact about the machine into a variable.</summary>
    private void SystemInfo(ExecutableStep step, int depth)
    {
        var value = _devices.System.Info(ReadText(step, "field").Trim());
        Store(step, "value", Value.FromText(value), depth);
    }

    /// <summary>
    /// How the desk is laid out, one record to a display, so a macro that has to work on the second
    /// screen can find out where it is instead of having the numbers written into it. The rectangle
    /// is in desktop coordinates, which is what every other coordinate in a macro is written in —
    /// a display to the left of the primary one starts at a negative number, and that is exactly the
    /// number a step aiming at it needs. The middle of it is in the record as well, so aiming at
    /// "the middle of that screen" is one field rather than four numbers and an arithmetic problem.
    /// </summary>
    private void ListDisplays(ExecutableStep step, int depth)
    {
        var desk = _devices.Screen.Desktop;

        Give(step, "displays", Value.FromList(
        [
            .. desk.Displays.Select(display =>
            {
                var area = display.Bounds;
                var middle = Middle(new ScreenPoint(area.Left, area.Top),
                    new ScreenSize(area.Width, area.Height));

                return Value.FromRecord(
                [
                    ("x", Value.FromNumber(middle.X)),
                    ("y", Value.FromNumber(middle.Y)),
                    (FieldNames.Left, Value.FromNumber(area.Left)),
                    (FieldNames.Top, Value.FromNumber(area.Top)),
                    (FieldNames.Width, Value.FromNumber(area.Width)),
                    (FieldNames.Height, Value.FromNumber(area.Height)),
                    ("right", Value.FromNumber(area.Right)),
                    ("bottom", Value.FromNumber(area.Bottom)),
                    ("primary", Value.FromBool(display.Primary)),
                    ("box", Value.FromText($"{area.Left},{area.Top},{area.Width},{area.Height}")),
                ]);
            }),
        ]));

        Log(LogLevel.Info, depth, step.Type, LogKeys.ListedDisplays,
            desk.Displays.Count, $"{desk.Bounds.Width}x{desk.Bounds.Height}");
    }

    /// <summary>Reads an environment variable into a variable.</summary>
    private void EnvironmentVariable(ExecutableStep step, int depth)
    {
        var name = ReadValue(step, "environmentName").AsText();
        var value = _devices.System.Environment(name);
        Store(step, "value", Value.FromText(value), depth);
    }

    /// <summary>
    /// Asks the machine to do one of the things the Start menu's power button does. The name is
    /// read strictly rather than falling back to something: a macro that mistyped "shutdown" should
    /// not have its screen locked instead, and one that mistyped "lock" should certainly not be
    /// turning the machine off.
    /// </summary>
    private void Power(ExecutableStep step, int depth)
    {
        var wanted = ReadText(step, "powerAction").Trim().ToLowerInvariant();
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

        var grace = OptionalNumber(step, "graceSeconds", 0);
        _devices.System.Power(action, grace);
        Log(LogLevel.Info, depth, step.Type, LogKeys.Power, wanted.Length == 0 ? "lock" : wanted,
            grace);
    }

    /// <summary>
    /// Reads or changes the volume of the speakers Windows is using. Whatever the step asked for,
    /// what it leaves behind is the level the machine ended up at, so a macro that turned the sound
    /// down can put it back the way it found it.
    /// </summary>
    private void Volume(ExecutableStep step, int depth)
    {
        var what = ReadText(step, "volumeAction").Trim().ToLowerInvariant();
        switch (what)
        {
            // A step written before this field existed means the same as the editor's own default:
            // read the level and leave the machine alone.
            case "":
            case "get":
                break;

            case "set":
                _devices.System.SetVolume(OptionalNumber(step, "levelPercent", 50));
                break;

            case "up":
            case "down":
                var move = OptionalNumber(step, "stepPercent", 5);
                _devices.System.SetVolume(
                    _devices.System.Volume() + (what == "up" ? move : -move));
                break;

            case "mute":
                _devices.System.SetMuted(true);
                break;

            case "unmute":
                _devices.System.SetMuted(false);
                break;

            case "togglemute":
                _devices.System.SetMuted(!_devices.System.IsMuted());
                break;

            default:
                throw new StepFailure(RunKeys.UnknownVolumeAction, what);
        }

        var level = _devices.System.Volume();
        Give(step, "volume", Value.FromNumber(level));
        Log(LogLevel.Info, depth, step.Type, LogKeys.Volume, level);

        // Switching the sound off does not change the level, so the level alone would leave a reader
        // wondering whether the macro had done anything at all.
        if (what is "mute" or "unmute" or "togglemute")
        {
            Log(LogLevel.Info, depth, step.Type,
                _devices.System.IsMuted() ? LogKeys.SoundOff : LogKeys.SoundOn);
        }
    }

    /// <summary>
    /// Plays one of the machine's own event sounds, so a macro that has finished, or gone wrong,
    /// can say so where nobody is looking at the screen.
    /// </summary>
    private void PlaySound(ExecutableStep step, int depth)
    {
        var what = ReadText(step, "sound").Trim().ToLowerInvariant();
        var kind = what switch
        {
            "" or "default" => SoundKind.Default,
            "information" or "info" => SoundKind.Information,
            "warning" => SoundKind.Warning,
            "error" => SoundKind.Error,
            "question" => SoundKind.Question,
            _ => throw new StepFailure(RunKeys.UnknownSound, what),
        };

        _devices.System.PlaySound(kind);
        Log(LogLevel.Info, depth, step.Type, LogKeys.PlayedSound,
            what.Length == 0 ? "default" : what);
    }

    /// <summary>
    /// Shows a notification beside the notification area, so a macro that has finished, or gone
    /// wrong, can say so to somebody who is not looking at the window. The words go through the
    /// usual reading, so a macro can put a variable in the title or the message.
    /// </summary>
    private void Notify(ExecutableStep step, int depth)
    {
        var title = ReadValue(step, "heading").AsText();
        var message = ReadValue(step, "notificationText").AsText();
        var what = ReadText(step, "notificationKind").Trim().ToLowerInvariant();

        var kind = what switch
        {
            "" or "information" or "info" => NotificationKind.Information,
            "warning" => NotificationKind.Warning,
            "error" => NotificationKind.Error,
            _ => throw new StepFailure(RunKeys.UnknownNotification, what),
        };

        _devices.System.Notify(title, message, kind);
        Log(LogLevel.Info, depth, step.Type, LogKeys.Notified, what.Length == 0 ? "information" : what);
    }

    /// <summary>
    /// Reads or changes what the window with the focus is typing in. A macro that types Latin keys
    /// while a Chinese layout is in use gets Chinese candidates instead, so switching the window to
    /// its English layout first is often the difference between a macro that works and one that
    /// types 你好.
    /// </summary>
    private void InputMethod(ExecutableStep step, int depth)
    {
        var what = ReadText(step, "imeAction").Trim().ToLowerInvariant();
        Value answer;

        switch (what)
        {
            case "get":
                answer = Value.FromText(_devices.System.InputMethod());
                break;

            case "list":
                answer = Value.FromText(string.Join(';', _devices.System.InputMethods()));
                break;

            case "switch":
                var wanted = ReadValue(step, "layout").AsText().Trim();
                if (wanted.Length == 0)
                {
                    throw new StepFailure(RunKeys.MissingName);
                }

                var switched = _devices.System.SwitchInputMethod(wanted);
                if (switched is null)
                {
                    throw new StepFailure(RunKeys.LayoutNotFound, wanted);
                }

                answer = Value.FromText(switched);
                break;

            default:
                throw new StepFailure(RunKeys.UnknownInputMethodAction, what);
        }

        Store(step, "ime", answer, depth);
    }

    /// <summary>
    /// Reads or changes how bright the screens are. Like the volume, the result is the brightness the
    /// machine was left at rather than the one that was asked for, so a macro can put it back.
    /// </summary>
    private void Brightness(ExecutableStep step, int depth)
    {
        var what = ReadText(step, "brightnessAction").Trim().ToLowerInvariant();
        switch (what)
        {
            // A step written before this field existed means the same as the editor's own default:
            // read the brightness and leave the screens alone.
            case "":
            case "get":
                break;

            case "set":
                _devices.System.SetBrightness(OptionalNumber(step, "levelPercent", 50));
                break;

            case "up":
            case "down":
                var move = OptionalNumber(step, "stepPercent", 10);
                _devices.System.SetBrightness(
                    _devices.System.Brightness() + (what is "up" ? move : -move));
                break;

            default:
                throw new StepFailure(RunKeys.UnknownBrightnessAction, what);
        }

        var level = _devices.System.Brightness();
        Give(step, "brightness", Value.FromNumber(level));
        Log(LogLevel.Info, depth, step.Type, LogKeys.Brightness, level);
    }

    /// <summary>The process id a step names, which is usually a variable.</summary>
    private int ProcessId(ExecutableStep step)
    {
        var text = ReadValue(step, "id").AsText().Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            throw new StepFailure(RunKeys.BadProcessId, text);
        }

        return id;
    }

}
