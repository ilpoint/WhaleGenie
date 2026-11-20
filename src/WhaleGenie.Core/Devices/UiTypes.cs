using System;
using System.Globalization;


namespace WhaleGenie.Core.Devices;

// A query for a window own controls: the conditions, the anchor, and what is read back.
/// <summary>
/// One thing an element has to be like besides the way the selector names it. Only what UI
/// Automation answers without hedging: that it is on screen, that it can be used right now, and
/// that its name holds a run of letters. The last one is not the selector's wildcard written
/// again — a wildcard anchors at one end or the whole string, and cannot say "somewhere in the
/// middle".
/// </summary>
public enum UiConditionHow
{
    /// <summary>The one a person can see, rather than the hidden copy beside it.</summary>
    Visible,

    /// <summary>The one that can be used, rather than the greyed-out copy.</summary>
    Enabled,

    /// <summary>Its name holds this run of letters.</summary>
    NameHas,

    /// <summary>Its name holds no such run.</summary>
    NameHasNot,
}

/// <summary>One such condition: which of them, and the letters a name has to hold.</summary>
public sealed record UiCondition(UiConditionHow How, string Value = "");

/// <summary>Which side of the anchor element the one being looked for has to sit on.</summary>
public enum UiSide
{
    Left,
    Right,
    Above,
    Below,
}

/// <summary>
/// Another element the one being looked for has to sit beside, and how far away: the answer to two
/// buttons carrying the same name, of which only one is "the one right of 确定". The anchor is
/// written with the same selector syntax as anything else and read with the same parser, and it
/// cannot have an anchor of its own.
/// </summary>
public sealed record UiAnchor(UiQuery Query, UiSide Side, int Gap = 0)
{
    /// <summary>
    /// Whether a candidate sits on the side asked for, at least <see cref="Gap"/> pixels clear of
    /// the anchor. Only that one axis is compared, so a button to the left and a little higher is
    /// still "on the left". Nothing is decided when either rectangle is empty: a provider that will
    /// not say where an element is cannot answer "which side", and guessing would pick the wrong
    /// button.
    /// </summary>
    public bool Holds(ScreenRect candidate, ScreenRect anchor)
    {
        if (candidate.IsEmpty || anchor.IsEmpty)
        {
            return false;
        }

        var gap = Math.Max(0, Gap);
        return Side switch
        {
            UiSide.Left => candidate.Right <= anchor.Left - gap,
            UiSide.Right => candidate.Left >= anchor.Right + gap,
            UiSide.Above => candidate.Bottom <= anchor.Top - gap,
            UiSide.Below => candidate.Top >= anchor.Bottom + gap,
            _ => false,
        };
    }
}

/// <summary>
/// What to look for through UI Automation. Every part is optional. A window or a page often shows
/// the same control many times over — five rows of one list, four identical buttons — so
/// <paramref name="Index"/> says which of the matches a step means, counted the way a person counts
/// them on screen: across the row first, then down. One is the first one, which is what a step
/// written before this setting existed still asks for. The window the search is narrowed to is
/// recognised by the same rule every other step recognises one with, which is why the two fields of
/// that rule come along: what the text is held against, and how.
/// </summary>
public sealed record UiQuery(
    string? Name = null,
    string? AutomationId = null,
    string? ControlType = null,
    string? ClassName = null,
    string? WindowTitle = null,
    int Index = 1,
    WindowMatch WindowMatch = WindowMatch.Title,
    string WindowCompare = TextCompare.Contains,
    TextCompareOptions WindowOptions = default,
    IReadOnlyList<UiCondition>? Narrow = null,
    UiAnchor? Anchor = null)
{
    /// <summary>
    /// What else the element has to be like, all of it holding at once. A window often shows the
    /// same control more than once — five rows of one list, two identical buttons, one of them
    /// hidden — and the selector alone cannot say "the one I can see".
    /// </summary>
    public IReadOnlyList<UiCondition> Conditions => Narrow ?? [];

    /// <summary>
    /// True when a part of the query is written with wildcards, which cannot go into the search
    /// condition: the provider only knows exact matches. Such a query is answered by walking what
    /// the rest of it narrows down to and comparing by hand.
    /// </summary>
    public bool HasWildcard
        => TextCompare.HasWildcard(Name)
            || TextCompare.HasWildcard(AutomationId)
            || TextCompare.HasWildcard(ClassName);

    /// <summary>
    /// Whether one property of an element answers to what the query wrote for it: with `*` and `?`
    /// when the writing has them (`Button[name='保存*']` is "a button whose name starts with 保存"),
    /// and exactly, letter case aside, when it does not. Exact is the older behaviour and stays the
    /// one for a selector without wildcards, so nothing written before this changes meaning.
    /// </summary>
    public static bool Answers(string? text, string written)
        => TextCompare.HasWildcard(written)
            ? TextCompare.Wildcard(text, written)
            : string.Equals(text, written, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the query would match anything, which is never what was meant.</summary>
    public bool IsEmpty
        => string.IsNullOrWhiteSpace(Name)
           && string.IsNullOrWhiteSpace(AutomationId)
           && string.IsNullOrWhiteSpace(ControlType)
           && string.IsNullOrWhiteSpace(ClassName)
           && string.IsNullOrWhiteSpace(WindowTitle);

    /// <summary>
    /// Reads selector text such as <c>Button[name='Save']</c> or
    /// <c>Edit[automationId='input', class='Edit']</c> back into the query it describes. The text
    /// before the bracket is the kind of control, and everything inside is a property written as
    /// <c>name=value</c>. Anything unrecognised is left out rather than refused, so a selector
    /// with a stray word in it still finds what the rest of it describes.
    /// </summary>
    public static UiQuery Parse(string? selector, string? windowTitle = null, int index = 1,
        WindowMatch windowMatch = WindowMatch.Title,
        string windowCompare = TextCompare.Contains,
        TextCompareOptions windowOptions = default)
    {
        var text = (selector ?? string.Empty).Trim();
        string? name = null;
        string? id = null;
        string? type = null;
        string? cls = null;

        var open = text.IndexOf('[');
        var head = open < 0 ? text : text[..open].Trim();
        if (head.Length > 0)
        {
            type = head;
        }

        var close = text.LastIndexOf(']');
        if (open >= 0 && close > open)
        {
            foreach (var clause in text[(open + 1)..close]
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var split = clause.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                var key = clause[..split].Trim().ToLowerInvariant();
                var value = clause[(split + 1)..].Trim().Trim('\'', '"');
                switch (key)
                {
                    case "name":
                        name = value;
                        break;
                    case "automationid":
                        id = value;
                        break;
                    case "controltype":
                    case "type":
                        type = value;
                        break;
                    case "class":
                    case "classname":
                        cls = value;
                        break;
                }
            }
        }

        var title = (windowTitle ?? string.Empty).Trim();
        return new UiQuery(name, id, type, cls, title.Length == 0 ? null : title, index,
            windowMatch, windowCompare, windowOptions);
    }

    /// <summary>
    /// The selector text this query reads back as: the control type, then the parts that pin it
    /// down, in the order the engine reads them. Only a value the reader can give back unchanged
    /// is written, so a name holding a quote or a comma is left out rather than turning into a
    /// selector that quietly matches something else.
    /// </summary>
    public string ToSelector()
    {
        var parts = new List<string>();

        if (Readable(AutomationId))
        {
            parts.Add($"automationId='{AutomationId!.Trim()}'");
        }

        if (Readable(Name))
        {
            parts.Add($"name='{Name!.Trim()}'");
        }

        if (Readable(ClassName))
        {
            parts.Add($"className='{ClassName!.Trim()}'");
        }

        var type = ControlType?.Trim() ?? string.Empty;
        return parts.Count == 0 ? type : $"{type}[{string.Join(", ", parts)}]";
    }

    /// <summary>
    /// True when a value survives the trip: the reader splits a selector on commas and strips the
    /// quotes around a value, so anything carrying those would come back as a different string.
    /// </summary>
    private static bool Readable(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.Contains(',')
           && !value.Contains('\'')
           && !value.Contains('"')
           && !value.Contains('[')
           && !value.Contains(']');
}

/// <summary>
/// One element of the desktop's UI Automation tree, read where the pointer was. This is what the
/// element picker needs to write a selector a macro can use again.
/// </summary>
/// <summary>
/// What a table handed back: the names of its columns, and the rows of data under them. A grid
/// keeps the names in a strip of its own rather than in a row, which is why they come back
/// separately — a macro that has to know which column holds what reads them, and one that only
/// wants the data is not handed a row that is not data.
/// </summary>
public sealed record UiTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    /// <summary>A table with nothing in it.</summary>
    public static UiTable Empty { get; } = new([], []);
}

/// <summary>
/// Everything one element will say about itself, for a step that wants to look before it acts:
/// whether it can be used at all, whether it is on screen, what it holds, what it is called, and
/// where it sits. A value is whatever the element offers — the text in a box, the caption of a
/// button — and is empty for an element that keeps no value of its own.
/// </summary>
public sealed record UiElementFacts(
    bool Enabled,
    bool Visible,
    string Value,
    string Name,
    string AutomationId,
    string ControlType,
    ScreenPoint Location,
    ScreenSize Size);

public sealed record UiElementInfo(
    string Name,
    string AutomationId,
    string ControlType,
    string ClassName,
    ScreenPoint Location,
    ScreenSize Size,
    string WindowTitle)
{
    /// <summary>
    /// The selector a macro can use again: what kind of control it is, then the stablest thing
    /// the element offers — its automation id when it has one, and otherwise the name a person
    /// reads. A name follows the interface language, so it is the second choice.
    /// </summary>
    public string Selector
    {
        get
        {
            var identified = !string.IsNullOrWhiteSpace(AutomationId);
            var query = new UiQuery(
                Name: identified ? null : Trimmed(Name),
                AutomationId: identified ? AutomationId.Trim() : null,
                ControlType: Trimmed(ControlType));

            return query.ToSelector();
        }
    }

    private static string? Trimmed(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
