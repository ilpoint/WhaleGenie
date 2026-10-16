using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One thing a page step says the element it means has to be like, as the dialog edits it: which way
/// of narrowing the page down, and what to look for by it — words the element writes, an element it
/// holds, where it sits, or nothing at all when the answer is "only one a person could see".
/// </summary>
/// <remarks>
/// The ways offered are the ones the browser engine itself puts on an element, so the dialog is
/// asking the page a question the page already answers rather than a matching rule of the program's
/// own. A row is what the person filling the form sees; the step writes down the way and the value.
/// </remarks>
public partial class NarrowRowViewModel : ViewModelBase
{
    /// <summary>
    /// The ways of narrowing a page down, in the order they are offered. Looking inside a container
    /// comes first because it is the one that reads as a sentence about where the element is; only
    /// what can be seen comes last because it is the one that needs nothing typed beside it.
    /// </summary>
    public static IReadOnlyList<ActionParameterOption> Ways { get; } =
    [
        .. new[]
        {
            PageFilter.Inside, PageFilter.HasText, PageFilter.HasNotText, PageFilter.Has,
            PageFilter.HasNot, PageFilter.Visible,
        }.Select(way => new ActionParameterOption(way.ToString(),
            Strings.Get("Add.Narrow." + way))),
    ];

    /// <summary>The same list for one row to offer, which is what the dropdown in it is bound to.</summary>
    public IReadOnlyList<ActionParameterOption> Choices => Ways;

    /// <summary>
    /// Which way of narrowing this row asks for. Changing it changes whether the row needs a value
    /// at all, and lets go of the count the last look reported, which was an answer about the way
    /// that was chosen then.
    /// </summary>
    [ObservableProperty]
    public partial ActionParameterOption Way { get; set; } = Ways[0];

    /// <summary>What this row looks for: a selector, or a piece of the writing the element shows.</summary>
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
    public bool NeedsValue => Chosen() is not PageFilter.Visible;

    /// <summary>
    /// True when this row looks for writing rather than for an element, which is what decides what a
    /// pick off the page fills the value in with: the words the element shows, or the element.
    /// </summary>
    public bool WantsWords => Chosen() is PageFilter.HasText or PageFilter.HasNotText;

    /// <summary>What the value box says it is for, which the way chosen is what it depends on.</summary>
    public string ValueHint => Strings.Get("Add.NarrowValue." + Way.Value);

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
    public PageCondition? ToCondition()
    {
        var value = NeedsValue ? Value.Trim() : string.Empty;
        return value.Length > 0 || !NeedsValue
            ? new PageCondition(Chosen(), value)
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
    public static NarrowRowViewModel From(StepParameterRow row)
    {
        var written = row.Text("how").Trim();
        var way = Ways.FirstOrDefault(choice =>
            string.Equals(choice.Value, written, StringComparison.OrdinalIgnoreCase));
        return new NarrowRowViewModel
        {
            Way = way ?? Ways[0],
            Value = row.Text("value"),
        };
    }

    /// <summary>Which way of narrowing this row asks for, as the engine knows it.</summary>
    private PageFilter Chosen()
        => Enum.TryParse<PageFilter>(Way?.Value ?? string.Empty, ignoreCase: true, out var way)
            ? way
            : PageFilter.HasText;

    partial void OnWayChanged(ActionParameterOption value)
    {
        Count = string.Empty;
        OnPropertyChanged(nameof(NeedsValue));
        OnPropertyChanged(nameof(ValueHint));
    }

    partial void OnCountChanged(string value) => OnPropertyChanged(nameof(HasCount));
}
