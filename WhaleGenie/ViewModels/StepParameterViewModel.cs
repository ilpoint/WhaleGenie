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

/// <summary>
/// Editable wrapper around one <see cref="ActionParameter"/>. Each editor kind has
/// its own property, so the dialog never has to convert between them while typing.
/// </summary>
public partial class StepParameterViewModel : ViewModelBase
{
    private string _assetFolder = string.Empty;
    private DurationUnit _unit = DurationUnit.Catalog[0];

    /// <summary>True while a whole value is being put in at once, rather than typed a character at a time.</summary>
    private bool _writing;

    private WindowPreviewViewModel? _windowPreview;

    /// <summary>
    /// The results this action can work out but does not offer. They are not drawn — how the picture
    /// was taken is not something a macro does anything with — but a step written when they were
    /// offered keeps the ones it handed over: they are put back when the step is saved, so opening
    /// it and saving it does not quietly drop a result a later step may be reading.
    /// </summary>
    private readonly Dictionary<string, string> _keptResults = new(StringComparer.OrdinalIgnoreCase);

    public StepParameterViewModel(ActionParameter definition,
        IReadOnlyList<VariableChoice>? variables = null,
        IReadOnlyList<MacroCallable>? macros = null,
        IReadOnlyList<ActionParameterOption>? steps = null,
        IReadOnlyList<ActionReturn>? results = null)
    {
        Definition = definition;
        Variables = variables ?? [];
        KnownNames = [.. Variables.Select(choice => choice.Name)];
        Macros = [.. (macros ?? []).Select(call => call.Name)];
        Steps = steps ?? [];
        DurationUnits = DurationUnit.Localized();
        _unit = DurationUnits[0];

        // A box that writes a variable name offers only names a macro may write. Every other box
        // reads, and a read may also name what WhaleGenie provides or what an earlier step left
        // behind. A bare-name field takes the name itself, while a value box takes the reader's
        // form with the dollar sign.
        var writesName = definition.NamesVariable
            && MacroStep.DefinesVariable(definition.OwnerKey, definition.Name);
        var offered = writesName
            ? Variables.Where(choice => choice.Scope != VariableScope.System && !choice.IsStepResult)
            : Variables;
        NameChoices = definition.NamesVariable
            ? [.. offered.Select(choice => choice.Name)]
            : [.. offered.Select(choice => "$" + choice.Name)];

        // A field that may name variables offers them; one that is read as a whole value offers the
        // functions as well, because a function name is something the expression reader understands
        // there. Text that is only filled in offers the names alone: a function in it would be
        // taken literally.
        var knowsFunctions = definition.Kind
            is ActionParameterKind.Expression or ActionParameterKind.Number
                or ActionParameterKind.Condition
            || definition.AcceptsFormula;

        // A field that takes a record suggests only the names known to hold one: a number is not a
        // place, and offering it would be offering a mistake. Everything else may hold any value,
        // so every name is offered.
        var recordField = definition.Shape
            is ActionValueShape.Record or ActionValueShape.Point or ActionValueShape.Rect;
        var named = recordField
            ? Variables.Where(choice => choice.Type == "record").Select(choice => choice.Name)
            : KnownNames;
        var suggestions = new List<string>();
        if (knowsFunctions || definition.AcceptsVariables)
        {
            suggestions.AddRange(named.Select(name => "$" + name));
            if (knowsFunctions)
            {
                suggestions.AddRange(Expression.Functions.Select(function => function.Name + "("));
            }
        }

        ExpressionSuggestions = suggestions;
        // The starting value in the interface's own language: a field whose default is a piece of
        // code — the example a script step begins with — has prose in it, which is a thing the
        // message tables carry like a label or a hint rather than something written into the
        // catalogue once.
        Text = definition.LocalDefault;

        if (IsCondition)
        {
            ConditionEditor = new ConditionEditorViewModel(Text, WriteCondition, ExpressionSuggestions,
                Variables);
        }
        Flag = string.Equals(definition.DefaultValue, "true", StringComparison.OrdinalIgnoreCase);

        // The results this action can hand over are listed here, one line each, with the one a step
        // nearly always wants ticked. What each is named is filled in by the dialog, which is the
        // only thing that knows the note and the step's own name.
        if (definition.Kind is ActionParameterKind.Returns)
        {
            foreach (var result in results ?? [])
            {
                if (result.Hidden)
                {
                    continue;
                }

                ReturnsRows.Add(Watch(new ReturnsRowViewModel(result)));
            }
        }
        // A step name is picked from the steps of the macro rather than from the catalogue, so
        // those choices come in from the editor; every other choice is the catalogue's own.
        Choices = definition.Kind is ActionParameterKind.Step
            ? new ObservableCollection<ActionParameterOption>(Steps)
            : new ObservableCollection<ActionParameterOption>(
                definition.OptionChoices.Select(choice => choice with
                {
                    Display = Strings.Get($"{definition.OwnerKey}.{definition.Name}.option.{choice.Value}",
                        choice.Display),
                }));
        // A step has no first choice: picking one would quietly point the step at whichever step
        // of the macro happens to be listed first, and a wrong condition that never says so is
        // worse than one that asks to be filled in.
        Option = definition.Kind is ActionParameterKind.Step
            ? Choices.FirstOrDefault(choice => choice.Value == definition.DefaultValue)
            : PickInitialOption(Choices, definition.DefaultValue);
        IsEnabled = string.IsNullOrEmpty(definition.EnabledBySibling);

        if (decimal.TryParse(definition.DefaultValue, NumberStyles.Number, CultureInfo.InvariantCulture,
                out var number))
        {
            ShowMilliseconds(number);
        }
        else if (definition.Kind is ActionParameterKind.Number
                 && definition.DefaultValue.Trim().Length > 0)
        {
            // A number that was written as an expression opens in the expression editor. One that
            // starts empty is a field nobody has answered yet, and it opens as the numbered box: an
            // empty expression box would read as "this step has a formula" when it has nothing at all.
            UseFormula = true;
        }

        if (definition.Kind is ActionParameterKind.Steps)
        {
            List = new StepListValue();
        }
    }

    public ActionParameter Definition { get; }

    /// <summary>Nested editor, set when this parameter holds steps or a condition.</summary>
    public StepListValue? List { get; }

    /// <summary>
    /// The places this step looks at, when it is one that searches: one row per rectangle, which
    /// the region picker fills in from a drag on the screen.
    /// </summary>
    /// <summary>The colours a pattern wants around its base colour, in the order they are read.</summary>
    public ObservableCollection<ColourPointRowViewModel> ColourPointRows { get; } = [];

    /// <summary>True when this parameter is the pattern of a multi-point colour search.</summary>
    public bool IsColourPoints => Definition.Kind is ActionParameterKind.ColourPoints;

    /// <summary>True while the pattern has at least one row, which is what makes it a pattern.</summary>
    public bool HasColourPoints => ColourPointRows.Count > 0;

    public ObservableCollection<RegionRowViewModel> Regions { get; } = [];

    /// <summary>True when this parameter is a list of the places to search.</summary>
    public bool IsRegion => Definition.Kind is ActionParameterKind.Region;

    /// <summary>The first of the four bounds a window mover can take from one framed rectangle.</summary>
    public bool IsWindowBoundsAnchor =>
        Definition.Picker is ActionPickerKind.WindowBounds && Definition.Name == "x";

    /// <summary>
    /// True when this field belongs beside the one before it on the same line rather than taking a
    /// line of its own, which is how the switch that takes a mouse step's place from the pointer
    /// stands next to the button it belongs to. Set by the dialog.
    /// </summary>
    public bool IsRowCompanion { get; set; }

    /// <summary>True while the step has nowhere in particular to look, which is the whole screen.</summary>
    public bool HasRegions => Regions.Count > 0;

    /// <summary>
    /// The presses of a key run, when this parameter is one: what the macro sends in order, each
    /// row a combination with an optional hold and gap of its own.
    /// </summary>
    public ObservableCollection<KeyRowViewModel> KeyRows { get; } = [];

    /// <summary>True when this parameter is a run of key combinations.</summary>
    public bool IsKeySequence => Definition.Kind is ActionParameterKind.KeySequence;

    /// <summary>True while the run has presses in it.</summary>
    public bool HasKeyRows => KeyRows.Count > 0;

    /// <summary>
    /// True when the run holds a press. A row that is there but says nothing is not one, so a run
    /// counts as filled in when it holds keys rather than rows.
    /// </summary>
    private bool HasKeyPress => KeyRows.Any(row => row.ToRow().Cells.Count > 0);
}
