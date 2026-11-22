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

// What a run handed over, as the panel under the step list shows it.
/// <summary>
/// One value a step worked out, as the panel under its row shows it: what it is called and what it
/// holds. <paramref name="Kept"/> says whether the macro keeps it under a name of its own; a value
/// nobody ticked is shown all the same, because looking at what a step could hand over is the
/// reason to open a debugger at all.
/// </summary>
public sealed record RunResultViewModel(string Name, string Value, bool Kept)
{
    /// <summary>
    /// The fields of a record result, one line each, or empty when the result is one piece of data
    /// whose text is the whole of it. A hit is several answers about one thing — where it is, how
    /// sure the reading was — and a line that ran them together is exactly what cannot be read.
    /// </summary>
    public IReadOnlyList<RunResultLine> Lines { get; init; } = [];

    /// <summary>
    /// The colour the name reads in: a value the macro keeps under a name of its own stands out
    /// from one that is only here to be looked at.
    /// </summary>
    public string Color => Kept ? "#BFE3BF" : "#8FB3D9";
}

/// <summary>
/// One line under a step's result: a field of a record, or one entry of a list of them, indented by
/// how deep it sits.
/// </summary>
public sealed record RunResultLine(string Name, string Value);

/// <summary>
/// One try at a step, already worded for the debugger: when it ran, how much of the step's budget
/// was left, and why it failed if it did.
/// </summary>
public sealed record RunAttemptViewModel(string Note, string Failure, bool HasFailure);

/// <summary>
/// Turns the fields of a record into those lines. It is arithmetic of its own rather than a
/// walk over the panel's controls, because the shape is what is worth checking: a hit is three
/// answers about one place, and a list of hits is three places with each one's answers under it.
/// </summary>
public static class RunResultLines
{
    /// <summary>The lines for the fields of one result, the fields themselves one level in.</summary>
    public static IReadOnlyList<RunResultLine> Of(IReadOnlyList<JsonTreeNode> fields)
    {
        var lines = new List<RunResultLine>();
        Add(fields, depth: 1, lines);
        return lines;
    }

    private static void Add(IReadOnlyList<JsonTreeNode> nodes, int depth, List<RunResultLine> lines)
    {
        foreach (var node in nodes)
        {
            lines.Add(new RunResultLine(node.Name, node.Preview));

            // A field that is a record or a list of them keeps its own name and puts what is inside
            // it on the lines below, so a list of hits reads as hits rather than as one long line.
            if (node.Children.Count > 0)
            {
                Add(node.Children, depth + 1, lines);
            }
        }
    }
}

/// <summary>How one step of the macro went, for the list beside the variables.</summary>
public sealed record StepOutcome(string Name, string Label, string Value)
{
    public string Color => Value switch
    {
        "failed" => "#E06C75",
        "ok" => "#8CC98C",
        _ => "#9A9A9A",
    };
}
