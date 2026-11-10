using System;
using System.Globalization;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Devices;

// What a search found: one match, the order they are offered in, and the query that asked.
/// <summary>Where a reference image was found, and how well it matched.</summary>
public sealed record ImageMatch(double Score, ScreenPoint Location, ScreenSize Size)
{
    /// <summary>The middle of the match, which is what a click aims at.</summary>
    public ScreenPoint Center => new(Location.X + (Size.Width / 2), Location.Y + (Size.Height / 2));
}

/// <summary>
/// How a reference picture is looked for. The three ways of comparing two pictures pixel for pixel
/// and the one way of pairing up their features are one list rather than two settings, because
/// they answer one question — and because a step that pairs up features has no use for which way
/// pixels are compared, so offering both would leave one of them doing nothing.
/// </summary>
public enum MatchAlgorithm
{
    /// <summary>
    /// Normalised correlation: the area may come out brighter or darker overall and it still
    /// matches, which is what a macro usually wants.
    /// </summary>
    Normed,

    /// <summary>
    /// Normalised cross correlation, which does not take the brightness out first: steadier for a
    /// very small reference picture, where there is little to average.
    /// </summary>
    Correlated,

    /// <summary>
    /// Normalised square difference read the other way round: what it wants is a pixel for pixel
    /// copy. A flat coloured icon or a screenshot of a dialog is the case for it.
    /// </summary>
    Difference,

    /// <summary>
    /// Pairing up the features found in both pictures: slower, and it still finds the thing when
    /// it is drawn at another size or has something small changed about it.
    /// </summary>
    Feature,
}

/// <summary>What order the hits are handed back in, which is what "the third one" means.</summary>
public enum MatchOrder
{
    /// <summary>Down the screen first and then across, the order a person counts them in.</summary>
    Reading,

    /// <summary>
    /// The same reading, from the bottom of the area upwards. A list of things stacked against the
    /// bottom of a window is counted upwards, and the last one drawn is the one nearest the edge.
    /// </summary>
    ReadingUp,

    /// <summary>The same reading, from the right of the area to the left.</summary>
    ReadingBack,

    /// <summary>From the bottom right corner of the area to the top left, the way round that is left.</summary>
    ReadingBackUp,

    /// <summary>The surest first, wherever on the screen it is.</summary>
    Score,

    /// <summary>
    /// The biggest box first. A picture looked for by features comes back at whatever size it was
    /// drawn at, and a reading of writing can be a whole line or one word, so the biggest one is
    /// how a step says "the whole banner, not a piece of it".
    /// </summary>
    Area,

    /// <summary>
    /// Shuffled, for a macro that must not always take the same one of several.
    /// </summary>
    Random,
}

/// <summary>One search asked for: how it is done, how sure it has to be, and what to leave out.</summary>
public sealed record VisionQuery(
    MatchAlgorithm Algorithm = MatchAlgorithm.Normed,
    double ConfidencePercent = 90,
    int MinFeatures = 6,
    PixelColor? Transparent = null,
    int Limit = 1,
    double TolerancePercent = 0);

/// <summary>
/// One run of a detection model over a region: how sure a guess has to be, how many pixels of the
/// region are handed to it, how much two guesses may overlap and still be kept, which classes are
/// wanted, where the names of those classes are written, and how many guesses are collected at
/// most. Which model it is is said by the path of the file, which travels beside this.
/// </summary>
/// <remarks>
/// The overlap is what stops one thing being reported five times: a model answers with a guess per
/// place it thinks the thing is, and two guesses over the same thing are one find rather than two.
/// It is told apart by class, because two different things drawn one over the other are both there.
///
/// The classes are written the way the user wrote them — a name from the label file, or a number —
/// because the file that says which number a name is belongs to the run rather than to the model.
/// </remarks>
public sealed record DetectionQuery(
    double ConfidencePercent = 25,
    int InputSize = 640,
    double IouPercent = 45,
    IReadOnlyList<string>? Labels = null,
    int Limit = 1,
    string LabelFile = "");

/// <summary>
/// One thing a detection model found: where it is on the screen, how sure the model was, and which
/// class of thing it is — both the number the model answers with and the name the user's label file
/// gives that number, which is empty when the file does not name it.
/// </summary>
public sealed record DetectionHit(
    double Score,
    ScreenPoint Location,
    ScreenSize Size,
    int ClassId,
    string Label);

/// <summary>
/// One piece of text found on screen. The confidence is the reading model's own score rather than
/// a percentage: it is the average, over the characters it read, of how far ahead the model's best
/// guess was, so a bigger number means a surer reading but there is no range it has to sit in.
/// </summary>
public sealed record TextSpan(string Text, ScreenPoint Location, ScreenSize Size, double Confidence)
{
    public ScreenPoint Center => new(Location.X + (Size.Width / 2), Location.Y + (Size.Height / 2));
}
