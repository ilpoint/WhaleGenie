using System;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One of the ways of naming an element that a pick off a page answered with, offered beside the
/// field so a second way of finding it can be written down without spelling it out by hand.
/// </summary>
/// <remarks>
/// What is worth knowing before choosing is on the button: which kind of naming it is — a test id
/// outlives a redesign, a path of tags does not — and how many elements of the page it names.
/// </remarks>
public sealed class BrowserWayViewModel : ViewModelBase
{
    /// <summary>The way of naming the element, as it is written into the field.</summary>
    public string Selector { get; init; } = string.Empty;

    /// <summary>What the button says: the kind of naming and how many elements it names.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// What puts this way of naming the element into the field as one more line. The field is the
    /// one that knows where it goes, so the button is told what to call rather than the field
    /// knowing which buttons exist.
    /// </summary>
    public Action<BrowserWayViewModel>? Take { get; set; }
}
