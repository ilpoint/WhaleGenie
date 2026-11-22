using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;


namespace WhaleGenie.ViewModels;

// Deciding which calls fold onto more than one line to fit the width.
/// <summary>
/// Decides when a call is too wide to stay on one line. A short call keeps the compact row; a long
/// one gets a row per argument. Widths are estimates from the text, not measured pixels: this has
/// to say the same thing in a headless test as it does on a wide screen.
/// </summary>
internal static class SlotStacking
{
    /// <summary>More arguments than this always get their own rows.</summary>
    public const int ArgumentThreshold = 3;

    /// <summary>One character of the band's monospace face, in device independent pixels.</summary>
    public const double CharWidthPx = 7;

    /// <summary>What one token adds on top of its letters: padding, margins and a rounded edge.</summary>
    public const double TokenOverheadPx = 20;

    /// <summary>The width a compact call is compared with when no window has measured one yet.</summary>
    public const double CompactWidthPx = 560;

    /// <summary>
    /// A nested call on its own is not a reason to unfold: a two-argument call that merely contains
    /// another call stayed on one line quite happily, and unfolding it doubled the height of the band.
    /// </summary>
    public static bool Decide(SlotCall call, double availableWidth)
        => call.Arguments.Count > ArgumentThreshold
            || EstimateWidth(call) > Math.Min(availableWidth, CompactWidthPx);

    public static double EstimateWidth(SlotCall call)
    {
        var width = (call.Name.Length + 2) * CharWidthPx + TokenOverheadPx;
        foreach (var argument in call.Arguments)
        {
            width += 2 * CharWidthPx;
            foreach (var piece in argument.Items)
            {
                width += WrittenWidth(piece);
            }
        }

        return width;
    }

    private static double WrittenWidth(SlotNode piece) => piece switch
    {
        SlotCall nested => EstimateWidth(nested),
        _ => Math.Max(1, piece.Length) * CharWidthPx + TokenOverheadPx,
    };
}
