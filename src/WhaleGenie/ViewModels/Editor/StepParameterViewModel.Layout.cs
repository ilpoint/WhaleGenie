using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.ViewModels;

// Where the field sits on the form, and the numbers a range or a jitter shows.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>
    /// True when this parameter is one half of a screen position. Set by the dialog once it
    /// knows the action carries both an x and a y, so either field can take the pointer's place.
    /// </summary>
    public bool IsCoordinate { get; set; }

    /// <summary>True on the first of the two numbers a point picker can fill together.</summary>
    public bool IsPointAnchor { get; set; }

    /// <summary>
    /// True when this parameter shares its line with the next one. The two halves of a screen
    /// position always do; the corners of a drag do too, even though the pointer shortcut does
    /// not fill them.
    /// </summary>
    public bool IsRowPair { get; set; }

    /// <summary>
    /// True on the parameter whose line carries the region picker. Set by the dialog when the
    /// action works on a rectangle of the screen, so one button fills the whole group.
    /// </summary>
    public bool IsRegionAnchor { get; set; }

    /// <summary>
    /// True on the parameter whose line carries the button that picks a click point on the picture
    /// the step saw. Set by the dialog on the first half of the offset of an action that looks at
    /// the screen, which is the only place there is something to point at.
    /// </summary>
    public bool IsOffsetAnchor { get; set; }

    /// <summary>
    /// True on the tolerance line of a multi-point colour search: the colours that have to surround
    /// the base colour are listed under it, with the button that picks them off the picture.
    /// </summary>

    public bool IsMultiline => Definition.Kind is ActionParameterKind.MultilineText or
        ActionParameterKind.Code;

    /// <summary>
    /// True when what goes in this box is a piece of a program rather than a sentence: monospaced,
    /// lines left unbroken so the indentation stays where it was written, and taller than the
    /// usual four lines of writing.
    /// </summary>
    public bool IsCode => Definition.Kind is ActionParameterKind.Code;

    /// <summary>
    /// How tall a box of several lines stands: the usual four lines of writing, or the dozen or so
    /// a script of its own needs.
    /// </summary>
    public double MultilineHeight => IsCode ? 240 : 76;

    /// <summary>
    /// Whether the box folds long lines. Writing wraps, because a sentence has no wrong place to
    /// break it; a program does not, because a line broken where the author did not break it reads
    /// as two statements. A long line is read by scrolling instead.
    /// </summary>
    public Avalonia.Media.TextWrapping Wrap => IsCode
        ? Avalonia.Media.TextWrapping.NoWrap
        : Avalonia.Media.TextWrapping.Wrap;

    public bool IsNumber => Definition.Kind is ActionParameterKind.Number;

    /// <summary>
    /// True on one of the four numbers a rectangle is said with — where its corner is, how far it
    /// reaches. The four are one sentence and are edited on one line, the way a place in a search
    /// is; two lines of two, each with its own paragraph underneath, is a page of words for four
    /// numbers.
    /// </summary>
    public bool IsRegionQuad { get; set; }

    /// <summary>
    /// What this number is called on the one line the four of a rectangle share: "X" beside a box
    /// says what "top-left X" says above it, and only one of them fits across four.
    /// </summary>
    public string QuadShortLabel => Definition.Name switch
    {
        "x" => Strings.Get("Add.RegionXShort"),
        "y" => Strings.Get("Add.RegionYShort"),
        FieldNames.Width => Strings.Get("Add.RegionWidthShort"),
        FieldNames.Height => Strings.Get("Add.RegionHeightShort"),
        _ => Definition.LocalLabel,
    };

    /// <summary>True when the number is typed in as a plain number.</summary>
    public bool IsNumberValue => IsNumber && !UseFormula;

    /// <summary>True when the number is written as an expression.</summary>
    public bool IsNumberFormula => IsNumber && UseFormula;

    /// <summary>True when the number is a length of time rather than a count or a pixel.</summary>
    public bool IsDuration => IsNumber && Definition.IsDuration;

    /// <summary>
    /// True for a number counted in percent. A share is read by eye rather than counted out, so it
    /// is given a slider beside the box: dragging says more about "about this much" than typing a
    /// number does, and the box stays there for the times it does not.
    /// </summary>
    public bool IsPercent => IsNumber && Definition.Unit == ActionUnit.Percent && !UseFormula;

    /// <summary>
    /// The same number the box holds, as the slider holds it. One value, two ways to say it: the
    /// slider is not a second answer, so it never has to be kept in step with the box.
    /// </summary>
    public double PercentValue
    {
        get => (double)(NumberValue ?? 0m);
        set
        {
            // The slider is built for every number field and only shown for a percentage. A hidden
            // slider still holds a value, and one bounded by 0–100 will write 100 back the moment a
            // pixel field is set to 300 — so the write is refused where the slider has no say.
            if (!IsPercent)
            {
                return;
            }

            var rounded = decimal.Round((decimal)value, 1, MidpointRounding.AwayFromZero);
            if (NumberValue != rounded)
            {
                NumberValue = rounded;
            }
        }
    }

    /// <summary>
    /// What this percentage comes to where it is measured against a colour channel: "5%" is what
    /// the file says, ±13 out of 255 is what the eye can check. Empty for a share of an area or a
    /// probability, where the same percentage would be a different number entirely.
    /// </summary>
    public string ChannelNote
    {
        get
        {
            if (!Definition.PerChannelPercent || UseFormula || NumberValue is not { } percent)
            {
                return string.Empty;
            }

            var reach = decimal.Round(255m * Math.Clamp(percent, 0m, 100m) / 100m);
            return Strings.Format("Add.ToleranceChannel", reach);
        }
    }

    /// <summary>True when there is a conversion worth showing under the field.</summary>
    public bool HasChannelNote => ChannelNote.Length > 0;

    /// <summary>
    /// What the give does to the number on screen, so the range is never a sum to do in the head.
    /// Empty while there is nothing to say: no give, no number yet, or an expression whose value
    /// is only known when the macro runs.
    /// </summary>
    public string JitterRange
    {
        get
        {
            var percent = Math.Min(100m, JitterPercent ?? 0m);
            var milliseconds = Milliseconds;
            if (!IsDuration || UseFormula || percent <= 0m || milliseconds <= 0m)
            {
                return string.Empty;
            }

            var give = milliseconds * percent / 100m;
            return Strings.Format("Add.JitterRange",
                DurationUnit.Written(decimal.Round(milliseconds - give)),
                DurationUnit.Written(decimal.Round(milliseconds + give)));
        }
    }

    /// <summary>True when there is a range worth showing under the field.</summary>
    public bool HasJitterRange => JitterRange.Length > 0;

    /// <summary>True while the unit dropdown belongs on screen beside a dialled-in time.</summary>
    public bool IsDurationEditor => IsDuration && !UseFormula;

    /// <summary>True while a length of time is written as an expression, which counts milliseconds.</summary>
    public bool IsDurationFormula => IsDuration && UseFormula;

    /// <summary>
    /// What the box under a number written as an expression says, so the way back to a plain number
    /// is not something to be discovered by accident.
    /// </summary>
    public string FormulaNote => Strings.Get("Add.FormulaNumber");

    /// <summary>True when what the field holds is a plain number rather than an expression.</summary>
    public bool TextIsNumber => NumberField.IsNumberText(Text);

    public bool IsBool => Definition.Kind is ActionParameterKind.Bool;

    public bool IsChoice => Definition.Kind is ActionParameterKind.Choice;

    /// <summary>
    /// True when this parameter names a step of this macro. It is picked from a list rather than
    /// typed, because the name is four characters nobody should have to copy by eye, and it is
    /// shown with the note and the action it belongs to, which is how the user recognises it.
    /// </summary>
    public bool IsStepChoice => Definition.Kind is ActionParameterKind.Step;

    /// <summary>
    /// True when the value is a path, so the dialog can offer the file or folder that is already
    /// on the machine instead of making the user spell it out — a path with spaces and Chinese in
    /// it is not something anybody wants to type twice.
    /// </summary>
    public bool IsPath => Definition.PathIntent is not PathIntent.None;

    /// <summary>
    /// How much has to be typed before the names come up over this box. A box that mostly holds
    /// written text — a path, a picture reference — waits for the first character, so clicking into
    /// it to edit the text does not put a list of names over the form; a box that holds nothing but
    /// a name has no text to type blind, so its names are there as soon as it is opened.
    /// </summary>
    public int NamePrefix => IsPath || IsImage ? 1 : 0;

    /// <summary>True when this parameter is the list of results the step may hand over.</summary>
    public bool IsReturns => Definition.Kind is ActionParameterKind.Returns;

    /// <summary>
    /// True when this parameter holds what a page step says the element it means has to be like,
    /// beyond the way it is named.
    /// </summary>
    public bool IsNarrow => Definition.Kind is ActionParameterKind.Narrow;

    /// <summary>
    /// What the element has to be like besides the way it is named: one row per thing, all of them
    /// holding at once. A page that shows the same button twice is told apart here rather than by
    /// counting the matches, which is what a person cannot read off the page.
    /// </summary>
    public ObservableCollection<NarrowRowViewModel> NarrowRows { get; } = [];

    /// <summary>True while at least one row has been added, filled in or not.</summary>
    public bool HasNarrowRows => NarrowRows.Count > 0;
}
