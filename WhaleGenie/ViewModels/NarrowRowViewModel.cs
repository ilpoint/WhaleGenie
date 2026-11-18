using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>一条限定，按引擎要的写法说：哪一种，以及要一段什么字。</summary>
public sealed record NarrowCondition(string How, string Value);

/// <summary>
/// One thing a step says the element it means has to be like, as the dialog edits it: which way of
/// narrowing the element down, and what to look for by it — words the element shows, an element it
/// holds, where it sits, or nothing at all when the answer is "only one a person could see".
/// </summary>
/// <remarks>
/// The ways offered depend on which family the field belongs to, which the catalogue declares: a
/// page and a desktop control can be told apart by different things. A row is what the person
/// filling the form sees; the step writes down the way and the value.
/// </remarks>
public partial class NarrowRowViewModel : ViewModelBase
{
    public NarrowRowViewModel(NarrowFamily family)
    {
        Family = family;
        Choices = NarrowWay.For(family);
        Way = Choices[0];
    }

    /// <summary>Which family of element this field narrows down, which is what its ways come from.</summary>
    public NarrowFamily Family { get; }

    /// <summary>
    /// True for a page step, whose rows can be looked at on the page itself. A desktop control has
    /// no page to look at, so those two buttons are not drawn for it.
    /// </summary>
    public bool OnPage => Family is NarrowFamily.Page;

    /// <summary>The ways this row may ask for, which is what the dropdown in it offers.</summary>
    public IReadOnlyList<NarrowWay> Choices { get; }

    /// <summary>
    /// Which way of narrowing this row asks for. Changing it changes whether the row needs a value
    /// at all, and lets go of the count the last look reported, which was an answer about the way
    /// that was chosen then.
    /// </summary>
    [ObservableProperty]
    public partial NarrowWay Way { get; set; }

    /// <summary>What this row looks for: a selector, or the letters a name has to hold.</summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>
    /// What the last look at the page found with this row and the ones before it, or nothing while
    /// the row has not been looked at. It is the answer to "did this make it clearer", which is the
    /// question a person adding a condition is asking.
    /// </summary>
    [ObservableProperty]
    public partial string Count { get; set; } = string.Empty;

    /// <summary>What takes this row out of the list it is in, told the way every row list is.</summary>
    public Action<NarrowRowViewModel>? Take { get; set; }

    /// <summary>True while what the last look said is worth showing under the row.</summary>
    public bool HasCount => Count.Length > 0;

    /// <summary>
    /// True while this row has something to look for. "Only what can be seen" asks nothing of the
    /// element beyond being on screen, so its value box is not drawn and what is left in it is not
    /// written down either.
    /// </summary>
    public bool NeedsValue => Chosen().NeedsValue;

    /// <summary>
    /// True when this row looks for writing rather than for an element, which is what decides what a
    /// pick off the page fills the value in with: the words the element shows, or the element.
    /// </summary>
    public bool WantsWords => Chosen().WantsWords;

    /// <summary>What the value box says it is for, which the way chosen is what it depends on.</summary>
    public string ValueHint => Chosen().ValueHint;

    /// <summary>Takes this row out of the list.</summary>
    [RelayCommand]
    private void Remove() => Take?.Invoke(this);

    /// <summary>Says what the last look at the page found with this row and the rows above it.</summary>
    public void Say(HighlightResult found) => Count = found.Found
        ? Strings.Format("Add.NarrowMatches", found.Matches)
        : Strings.Get("Add.NarrowNothing");

    /// <summary>
    /// This row the way the engine reads it, or nothing when the row says nothing: a row that has
    /// been added and not filled in is a form somebody is still working on, not a condition.
    /// </summary>
    public NarrowCondition? ToCondition()
    {
        var value = NeedsValue ? Value.Trim() : string.Empty;
        return value.Length > 0 || !NeedsValue
            ? new NarrowCondition(Chosen().Value, value)
            : null;
    }

    /// <summary>
    /// This row the way the step stores it. A row with nothing written in it is not written down at
    /// all, the same way a picture list leaves out the rows nobody filled in.
    /// </summary>
    public StepParameterRow? ToRow()
    {
        var value = NeedsValue ? Value.Trim() : string.Empty;
        if (value.Length == 0 && NeedsValue)
        {
            return null;
        }

        var row = new StepParameterRow();
        row.Columns["how"] = Way.Value;
        if (value.Length > 0)
        {
            row.Columns["value"] = value;
        }

        return row;
    }

    /// <summary>One row back out of the macro file, opening on the first way when it names none.</summary>
    public static NarrowRowViewModel From(StepParameterRow row, NarrowFamily family)
    {
        var written = row.Text("how").Trim();
        var opened = new NarrowRowViewModel(family);
        opened.Way = opened.Choices.FirstOrDefault(choice =>
            string.Equals(choice.Value, written, StringComparison.OrdinalIgnoreCase)) ?? opened.Choices[0];
        opened.Value = row.Text("value");
        return opened;
    }

    /// <summary>Which way of narrowing this row asks for, as the form knows it.</summary>
    private NarrowWay Chosen() => Way ?? Choices[0];

    partial void OnWayChanged(NarrowWay value)
    {
        Count = string.Empty;
        OnPropertyChanged(nameof(NeedsValue));
        OnPropertyChanged(nameof(ValueHint));
    }

    partial void OnCountChanged(string value) => OnPropertyChanged(nameof(HasCount));
}
