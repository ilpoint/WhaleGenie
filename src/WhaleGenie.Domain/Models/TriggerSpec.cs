using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// What one trigger watches, and what to do about it. Each kind of trigger is a record of its own,
/// so a setting that only makes sense for a schedule cannot be written on a key, and a reader that
/// switched on the kind gets the fields it may rely on.
/// </summary>
/// <remarks>
/// Every member is a value — no collections. The runtime compares the spec it published last with
/// the one it is given now to tell "the same watch, described again" from "a different thing to
/// watch"; a collection member would make that comparison depend on identity and quietly start
/// treating every edit as a different watch.
/// </remarks>
public abstract record TriggerSpec
{
    public abstract TriggerKind Kind { get; }

    /// <summary>How the entry reads in a list, in the interface's language.</summary>
    public abstract string Describe();
}

/// <summary>A key, a mouse button or a controller button, and how the macro repeats.</summary>
public sealed record KeySpec(string Binding = "", MacroLoop Loop = MacroLoop.Press, bool Once = false)
    : TriggerSpec
{
    public override TriggerKind Kind => TriggerKind.Key;

    public override string Describe()
        => string.IsNullOrWhiteSpace(Binding) ? Strings.Get("Common.None") : Binding;
}

/// <summary>A pixel watched for a colour, from a fixed point or from under the cursor.</summary>
public sealed record ColorSpec(ColorMatchCondition Match = ColorMatchCondition.ColorMatches,
    int X = 0, int Y = 0, bool FollowCursor = false, string Hex = "#000000", int Tolerance = 5,
    MacroLoop Loop = MacroLoop.Press, bool Once = false) : TriggerSpec
{
    public override TriggerKind Kind => TriggerKind.Color;

    public override string Describe() => Hex.ToUpperInvariant();
}

/// <summary>A clock, either every so often or at a set time of day.</summary>
public sealed record TimerSpec(ScheduleMode Mode = ScheduleMode.Interval, string Time = "08:00",
    int Interval = 5, ScheduleUnit Unit = ScheduleUnit.Seconds, bool Once = false) : TriggerSpec
{
    public override TriggerKind Kind => TriggerKind.Timer;

    public override string Describe() => MacroSchedule.Describe(this);
}

/// <summary>A file or a folder whose change starts the macro.</summary>
public sealed record FileSpec(string Path = "", FileChangeKind Change = FileChangeKind.Any,
    string Filter = "*", bool Subfolders = false, bool Once = false) : TriggerSpec
{
    public override TriggerKind Kind => TriggerKind.File;

    public override string Describe() => Path.Length == 0 ? Strings.Get("Common.None") : Path;
}

/// <summary>A program appearing among the running ones, or leaving them.</summary>
public sealed record ProcessSpec(string Name = "", ProcessChangeKind Change = ProcessChangeKind.Started,
    bool Once = false) : TriggerSpec
{
    public override TriggerKind Kind => TriggerKind.Process;

    public override string Describe() => Name.Length == 0 ? Strings.Get("Common.None") : Name;
}

/// <summary>A window matching a rule appearing or going away.</summary>
public sealed record WindowSpec(WindowRule Target, WindowChangeKind Change = WindowChangeKind.Appeared,
    bool Once = false) : TriggerSpec
{
    public override TriggerKind Kind => TriggerKind.Window;

    public override string Describe() => Target.HasTarget
        ? $"{Target.MatchValue} · {Strings.Get($"WindowChange.{Change}")}"
        : Strings.Get("WindowRule.NotSet");
}

/// <summary>The machine going untouched for a while.</summary>
public sealed record IdleSpec(int Seconds = 60, bool Once = false) : TriggerSpec
{
    public override TriggerKind Kind => TriggerKind.Idle;

    public override string Describe() => Strings.Format("Idle.Summary", System.Math.Max(1, Seconds));
}

/// <summary>A typed abbreviation that expands into the macro.</summary>
public sealed record HotstringSpec(string Abbreviation = "",
    string EndChars = HotstringCharacters.DefaultEndChars, bool CaseSensitive = false,
    bool InsideWord = false, bool RequireEndChar = true, bool EraseTyped = true,
    bool KeepEndChar = true) : TriggerSpec
{
    public override TriggerKind Kind => TriggerKind.Hotstring;

    public override string Describe() => Abbreviation.Length == 0
        ? Strings.Get("Common.None")
        : Strings.Format("Trigger.HotstringPreview", Abbreviation,
            Strings.Get(RequireEndChar
                ? "Trigger.HotstringEndRequired"
                : "Trigger.HotstringEndNotRequired"));
}

/// <summary>The group of floating buttons a window gets.</summary>
public sealed record InterfaceSpec(WindowRule Window) : TriggerSpec
{
    public override TriggerKind Kind => TriggerKind.Interface;

    public override string Describe() => Window.HasTarget
        ? Window.MatchValue
        : Strings.Get("WindowRule.NotSet");
}

/// <summary>One cell of the floating menu, with the icon shown in it.</summary>
public sealed record MenuSpec(string PageId = "", int Row = -1, int Column = -1,
    MenuIconReference? Icon = null) : TriggerSpec
{
    public override TriggerKind Kind => TriggerKind.Menu;

    public override string Describe() => Strings.Get("Trigger.MenuMember");
}
