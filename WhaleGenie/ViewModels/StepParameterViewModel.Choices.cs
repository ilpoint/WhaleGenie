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

// The values a field may name: variables, macros, steps and units it offers, and when it applies.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>
    /// Variables offered while editing a field that may name one, with what choosing one takes.
    /// </summary>
    public IReadOnlyList<VariableChoice> Variables { get; }

    /// <summary>
    /// The names on their own, which is what the expression reader and the checks use. A name here
    /// is one the reader knows, whether or not the interface offers it: a hand written
    /// <c>$sys.dateTime</c> is a value rather than a mistake, so the check has to be able to say so.
    /// </summary>
    public IReadOnlyList<string> KnownNames { get; }

    /// <summary>
    /// What the box that holds a variable name offers while it is typed in: the macro's own and the
    /// shared names, in the form this box takes — with a dollar sign where the field is read as a
    /// value, bare where the name itself is what gets written down. The names are offered rather
    /// than the rows the wall lists, because the box holds text and a row put in it would be spelled
    /// out as the name of its own type.
    /// </summary>
    public IReadOnlyList<string> NameChoices { get; }

    /// <summary>Macro names offered while editing an <see cref="ActionParameterKind.Macro"/>.</summary>
    public IReadOnlyList<string> Macros { get; }

    /// <summary>Steps of the macro offered while editing an <see cref="ActionParameterKind.Step"/>.</summary>
    public IReadOnlyList<ActionParameterOption> Steps { get; }

    /// <summary>Units offered beside a length-of-time box, labelled in the interface language.</summary>
    public IReadOnlyList<DurationUnit> DurationUnits { get; }

    /// <summary>
    /// Tokens an expression field offers while the user types: the variables the macro
    /// knows, then the built-in function names with their opening bracket.
    /// </summary>
    public IReadOnlyList<string> ExpressionSuggestions { get; }

    /// <summary>
    /// False while this parameter waits on a sibling list (the logic of a condition group
    /// needs two or more conditions). Such a parameter is neither validated nor saved.
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// False while the field this one follows says something it does not answer to. A field that
    /// does not apply is not drawn and is not written into the step: one of the ways of recognising
    /// a picture reports a single hit, so counting the hits and choosing which one are questions
    /// with nothing behind them there.
    /// </summary>
    public bool IsApplicable { get; private set; } = true;

    /// <summary>
    /// Recomputes <see cref="IsApplicable"/> from what the fields it follows say, and reports
    /// whether that changed anything — the dialog counts its rows from this. A field that follows
    /// two switches is answered by both of them: it is drawn only where the two of them agree with
    /// a way of writing it, which is what keeps the numbers off a step that takes its place from the
    /// pointer and the record field off a step that does not take one at all.
    /// </summary>
    public bool UpdateApplicability(string? followed, string? also = null)
    {
        var applies = Answers(Definition.AppliesWhen, Definition.AppliesWhenValues, followed)
            && Answers(Definition.AppliesWhenAlso, Definition.AppliesWhenAlsoValues, also);

        if (applies == IsApplicable)
        {
            return false;
        }

        IsApplicable = applies;
        OnPropertyChanged(nameof(IsApplicable));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(IsIncluded));
        return true;
    }

    /// <summary>
    /// Whether one field this one follows lets it take part: the switch it names says one of the
    /// values it lists, where those values may name "anything in it" rather than a word. A field
    /// that follows nothing always takes part.
    /// </summary>
    private static bool Answers(string follows, IReadOnlyList<string> values, string? said)
    {
        if (follows.Length == 0)
        {
            return true;
        }

        var text = said?.Trim() ?? string.Empty;
        return said is not null
            && (values.Contains(text, StringComparer.OrdinalIgnoreCase)
                || (text.Length > 0
                    && values.Contains(ActionParameter.AnyValue, StringComparer.Ordinal)));
    }
}
