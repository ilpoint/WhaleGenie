using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// A scene the wheel shows: its slots, what it overrides and how it is saved.
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
