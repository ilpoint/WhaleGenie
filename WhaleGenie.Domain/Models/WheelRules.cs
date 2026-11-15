using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// Which scene a window gets, and the problems a scene rule can have.
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
