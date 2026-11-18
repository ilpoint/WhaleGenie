using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One way of saying what an element has to be like besides the way the selector names it, together
/// with whether it needs something typed beside it.
/// </summary>
/// <remarks>
/// The ways come in two sets, one per family, because the two ask different questions: a page can
/// say where an element sits and what it holds, while a desktop control only answers whether it is
/// on screen, whether it can be used, and what it is called. The value is the name stored in the
/// macro file; what is shown and what the box beside it says are looked up from it.
/// </remarks>
public sealed record NarrowWay(string Value, bool NeedsValue, bool WantsWords)
{
    /// <summary>What this way is called, in the reader's own words.</summary>
    public string Display => Strings.Get("Add.Narrow." + Value);

    /// <summary>What the box beside it is for, which is what the way chosen decides.</summary>
    public string ValueHint => Strings.Get("Add.NarrowValue." + Value);

    /// <summary>
    /// The ways one family offers, in the order they are read. Looking at a place on the page comes
    /// first for pages because it reads as a sentence about where the element is; "only what can be
    /// seen" comes last there because it needs nothing typed beside it. For a desktop control the
    /// two that need nothing come first: they are the ones a person reaches for most.
    /// </summary>
    public static IReadOnlyList<NarrowWay> For(NarrowFamily family) => family switch
    {
        NarrowFamily.Element =>
        [
            new(nameof(UiConditionHow.Visible), false, false),
            new(nameof(UiConditionHow.Enabled), false, false),
            new(nameof(UiConditionHow.NameHas), true, true),
            new(nameof(UiConditionHow.NameHasNot), true, true),
        ],

        _ =>
        [
            new(nameof(PageFilter.Inside), true, false),
            new(nameof(PageFilter.HasText), true, true),
            new(nameof(PageFilter.HasNotText), true, true),
            new(nameof(PageFilter.Has), true, false),
            new(nameof(PageFilter.HasNot), true, false),
            new(nameof(PageFilter.Visible), false, false),
        ],
    };
}
