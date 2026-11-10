using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

/// <summary>A single step of a macro, saved as one node of the macro's JSON tree.</summary>
public partial class MacroStep : INotifyPropertyChanged
{
    /// <summary>Fully qualified action key, for example <c>input.keyPress</c>.</summary>
    public required string Type { get; init; }

    /// <summary>
    /// The short name this step is known by, written into the macro file so that it survives
    /// saving: a result variable carries it, a condition points at it, and a run reports the step
    /// under it. Empty on steps that have never been saved since ids were introduced, which is
    /// what <see cref="StepIds.Settle"/> fills in when the macro is opened.
    /// </summary>
    public string Id { get; internal set; } = string.Empty;

    public List<StepParameter> Parameters { get; init; } = [];

    /// <summary>
    /// True while the editor shows the steps written inside this one. It is editor state rather
    /// than part of the macro, so it is never written to the file: a macro saved with a block
    /// folded away is the same macro as one saved with it open.
    /// </summary>
    public bool IsExpanded { get; set; } = true;
}
