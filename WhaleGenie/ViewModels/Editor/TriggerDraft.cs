using System;
using System.Globalization;
using WhaleGenie.Models;
using WindowRuleModel = WhaleGenie.Core.Devices.WindowRule;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The one place the trigger drawer's fields and a trigger entry's settings are read into each
/// other. Every other class works with a <see cref="TriggerSpec"/>; only the drawer, which has a
/// control per field, needs the flat form.
/// </summary>
internal static class TriggerDraft
{
    /// <summary>Reads the form into the entry's settings.</summary>
    public static TriggerSpec ToSpec(MacroEditorViewModel form, TriggerEntry entry)
        => form.TriggerMode switch
        {
            TriggerKind.Key => new KeySpec(form.BindKey.Trim(), form.LoopMode, form.TriggerOnce),

            TriggerKind.Color => new ColorSpec(form.ColorMatch,
                Number(form.ColorPositionX), Number(form.ColorPositionY), form.UseCursorPosition,
                form.HexColor, Number(form.ColorTolerance), form.LoopMode, form.TriggerOnce),

            TriggerKind.Timer => new TimerSpec(form.ScheduleMode, form.ScheduleTime.Trim(),
                Math.Max(1, form.ScheduleInterval ?? 5), form.ScheduleUnit, form.TriggerOnce),

            TriggerKind.File => new FileSpec(form.WatchPath.Trim(), form.WatchChange,
                Filter(form.WatchFilter), form.WatchSubfolders, form.TriggerOnce),

            TriggerKind.Process => new ProcessSpec(form.ProcessName.Trim(), form.ProcessChange,
                form.TriggerOnce),

            TriggerKind.Window => new WindowSpec(form.WindowTarget, form.WindowChange, form.TriggerOnce),

            TriggerKind.Interface => new InterfaceSpec(form.WindowTarget),

            TriggerKind.Hotstring => new HotstringSpec(form.HotstringText, form.HotstringEndChars,
                form.HotstringCaseSensitive, form.HotstringInsideWord, form.HotstringRequireEnd,
                form.HotstringEraseTyped, form.HotstringKeepEnd),

            TriggerKind.Idle => new IdleSpec(Math.Max(1, form.IdleSeconds ?? 60), form.TriggerOnce),

            // A menu entry is placed on the menu pages; the drawer never edits it, so its cell and
            // icon are left exactly as they were.
            _ => entry.Spec is MenuSpec menu ? menu : new MenuSpec(),
        };

    /// <summary>Brings the form onto the settings of the selected entry, or onto a fresh key entry.</summary>
    public static void Load(MacroEditorViewModel form, TriggerEntry? entry)
    {
        var spec = entry?.Spec ?? new KeySpec();

        form.TriggerMode = spec.Kind;
        form.BindKey = spec is KeySpec key ? key.Binding : string.Empty;
        form.LoopMode = spec switch
        {
            KeySpec pressed => pressed.Loop,
            ColorSpec colour => colour.Loop,
            _ => MacroLoop.Press,
        };
        form.TriggerOnce = TriggerKinds.RunsOnce(spec);

        form.ScheduleMode = spec is TimerSpec timer ? timer.Mode : ScheduleMode.Interval;
        form.ScheduleTime = spec is TimerSpec time ? time.Time : "08:00";
        form.ScheduleInterval = spec is TimerSpec interval ? interval.Interval : 5;
        form.ScheduleUnit = spec is TimerSpec unit ? unit.Unit : ScheduleUnit.Seconds;

        form.WatchPath = spec is FileSpec file ? file.Path : string.Empty;
        form.WatchChange = spec is FileSpec watched ? watched.Change : FileChangeKind.Any;
        form.WatchFilter = spec is FileSpec filter ? filter.Filter : "*";
        form.WatchSubfolders = spec is FileSpec folders && folders.Subfolders;

        form.ProcessName = spec is ProcessSpec process ? process.Name : string.Empty;
        form.ProcessChange = spec is ProcessSpec program ? program.Change : ProcessChangeKind.Started;

        form.WindowTarget = spec switch
        {
            WindowSpec window => window.Target,
            InterfaceSpec group => group.Window,
            _ => WindowRuleModel.Empty,
        };
        form.WindowChange = spec is WindowSpec change ? change.Change : WindowChangeKind.Appeared;

        form.IdleSeconds = spec is IdleSpec idle ? idle.Seconds : 60;

        form.ColorMatch = spec is ColorSpec match ? match.Match : ColorMatchCondition.ColorMatches;
        form.ColorPositionX = (spec is ColorSpec point ? point.X : 0)
            .ToString(CultureInfo.InvariantCulture);
        form.ColorPositionY = (spec is ColorSpec vertical ? vertical.Y : 0)
            .ToString(CultureInfo.InvariantCulture);
        form.UseCursorPosition = spec is ColorSpec cursor && cursor.FollowCursor;
        form.HexColor = spec is ColorSpec hex ? hex.Hex : "#000000";
        form.ColorTolerance = (spec is ColorSpec tolerance ? tolerance.Tolerance : 5)
            .ToString(CultureInfo.InvariantCulture);

        form.HotstringText = spec is HotstringSpec hotstring ? hotstring.Abbreviation : string.Empty;
        form.HotstringEndChars = spec is HotstringSpec ends ? ends.EndChars
            : HotstringCharacters.DefaultEndChars;
        form.HotstringCaseSensitive = spec is HotstringSpec casing && casing.CaseSensitive;
        form.HotstringInsideWord = spec is HotstringSpec inside && inside.InsideWord;
        form.HotstringRequireEnd = spec is not HotstringSpec ending || ending.RequireEndChar;
        form.HotstringEraseTyped = spec is not HotstringSpec erasing || erasing.EraseTyped;
        form.HotstringKeepEnd = spec is not HotstringSpec keeping || keeping.KeepEndChar;
    }

    /// <summary>A number the person typed, or zero when it is not a number yet.</summary>
    private static int Number(string? value)
        => int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;

    private static string Filter(string? value)
        => (value ?? string.Empty).Trim() is { Length: > 0 } filter ? filter : "*";
}
