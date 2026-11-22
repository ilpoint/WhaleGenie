using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// The arguments a direct run asks for before it starts, and the pages that ask for them.
/// <summary>
/// One declared parameter as the debug window asks for it before a run. It is not a call site: a
/// direct run supplies the values from this window, a trigger supplies none and takes the default.
/// </summary>
public sealed partial class RunArgumentViewModel : ObservableObject
{
    public required string Name { get; init; }

    public required string TypeLabel { get; init; }

    public required bool Required { get; init; }

    public required string Default { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>The name shown beside the field; an optional one says so.</summary>
    public string Label => Required
        ? Name
        : Strings.Format("Common.OptionalSuffix", Name);

    /// <summary>What the field says under itself: the declaration's own words, the kind and the default.</summary>
    public string Hint
    {
        get
        {
            var parts = new List<string>();
            if (Description.Length > 0)
            {
                parts.Add(Description);
            }

            parts.Add(Strings.Format("Run.ArgumentType", TypeLabel));
            if (Required)
            {
                parts.Add(Strings.Get("Run.ArgumentRequired"));
            }
            else if (Default.Trim().Length > 0)
            {
                parts.Add(Strings.Format("Run.ArgumentDefault", Default));
            }

            return string.Join(" ", parts);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    public partial string Value { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    public partial bool Given { get; set; }

    /// <summary>True while a required value has neither been given nor marked as an empty one.</summary>
    public bool IsMissing => Required && !Given && Value.Trim().Length == 0;

    /// <summary>Raised when this row changes, so the run buttons can re-check what is still missing.</summary>
    public event Action? Changed;

    partial void OnValueChanged(string value) => Changed?.Invoke();

    partial void OnGivenChanged(bool value) => Changed?.Invoke();
}

/// <summary>Which half of the run-and-debug window is showing.</summary>
public enum RunPage
{
    /// <summary>The steps, how each of them went, the variables in play and the log.</summary>
    Debug,

    /// <summary>Which variables there are, and the shared ones' own fields.</summary>
    Variables,
}
