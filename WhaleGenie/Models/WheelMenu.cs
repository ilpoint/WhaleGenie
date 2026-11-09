using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>Which of the three circles a wheel position belongs to.</summary>
public enum WheelRing
{
    Inner,
    Outer,
    Third,
    Extended,
}

/// <summary>
/// What the wheel shows while it is open. The source can be changed with the wheel; the scene and
/// the layout it produced stay fixed for the rest of that session, because a wheel someone is
/// aiming at must not change shape under the pointer when another window happens to come forward.
/// </summary>
public enum WheelSource
{
    Merged,
    Global,
    Program,
}

/// <summary>
/// One position on the wheel, named by its circle and its clockwise index from straight up.
/// </summary>
public readonly record struct WheelSlotRef(WheelRing Ring, int Index);

/// <summary>
/// The fixed capacities of the three circles. The outer circle keeps sixteen slots even when the
/// layout only shows eight: narrowing the layout hides positions, it does not throw their actions
/// away, so widening it again puts the same actions back in the same directions.
/// </summary>
public static class WheelShape
{
    public const int InnerCount = 8;
    public const int OuterCount = 8;
    public const int OuterWideCount = 16;
    public const int ThirdCount = 24;
    public const int ExtendedCount = 8;

    /// <summary>The capacities Quicker offers for the third resident circle. Zero hides it.</summary>
    public static int[] ThirdCapacities => [0, 8, 16, 24];

    public static int CountOf(WheelRing ring, bool wideOuter, int thirdCapacity) => ring switch
    {
        WheelRing.Inner => InnerCount,
        WheelRing.Outer => wideOuter ? OuterWideCount : OuterCount,
        WheelRing.Third => ClampThird(thirdCapacity),
        _ => ExtendedCount,
    };

    /// <summary>The two-circle shape used by older callers and by the editor's default preview.</summary>
    public static int CountOf(WheelRing ring, bool wideOuter) => CountOf(ring, wideOuter, 0);

    public static int CapacityOf(WheelRing ring) => ring switch
    {
        WheelRing.Inner => InnerCount,
        WheelRing.Outer => OuterWideCount,
        WheelRing.Third => ThirdCount,
        _ => ExtendedCount,
    };

    public static int ClampThird(int capacity)
        => ThirdCapacities.Contains(capacity) ? capacity : 0;
}

/// <summary>What one position runs. An empty macro name means the position is left blank.</summary>
public sealed class WheelSlot
{
    public string Macro { get; set; } = string.Empty;

    /// <summary>A note shown in the centre instead of the macro name when the person wrote one.</summary>
    public string Note { get; set; } = string.Empty;

    public MenuIconReference? Icon { get; set; }

    public bool IsEmpty => Macro.Trim().Length == 0;

    public string Label => Note.Trim().Length > 0 ? Note.Trim() : Macro.Trim();

    public WheelSlot Copy() => new()
    {
        Macro = Macro,
        Note = Note,
        Icon = Icon is null ? null : Icon with { },
    };

    public JsonObject ToJson()
    {
        var node = new JsonObject { ["macro"] = Macro };
        if (Note.Trim().Length > 0)
        {
            node["note"] = Note;
        }

        if (Icon is { IsEmpty: false } icon)
        {
            node["icon"] = icon.ToJson();
        }

        return node;
    }

    public static WheelSlot FromJson(JsonObject? node) => new()
    {
        Macro = node?["macro"]?.GetValue<string>() ?? string.Empty,
        Note = node?["note"]?.GetValue<string>() ?? string.Empty,
        Icon = node?["icon"] is JsonObject icon ? MenuIconReference.FromJson(icon) : null,
    };
}

/// <summary>
/// One set of wheel positions. The global scene is the default; a program scene carries a window
/// rule and only overrides positions it has actually filled in.
/// </summary>
public sealed class WheelScene
{
    public const string GlobalKey = "global";

    private readonly WheelSlot[] _inner = NewSlots(WheelShape.InnerCount);
    private readonly WheelSlot[] _outer = NewSlots(WheelShape.OuterWideCount);
    private readonly WheelSlot[] _third = NewSlots(WheelShape.ThirdCount);
    private readonly WheelSlot[] _extended = NewSlots(WheelShape.ExtendedCount);

    public string Key { get; set; } = GlobalKey;

    public WindowRule Window { get; set; } = WindowRule.Empty;

    public bool IsGlobal => string.Equals(Key, GlobalKey, StringComparison.Ordinal);

    public IReadOnlyList<WheelSlot> Inner => _inner;

    public IReadOnlyList<WheelSlot> Outer => _outer;

    public IReadOnlyList<WheelSlot> Third => _third;

    public IReadOnlyList<WheelSlot> Extended => _extended;

    public WheelSlot[] Of(WheelRing ring) => ring switch
    {
        WheelRing.Inner => _inner,
        WheelRing.Outer => _outer,
        WheelRing.Third => _third,
        _ => _extended,
    };

    public WheelSlot At(WheelSlotRef reference)
    {
        var slots = Of(reference.Ring);
        return slots[Math.Clamp(reference.Index, 0, slots.Length - 1)];
    }

    public string Summary => IsGlobal
        ? Strings.Get("Wheel.SceneGlobal")
        : $"{Window.MatchValue} · {Strings.Get($"WindowRule.Short.{Window.How}", Window.How)}";

    public bool AnyAction()
        => _inner.Concat(_outer).Concat(_third).Concat(_extended).Any(slot => !slot.IsEmpty);

    /// <summary>
    /// The first empty position of a circle that is on screen. The invisible extended circle is
    /// left out: a macro put there would be a position nobody can see or aim at.
    /// </summary>
    public WheelSlot? FirstEmptySlot()
        => _inner.Concat(_outer).FirstOrDefault(slot => slot.IsEmpty);

    public JsonObject ToJson()
    {
        var node = new JsonObject
        {
            ["key"] = Key,
            ["inner"] = new JsonArray([.. _inner.Select(slot => (JsonNode)slot.ToJson())]),
            ["outer"] = new JsonArray([.. _outer.Select(slot => (JsonNode)slot.ToJson())]),
            ["third"] = new JsonArray([.. _third.Select(slot => (JsonNode)slot.ToJson())]),
            ["extended"] = new JsonArray([.. _extended.Select(slot => (JsonNode)slot.ToJson())]),
        };
        if (!IsGlobal && Window.HasTarget)
        {
            node["window"] = Window.ToJson();
        }

        return node;
    }

    public static WheelScene FromJson(JsonObject node)
    {
        var scene = new WheelScene
        {
            Key = node["key"]?.GetValue<string>() is { Length: > 0 } key ? key : Guid.NewGuid().ToString("N")[..10],
            Window = node["window"] is JsonObject window ? WindowRule.FromJson(window) : WindowRule.Empty,
        };
        ReadInto(node["inner"] as JsonArray, scene._inner);
        ReadInto(node["outer"] as JsonArray, scene._outer);
        ReadInto(node["third"] as JsonArray, scene._third);
        ReadInto(node["extended"] as JsonArray, scene._extended);
        return scene;
    }

    public WheelScene Copy()
    {
        var copy = new WheelScene { Key = Key, Window = Window with { } };
        CopyInto(_inner, copy._inner);
        CopyInto(_outer, copy._outer);
        CopyInto(_third, copy._third);
        CopyInto(_extended, copy._extended);
        return copy;
    }

    private static WheelSlot[] NewSlots(int count)
        => [.. Enumerable.Range(0, count).Select(_ => new WheelSlot())];

    private static void ReadInto(JsonArray? array, WheelSlot[] slots)
    {
        if (array is null)
        {
            return;
        }

        for (var index = 0; index < slots.Length && index < array.Count; index++)
        {
            if (array[index] is JsonObject slot)
            {
                slots[index] = WheelSlot.FromJson(slot);
            }
        }
    }

    private static void CopyInto(WheelSlot[] from, WheelSlot[] to)
    {
        for (var index = 0; index < to.Length && index < from.Length; index++)
        {
            to[index] = from[index].Copy();
        }
    }
}

/// <summary>One position as the wheel shows it, after the scene and the global scene were combined.</summary>
public sealed record WheelRuntimeSlot(
    WheelSlotRef Reference,
    string Name,
    MacroItem? Macro,
    MenuIconReference? Icon,
    bool FromProgram,
    string Note = "")
{
    /// <summary>The note wins over the macro name wherever a label is shown.</summary>
    public string Label => Note.Trim().Length > 0 ? Note.Trim() : Name.Trim();
}

/// <summary>An immutable snapshot handed to the floating wheel window.</summary>
public sealed record WheelRuntimeLayout(
    IReadOnlyList<WheelRuntimeSlot> Slots,
    string SceneName,
    WheelSource Source,
    bool WideOuter,
    int ThirdCapacity = 0,
    bool HideEmptyOuter = false)
{
    public WheelRuntimeSlot? At(WheelSlotRef reference)
        => Slots.FirstOrDefault(slot => slot.Reference == reference);
}

/// <summary>
/// The wheel actions of a project. Like the macro menu, the window rules and the positions live
/// with the project; the key that opens the wheel belongs to the machine.
/// </summary>
public sealed class WheelMenuRules
{
    public WheelScene Global { get; private set; } = new() { Key = WheelScene.GlobalKey };

    public ObservableCollection<WheelScene> Scenes { get; } = [];

    /// <summary>The first program scene whose rule matches the foreground window wins.</summary>
    public WheelScene? SceneFor(WindowInfo? foreground, IWindowDevice windows)
        => foreground is null
            ? null
            : Scenes.FirstOrDefault(scene =>
                scene.Window.HasTarget
                && ProjectTriggers.WindowRuleMatches(scene.Window, foreground, windows));

    /// <summary>
    /// Whether the wheel may open in the window that is in front. With "only in these programs"
    /// off this is always true; with it on, a program scene has to match, which is what keeps the
    /// trigger button an ordinary button everywhere else.
    /// </summary>
    public bool OpensFor(WindowInfo? foreground, IWindowDevice windows, bool onlyInPrograms)
        => !onlyInPrograms || SceneFor(foreground, windows) is not null;

    public WheelScene? Find(string key)
        => Global.IsGlobal && key == WheelScene.GlobalKey
            ? Global
            : Scenes.FirstOrDefault(scene => scene.Key == key);

    /// <summary>
    /// Whether any position of any scene runs this macro. The wheel is a trigger surface of its
    /// own, so the macro list asks this rather than looking for a trigger entry that never existed.
    /// </summary>
    public bool Uses(string macroName)
    {
        var wanted = macroName.Trim();
        bool InSlot(WheelSlot slot)
            => !slot.IsEmpty
               && string.Equals(slot.Macro.Trim(), wanted, StringComparison.OrdinalIgnoreCase);

        bool InScene(WheelScene scene)
            => scene.Inner.Concat(scene.Outer).Concat(scene.Third).Concat(scene.Extended).Any(InSlot);

        return InScene(Global) || Scenes.Any(InScene);
    }

    public WheelScene AddScene(WindowRule rule)
    {
        var scene = new WheelScene
        {
            Key = Guid.NewGuid().ToString("N")[..10],
            Window = rule with { },
        };
        Scenes.Add(scene);
        return scene;
    }

    public bool RemoveScene(string key)
    {
        var scene = Scenes.FirstOrDefault(candidate => candidate.Key == key);
        return scene is not null && Scenes.Remove(scene);
    }

    public void MoveScene(string key, string targetKey)
    {
        var from = Scenes.FirstOrDefault(scene => scene.Key == key);
        var target = Scenes.FirstOrDefault(scene => scene.Key == targetKey);
        if (from is null || target is null || from == target)
        {
            return;
        }

        Scenes.Remove(from);
        Scenes.Insert(Scenes.IndexOf(target) + 1, from);
    }

    /// <summary>
    /// Combines the global scene and the program scene position by position. This is the rule
    /// Quicker's wheel uses: a program position replaces the global one only when it is filled in.
    /// </summary>
    public WheelRuntimeLayout Resolve(WheelScene? scene, WheelSource source, bool wideOuter,
        IReadOnlyList<MacroItem> macros, int thirdCapacity = 0, bool hideEmptyOuter = false)
    {
        var useProgram = source is WheelSource.Merged or WheelSource.Program && scene is not null;
        var useGlobal = source is WheelSource.Merged or WheelSource.Global;
        var capacity = WheelShape.ClampThird(thirdCapacity);
        var slots = new List<WheelRuntimeSlot>();

        MacroItem? FindMacro(string? name)
            => name is null
                ? null
                : macros.FirstOrDefault(candidate => candidate.IsEnabled
                    && string.Equals(candidate.Name.Trim(), name.Trim(),
                        StringComparison.OrdinalIgnoreCase));

        WheelRuntimeSlot Make(WheelSlotRef reference, WheelSlot picked, bool fromProgram)
            => new(reference, picked.Macro, FindMacro(picked.Macro), picked.Icon, fromProgram,
                picked.Note);

        foreach (var ring in new[] { WheelRing.Inner, WheelRing.Outer,
                    WheelRing.Third, WheelRing.Extended })
        {
            for (var index = 0; index < WheelShape.CountOf(ring, wideOuter, capacity); index++)
            {
                var reference = new WheelSlotRef(ring, index);
                var program = useProgram ? scene!.At(reference) : null;
                var global = useGlobal ? Global.At(reference) : null;
                var picked = program is { IsEmpty: false } ? program
                    : global is { IsEmpty: false } ? global
                    : null;
                slots.Add(picked is null
                    ? new WheelRuntimeSlot(reference, string.Empty, null, null, false)
                    : Make(reference, picked, program is { IsEmpty: false }));
            }
        }

        var name = source == WheelSource.Global ? Global.Summary
            : scene?.Summary ?? Global.Summary;
        return new WheelRuntimeLayout(slots, name, source, wideOuter, capacity, hideEmptyOuter);
    }

    public IReadOnlyList<string> Problems(IReadOnlyList<MacroItem> macros,
        Func<string, bool>? knowsIcon = null)
    {
        var problems = new List<string>();
        var names = macros.Select(macro => macro.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in Scenes.Where(scene => !scene.Window.HasTarget))
        {
            problems.Add(Strings.Format("Wheel.Problem.RuleEmpty", scene.Key));
        }

        foreach (var scene in new[] { Global }.Concat(Scenes))
        {
            void Check(WheelSlot slot)
            {
                if (!slot.IsEmpty && !names.Contains(slot.Macro.Trim()))
                {
                    problems.Add(Strings.Format("Wheel.Problem.MacroMissing",
                        scene.Summary, slot.Macro));
                }

                if (slot.Icon is { Kind: MenuIconKind.Builtin, IsEmpty: false } icon
                    && knowsIcon is not null && !knowsIcon(icon.Value))
                {
                    problems.Add(Strings.Format("Wheel.Problem.Icon", scene.Summary, icon.Value));
                }
            }

            foreach (var ring in new[] { WheelRing.Inner, WheelRing.Outer,
                        WheelRing.Third, WheelRing.Extended })
            {
                for (var index = 0; index < WheelShape.CapacityOf(ring); index++)
                {
                    Check(scene.At(new WheelSlotRef(ring, index)));
                }
            }
        }

        return problems;
    }

    public JsonObject ToJson() => new()
    {
        ["global"] = Global.ToJson(),
        ["scenes"] = new JsonArray([.. Scenes.Select(scene => (JsonNode)scene.ToJson())]),
    };

    public static WheelMenuRules FromJson(JsonObject? node)
    {
        var rules = new WheelMenuRules();
        if (node?["global"] is JsonObject global)
        {
            var readGlobal = WheelScene.FromJson(global);
            readGlobal.Key = WheelScene.GlobalKey;
            readGlobal.Window = WindowRule.Empty;
            rules.Global = readGlobal;
        }

        foreach (var item in node?["scenes"] as JsonArray ?? [])
        {
            if (item is JsonObject scene)
            {
                var read = WheelScene.FromJson(scene);
                if (read.IsGlobal)
                {
                    // A program scene cannot be the global one; the key would make it unaddressable.
                    read.Key = Guid.NewGuid().ToString("N")[..10];
                }

                rules.Scenes.Add(read);
            }
        }

        return rules;
    }

    public WheelMenuRules Copy()
    {
        var copy = new WheelMenuRules { Global = Global.Copy() };
        foreach (var scene in Scenes)
        {
            copy.Scenes.Add(scene.Copy());
        }

        return copy;
    }
}

/// <summary>
/// The wheel choices that belong to this machine rather than to a project: the key that opens it,
/// its size and its behaviour while open. Written as one object so a new choice does not need a
/// second copy of the read/write pair in the settings file.
/// </summary>
public sealed class WheelOptions
{
    public const int SmallestDiameter = 150;
    public const int LargestDiameter = 600;
    public const int SmallestFont = 8;
    public const int LargestFont = 24;
    public const int LongestTimeoutMs = 120_000;

    public const string MiddleButton = "Mouse Middle";
    public const string RightButton = "Mouse Right";
    public const string BackButton = "Mouse 4";
    public const string ForwardButton = "Mouse 5";
    public const string UsualRepeatKey = "F1";

    /// <summary>
    /// The four buttons a wheel can be opened with. They are the buttons a hand can hold while it
    /// moves: the left button drags things, and the wheel notches belong to switching the scene.
    /// A keyboard binding is deliberately not among them — the gesture is a press-and-move, and a
    /// key that has to be held while the pointer moves is a different interaction.
    /// </summary>
    public static IReadOnlyList<string> LauncherButtons =>
        [RightButton, MiddleButton, ForwardButton, BackButton];

    /// <summary>
    /// The wheel opens on the right button by default. The middle button belongs to the floating
    /// macro menu, and one button cannot own two gestures that both start on a press.
    /// </summary>
    public string Launcher { get; set; } = RightButton;

    public int Diameter { get; set; } = 420;

    public int FontSize { get; set; } = 11;

    public int TimeoutMs { get; set; } = 20_000;

    public bool WideOuter { get; set; }

    public bool ExtendedHint { get; set; } = true;

    public bool HideLabelWithIcon { get; set; }

    public bool CenterName { get; set; }

    public bool ClampToScreen { get; set; } = true;

    public bool MovePointerToCenter { get; set; }

    public string RepeatKey { get; set; } = UsualRepeatKey;

    /// <summary>Capacity of the third resident circle: 0 hides it, otherwise 8, 16 or 24.</summary>
    public int ThirdCapacity { get; set; }

    /// <summary>Hides a whole outer circle when every position in it is empty.</summary>
    public bool HideEmptyOuter { get; set; } = true;

    /// <summary>Shows the line from the centre to the pointer, solid inside and dashed outside.</summary>
    public bool ShowGuide { get; set; } = true;

    /// <summary>
    /// Keeps the trigger button to the programs that have a scene. With this on the wheel opens
    /// only where a program scene matches; the desktop and every other program keep an ordinary
    /// button, which is what makes the wheel feel switched off by default.
    /// </summary>
    public bool OnlyInPrograms { get; set; }

    /// <summary>Uses the application's theme colours instead of a fixed light palette.</summary>
    public bool FollowTheme { get; set; } = true;

    /// <summary>A soft sheen over each sector.</summary>
    public bool Gloss { get; set; } = true;

    /// <summary>A brighter rim around the sector the pointer is on.</summary>
    public bool HoverBorder { get; set; } = true;

    /// <summary>Scales and fades the wheel in when it opens.</summary>
    public bool PopAnimation { get; set; } = true;

    public WheelOptions Copy() => new()
    {
        Launcher = Launcher,
        Diameter = Diameter,
        FontSize = FontSize,
        TimeoutMs = TimeoutMs,
        WideOuter = WideOuter,
        ExtendedHint = ExtendedHint,
        HideLabelWithIcon = HideLabelWithIcon,
        CenterName = CenterName,
        ClampToScreen = ClampToScreen,
        MovePointerToCenter = MovePointerToCenter,
        RepeatKey = RepeatKey,
        ThirdCapacity = ThirdCapacity,
        HideEmptyOuter = HideEmptyOuter,
        ShowGuide = ShowGuide,
        OnlyInPrograms = OnlyInPrograms,
        FollowTheme = FollowTheme,
        Gloss = Gloss,
        HoverBorder = HoverBorder,
        PopAnimation = PopAnimation,
    };

    public WheelOptions Clamped() => new()
    {
        // An empty launcher is a real choice: it means the wheel is switched off. Falling back to
        // the default here would turn the off switch back on after every restart.
        Launcher = Launcher.Trim().Length == 0
            ? string.Empty
            : IsAllowedLauncher(Launcher) ? Launcher.Trim() : RightButton,
        Diameter = Math.Clamp(Diameter, SmallestDiameter, LargestDiameter),
        FontSize = Math.Clamp(FontSize, SmallestFont, LargestFont),
        TimeoutMs = Math.Clamp(TimeoutMs, 0, LongestTimeoutMs),
        WideOuter = WideOuter,
        ExtendedHint = ExtendedHint,
        HideLabelWithIcon = HideLabelWithIcon,
        CenterName = CenterName,
        ClampToScreen = ClampToScreen,
        MovePointerToCenter = MovePointerToCenter,
        RepeatKey = HotkeyBinding.IsSupported(RepeatKey) ? RepeatKey.Trim() : UsualRepeatKey,
        ThirdCapacity = WheelShape.ClampThird(ThirdCapacity),
        HideEmptyOuter = HideEmptyOuter,
        ShowGuide = ShowGuide,
        OnlyInPrograms = OnlyInPrograms,
        FollowTheme = FollowTheme,
        Gloss = Gloss,
        HoverBorder = HoverBorder,
        PopAnimation = PopAnimation,
    };

    /// <summary>
    /// The four buttons of <see cref="LauncherButtons"/>, and nothing else. A stored binding that
    /// is not one of them — a keyboard key from an older file, say — is read back as the default
    /// rather than left in place, because nothing would ever open the wheel with it.
    /// </summary>
    public static bool IsAllowedLauncher(string? binding)
    {
        var text = binding?.Trim() ?? string.Empty;
        return LauncherButtons.Contains(text, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>The radii of the resident circles, in window coordinates.</summary>
public sealed record WheelMetrics(
    double Center,
    double Inner,
    double Outer,
    int OuterCount,
    double Third = 0,
    int ThirdCount = 0)
{
    /// <summary>Where the visible wheel ends; past it lies the invisible extended circle.</summary>
    public double Edge => ThirdCount > 0 ? Third : Outer;

    public bool HasOuter => Outer > Inner + 1;

    public bool HasThird => ThirdCount > 0 && Third > Outer + 1;
}

/// <summary>
/// Turning a pointer position into a wheel position. Kept free of Avalonia and of the window so the
/// direction and distance rules can be checked without a desktop.
/// </summary>
public static class WheelGeometry
{
    public const double CenterShare = 0.22;
    public const double InnerShare = 0.56;
    public const double OuterShare = 0.94;

    /// <summary>
    /// Rings is how many resident circles are actually drawn (1, 2 or 3). Empty outer circles can
    /// be hidden so the wheel shrinks to the circles that carry actions; leaving it at zero picks
    /// two circles, or three when the third circle has a capacity.
    /// </summary>
    public static WheelMetrics For(double diameter, bool wideOuter, int thirdCapacity = 0,
        int rings = 0)
    {
        var capacity = WheelShape.ClampThird(thirdCapacity);
        var drawn = rings <= 0 ? (capacity > 0 ? 3 : 2) : Math.Clamp(rings, 1, 3);
        var outerCount = wideOuter ? WheelShape.OuterWideCount : WheelShape.OuterCount;
        var half = diameter / 2;
        return drawn switch
        {
            1 => new WheelMetrics(half * 0.30, half * 0.94, half * 0.94, outerCount),
            3 => new WheelMetrics(half * 0.16, half * 0.40, half * 0.66, outerCount,
                half * 0.94, capacity),
            _ => new WheelMetrics(half * CenterShare, half * InnerShare,
                half * OuterShare, outerCount),
        };
    }

    /// <summary>
    /// Straight up is the middle of position zero, and positions run clockwise. A release inside
    /// the centre circle cancels; a release past the outer edge belongs to the invisible extended
    /// circle, which is why it is answered by direction rather than by a shape on screen.
    /// </summary>
    public static WheelSlotRef? At(double dx, double dy, WheelMetrics metrics)
    {
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < metrics.Center)
        {
            return null;
        }

        if (distance < metrics.Inner)
        {
            return new WheelSlotRef(WheelRing.Inner, Sector(dx, dy, WheelShape.InnerCount));
        }

        if (metrics.HasOuter && distance < metrics.Outer)
        {
            return new WheelSlotRef(WheelRing.Outer, Sector(dx, dy, metrics.OuterCount));
        }

        if (metrics.HasThird && distance < metrics.Third)
        {
            return new WheelSlotRef(WheelRing.Third, Sector(dx, dy, metrics.ThirdCount));
        }

        return new WheelSlotRef(WheelRing.Extended,
            Sector(dx, dy, WheelShape.ExtendedCount));
    }

    public static int Sector(double dx, double dy, int count)
    {
        var angle = Math.Atan2(dx, -dy);
        if (angle < 0)
        {
            angle += Math.PI * 2;
        }

        var step = Math.PI * 2 / count;
        return (int)Math.Floor((angle + step / 2) / step) % count;
    }

    /// <summary>Where the label of a position sits, relative to the wheel's centre.</summary>
    public static (double X, double Y) LabelAt(WheelSlotRef reference, WheelMetrics metrics,
        double diameter)
    {
        var radius = reference.Ring switch
        {
            WheelRing.Inner => (metrics.Center + metrics.Inner) / 2,
            WheelRing.Outer => (metrics.Inner + metrics.Outer) / 2,
            WheelRing.Third => (metrics.Outer + metrics.Third) / 2,
            _ => (metrics.Edge + diameter / 2) / 2,
        };
        var count = reference.Ring switch
        {
            WheelRing.Outer => metrics.OuterCount,
            WheelRing.Third => Math.Max(1, metrics.ThirdCount),
            WheelRing.Extended => WheelShape.ExtendedCount,
            _ => WheelShape.InnerCount,
        };
        var angle = reference.Index * Math.PI * 2 / count;
        return (radius * Math.Sin(angle), -radius * Math.Cos(angle));
    }
}
