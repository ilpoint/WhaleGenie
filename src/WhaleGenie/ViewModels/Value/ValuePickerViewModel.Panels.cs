using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

// The language itself, spelled out: the property below is the text being edited, which hides the
// static class of the same name inside this file.
using Language = WhaleGenie.Core.Expressions.Expression;

namespace WhaleGenie.ViewModels;

// The three panels: the wall, the signs, the plain values, and which function group is shown.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    public VariableWallViewModel Wall { get; }

    /// <summary>The signs a formula is built from, written as the engine reads them.</summary>
    public IReadOnlyList<ExpressionOperator> Operators { get; }

    /// <summary>The plain words that are worth a button rather than a memory.</summary>
    public IReadOnlyList<ValueToken> Values { get; }

    /// <summary>What this window edits, taken from the field's declared reading.</summary>
    public ValueEditorMode Mode { get; }

    /// <summary>True for a plain text field: one body of text, no tokens and no formula.</summary>
    public bool IsTextMode => Mode is ValueEditorMode.Text;

    /// <summary>True for a text field that may name variables: words and names in one run.</summary>
    public bool IsRunMode => Mode is ValueEditorMode.Run;

    /// <summary>True for a whole-value field: the token band and the three material panels.</summary>
    public bool IsValueMode => Mode is ValueEditorMode.Value;

    /// <summary>True for a field that holds one name: the list, its filters and a new-name row.</summary>
    public bool IsNameMode => Mode is ValueEditorMode.Name;

    /// <summary>True where the structure band is the editing surface.</summary>
    public bool ShowsBand => IsValueMode || IsRunMode;

    /// <summary>True where the operators, values and functions can be put into the band.</summary>
    public bool ShowsFormulaMaterials => IsValueMode;

    /// <summary>How many of the lower columns the variable panel takes: all of them in a run.</summary>
    public int PanelSpan => IsValueMode ? 1 : 3;

    /// <summary>True while the band has nothing in it, which is where the starting prompt goes.</summary>
    public bool IsValueEmpty => ShowsBand && _draft.IsEmpty;

    /// <summary>What the empty band says before the first piece.</summary>
    public string EmptyBandHint => IsRunMode
        ? Strings.Get("ValuePicker.Empty.Run")
        : Strings.Get("ValuePicker.Empty.Value");

    /// <summary>The one short line above the band saying what this mode's pieces are.</summary>
    public string StructureHint => IsRunMode
        ? Strings.Get("ValuePicker.Structure.RunHint")
        : Strings.Get("ValuePicker.Structure.Hint");

    public string Title => Mode switch
    {
        ValueEditorMode.Name => Strings.Get("ValuePicker.Names"),
        ValueEditorMode.Text => Strings.Get("ValuePicker.Title.Text"),
        ValueEditorMode.Run => Strings.Get("ValuePicker.Title.Run"),
        _ => Strings.Get("ValuePicker.Title"),
    };

    /// <summary>The one line under the window saying what this mode expects.</summary>
    public string Hint => Mode switch
    {
        ValueEditorMode.Name => Strings.Get("ValuePicker.Hint.Name"),
        ValueEditorMode.Text => Strings.Get("ValuePicker.Hint.Text"),
        ValueEditorMode.Run => Strings.Get("ValuePicker.Hint.Run"),
        _ => Strings.Get("ValuePicker.Hint"),
    };

    /// <summary>Which cell the window was opened for. Empty when nobody said.</summary>
    public string Target { get; }

    public string TargetLine => Target.Length > 0
        ? Strings.Format("ValuePicker.Target", Target)
        : string.Empty;

    public bool HasTarget => TargetLine.Length > 0;

    /// <summary>
    /// The shape drawn as one line under the band. It is a picture for the eye, never an input:
    /// the window has no way to read a line back into the value it keeps.
    /// </summary>
    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    /// <summary>One line under the formula: whether it reads, or why it does not.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool NoteIsProblem { get; set; }

    /// <summary>The formula said in words, for a reader who does not follow the punctuation.</summary>
    [ObservableProperty]
    public partial string Saying { get; set; } = string.Empty;

    /// <summary>True when the formula reads, which is what lets it be handed back.</summary>
    public bool Reads => _reads;

    public bool HasSaying => Saying.Length > 0;

    /// <summary>What is typed into the function panel's search box.</summary>
    [ObservableProperty]
    public partial string FunctionQuery { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllFunctions))]
    public partial ExpressionFunctionFilter? SelectedFunctionGroup { get; set; }

    public IReadOnlyList<ExpressionFunctionFilter> FunctionGroups { get; }

    public bool IsAllFunctions => SelectedFunctionGroup is { Group.Length: 0 };

    public ObservableCollection<ExpressionFunctionChoice> Functions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChosenFunction))]
    public partial ExpressionFunctionChoice? SelectedFunction { get; set; }

    public bool HasChosenFunction => SelectedFunction is not null;

    public bool HasChosenVariable => Wall.Looking is not null;

    public bool IsFunctionsEmpty => Functions.Count == 0;

    /// <summary>
    /// The formula as it is nested: a call holds the pieces written inside its brackets, and a piece
    /// that is itself a call holds its own. The band draws this shape, so the brackets on the page and
    /// the brackets in the formula are the same brackets.
    /// </summary>
    [ObservableProperty]
    public partial SlotNode? Recipe { get; set; }

    public bool HasRecipe => Recipe is not null;
}
