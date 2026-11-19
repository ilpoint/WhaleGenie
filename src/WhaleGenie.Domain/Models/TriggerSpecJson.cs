using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Models;

/// <summary>
/// The saved shape of a trigger's settings. A record per kind, so what is written is exactly what
/// that kind has, and what is read is required to be there.
/// </summary>
/// <remarks>
/// Nothing here falls back to a default. A file written by an older shape is refused by the
/// package version rather than patched up field by field: a missing setting would otherwise become
/// a silently different macro, and the person would have no way to tell which one changed.
/// </remarks>
public static class TriggerSpecJson
{
    public static JsonObject ToJson(TriggerSpec spec)
    {
        var node = new JsonObject { [FieldNames.Kind] = spec.Kind.ToString().ToLowerInvariant() };

        switch (spec)
        {
            case KeySpec key:
                node[FieldNames.BindKey] = key.Binding;
                node[FieldNames.LoopMode] = key.Loop.ToString();
                node[FieldNames.TriggerOnce] = key.Once;
                break;

            case ColorSpec colour:
                node[FieldNames.ColorMatch] = colour.Match.ToString();
                node[FieldNames.ColorPosition] = new JsonObject
                {
                    [FieldNames.X] = colour.X,
                    [FieldNames.Y] = colour.Y,
                };
                node[FieldNames.UseCursorPosition] = colour.FollowCursor;
                node[FieldNames.HexColor] = colour.Hex;
                node[FieldNames.ColorTolerance] = colour.Tolerance;
                node[FieldNames.LoopMode] = colour.Loop.ToString();
                node[FieldNames.TriggerOnce] = colour.Once;
                break;

            case TimerSpec timer:
                node[FieldNames.Schedule] = new JsonObject
                {
                    [FieldNames.Mode] = timer.Mode.ToString(),
                    [FieldNames.Interval] = timer.Interval,
                    [FieldNames.Unit] = timer.Unit.ToString(),
                    [FieldNames.Time] = timer.Time,
                };
                node[FieldNames.TriggerOnce] = timer.Once;
                break;

            case FileSpec file:
                node[FieldNames.Watch] = new JsonObject
                {
                    [FieldNames.Path] = file.Path,
                    [FieldNames.Change] = file.Change.ToString(),
                    [FieldNames.Filter] = file.Filter,
                    [FieldNames.Subfolders] = file.Subfolders,
                };
                node[FieldNames.TriggerOnce] = file.Once;
                break;

            case ProcessSpec process:
                node[FieldNames.Process] = new JsonObject
                {
                    [FieldNames.Name] = process.Name,
                    [FieldNames.Change] = process.Change.ToString(),
                };
                node[FieldNames.TriggerOnce] = process.Once;
                break;

            case WindowSpec window:
                node[FieldNames.Window] = window.Target.ToJson();
                node[FieldNames.WindowChange] = window.Change.ToString();
                node[FieldNames.TriggerOnce] = window.Once;
                break;

            case IdleSpec idle:
                node[FieldNames.IdleSeconds] = idle.Seconds;
                node[FieldNames.TriggerOnce] = idle.Once;
                break;

            case HotstringSpec hotstring:
                node[FieldNames.Abbreviation] = hotstring.Abbreviation;
                node[FieldNames.EndChars] = hotstring.EndChars;
                node[FieldNames.CaseSensitive] = hotstring.CaseSensitive;
                node[FieldNames.MatchInsideWord] = hotstring.InsideWord;
                node[FieldNames.RequireEndChar] = hotstring.RequireEndChar;
                node[FieldNames.EraseTyped] = hotstring.EraseTyped;
                node[FieldNames.KeepEndChar] = hotstring.KeepEndChar;
                break;

            case InterfaceSpec group:
                node[FieldNames.Window] = group.Window.ToJson();
                break;

            case MenuSpec menu:
                node[FieldNames.Page] = menu.PageId;
                node[FieldNames.Row] = menu.Row;
                node[FieldNames.Column] = menu.Column;
                if (menu.Icon is { IsEmpty: false } icon)
                {
                    node[FieldNames.Icon] = icon.ToJson();
                }

                break;
        }

        return node;
    }

    /// <param name="id">The entry the node belongs to, so a broken file names the row it is about.</param>
    public static TriggerSpec FromJson(JsonObject node, string id) => Kind(node, id) switch
    {
        TriggerKind.Key => new KeySpec(
            Text(node, FieldNames.BindKey, id),
            Choice<MacroLoop>(node, FieldNames.LoopMode, id),
            Flag(node, FieldNames.TriggerOnce, id)),

        TriggerKind.Color => new ColorSpec(
            Choice<ColorMatchCondition>(node, FieldNames.ColorMatch, id),
            Number(Position(node, id), FieldNames.X, id),
            Number(Position(node, id), FieldNames.Y, id),
            Flag(node, FieldNames.UseCursorPosition, id),
            Text(node, FieldNames.HexColor, id),
            Number(node, FieldNames.ColorTolerance, id),
            Choice<MacroLoop>(node, FieldNames.LoopMode, id),
            Flag(node, FieldNames.TriggerOnce, id)),

        TriggerKind.Timer => new TimerSpec(
            Choice<ScheduleMode>(Record(node, FieldNames.Schedule, id), FieldNames.Mode, id),
            Text(Record(node, FieldNames.Schedule, id), FieldNames.Time, id),
            Number(Record(node, FieldNames.Schedule, id), FieldNames.Interval, id),
            Choice<ScheduleUnit>(Record(node, FieldNames.Schedule, id), FieldNames.Unit, id),
            Flag(node, FieldNames.TriggerOnce, id)),

        TriggerKind.File => new FileSpec(
            Text(Record(node, FieldNames.Watch, id), FieldNames.Path, id),
            Choice<FileChangeKind>(Record(node, FieldNames.Watch, id), FieldNames.Change, id),
            Text(Record(node, FieldNames.Watch, id), FieldNames.Filter, id),
            Flag(Record(node, FieldNames.Watch, id), FieldNames.Subfolders, id),
            Flag(node, FieldNames.TriggerOnce, id)),

        TriggerKind.Process => new ProcessSpec(
            Text(Record(node, FieldNames.Process, id), FieldNames.Name, id),
            Choice<ProcessChangeKind>(Record(node, FieldNames.Process, id), FieldNames.Change, id),
            Flag(node, FieldNames.TriggerOnce, id)),

        TriggerKind.Window => new WindowSpec(
            Rule(node, id),
            Choice<WindowChangeKind>(node, FieldNames.WindowChange, id),
            Flag(node, FieldNames.TriggerOnce, id)),

        TriggerKind.Idle => new IdleSpec(
            Number(node, FieldNames.IdleSeconds, id),
            Flag(node, FieldNames.TriggerOnce, id)),

        TriggerKind.Hotstring => new HotstringSpec(
            Text(node, FieldNames.Abbreviation, id),
            Text(node, FieldNames.EndChars, id),
            Flag(node, FieldNames.CaseSensitive, id),
            Flag(node, FieldNames.MatchInsideWord, id),
            Flag(node, FieldNames.RequireEndChar, id),
            Flag(node, FieldNames.EraseTyped, id),
            Flag(node, FieldNames.KeepEndChar, id)),

        TriggerKind.Interface => new InterfaceSpec(Rule(node, id)),

        _ => new MenuSpec(
            Text(node, FieldNames.Page, id),
            Number(node, FieldNames.Row, id),
            Number(node, FieldNames.Column, id),
            node[FieldNames.Icon] is JsonObject icon ? MenuIconReference.FromJson(icon) : null),
    };

    private static TriggerKind Kind(JsonObject node, string id)
    {
        var text = Text(node, FieldNames.Kind, id);
        return Enum.TryParse<TriggerKind>(text, ignoreCase: true, out var kind)
               && Enum.IsDefined(kind)
            ? kind
            : throw Broken(id, FieldNames.Kind, text);
    }

    private static WindowRule Rule(JsonObject node, string id)
        => node[FieldNames.Window] is JsonObject rule
            ? WindowRule.FromJson(rule)
            : throw Missing(id, FieldNames.Window);

    private static JsonObject Position(JsonObject node, string id)
        => node[FieldNames.ColorPosition] is JsonObject point ? point : throw Missing(id, FieldNames.ColorPosition);

    private static JsonObject Record(JsonObject node, string name, string id)
        => node[name] is JsonObject record ? record : throw Missing(id, name);

    private static string Text(JsonObject node, string name, string id)
        => node[name] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : throw Missing(id, name);

    private static bool Flag(JsonObject node, string name, string id)
        => node[name] is JsonValue value && value.TryGetValue<bool>(out var flag)
            ? flag
            : throw Missing(id, name);

    private static int Number(JsonObject node, string name, string id)
        => node[name] is JsonValue value && value.TryGetValue<int>(out var number)
            ? number
            : throw Missing(id, name);

    private static T Choice<T>(JsonObject node, string name, string id) where T : struct, Enum
    {
        var text = Text(node, name, id);
        return Enum.TryParse<T>(text, ignoreCase: true, out var choice) && Enum.IsDefined(choice)
            ? choice
            : throw Broken(id, name, text);
    }

    private static InvalidDataException Missing(string id, string name)
        => new($"Trigger \"{id}\" has no \"{name}\".");

    private static InvalidDataException Broken(string id, string name, string value)
        => new($"Trigger \"{id}\" has \"{value}\" where \"{name}\" should say one of the values this version knows.");
}
