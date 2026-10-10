using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices.Platform;
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

    /// <summary>The name the dialog suggested last, so a name the user typed can be told apart.</summary>
    private string _suggested = string.Empty;

    public StepParameterViewModel(ActionParameter definition,
        IReadOnlyList<VariableChoice>? variables = null,
        IReadOnlyList<string>? macros = null, IReadOnlyList<ActionParameterOption>? steps = null)
    {
        Definition = definition;
        Variables = variables ?? [];
        Named = [.. Variables.Select(choice => choice.Name)];
        Macros = macros ?? [];
        Steps = steps ?? [];
        DurationUnits = DurationUnit.Localized();
        _unit = DurationUnits[0];

        // A field that may name variables offers them; one that is read as a whole value offers the
        // functions as well, because a function name is something the expression reader understands
        // there. Text that is only filled in offers the names alone: a function in it would be
        // taken literally.
        var knowsFunctions = definition.Kind
            is ActionParameterKind.Expression or ActionParameterKind.Number
            || definition.AcceptsFormula;
        var suggestions = new List<string>();
        if (knowsFunctions || definition.AcceptsVariables)
        {
            suggestions.AddRange(Named.Select(name => "$" + name));
            if (knowsFunctions)
            {
                suggestions.AddRange(Expression.Functions.Select(function => function.Name + "("));
            }
        }

        ExpressionSuggestions = suggestions;
        Text = definition.DefaultValue;
        Flag = string.Equals(definition.DefaultValue, "true", StringComparison.OrdinalIgnoreCase);
        // A step name is picked from the steps of the macro rather than from the catalogue, so
        // those choices come in from the editor; every other choice is the catalogue's own.
        Choices = definition.Kind is ActionParameterKind.Step
            ? [.. Steps]
            : [.. definition.OptionChoices.Select(choice => choice with
            {
                Display = Strings.Get($"{definition.OwnerKey}.{definition.Name}.option.{choice.Value}",
                    choice.Display),
            })];
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
        else if (definition.Kind is ActionParameterKind.Number)
        {
            // A number that was written as an expression opens in the expression editor.
            UseFormula = true;
        }

        if (definition.Kind is ActionParameterKind.Steps or ActionParameterKind.Condition)
        {
            var isCondition = definition.Kind is ActionParameterKind.Condition;
            // Conditions are what this dialog is for. The steps of a block are put in order in the
            // editor's own list, where the block's shape can be seen, so this dialog does not show
            // them — but it does add one, because "this block needs another step in it" is a thing
            // to want while its settings are open.
            var conditions = isCondition || definition.ConditionsOnly;
            var addLabel = definition.AddLabelKey.Length > 0
                ? Strings.Get(definition.AddLabelKey)
                : Strings.Get("Add.AddStepToBlock");

            // A list that only takes one kind of step says so wherever a step is added to it, and
            // that includes the button in this dialog: a switch's branches are branches even when
            // they are added from here, not whatever action happened to be picked.
            var catalog = conditions
                ? ActionCatalog.Conditions
                : definition.ChildKeys.Count > 0
                    ? ActionCatalog.ForKeys(definition.ChildKeys)
                    : null;

            List = new StepListEditorViewModel(
                conditions
                    ? Strings.Get(isCondition ? "Add.NestedCondition" : "Add.NestedAddCondition")
                    : addLabel,
                isCondition,
                catalog);

            // The line under the list says how many steps are in it, so it has to follow the list.
            List.PropertyChanged += (_, _) => OnPropertyChanged(nameof(StepsNote));
        }
    }

    public ActionParameter Definition { get; }

    /// <summary>Nested editor, set when this parameter holds steps or a condition.</summary>
    public StepListEditorViewModel? List { get; }

    /// <summary>
    /// The places this step looks at, when it is one that searches: one row per rectangle, which
    /// the region picker fills in from a drag on the screen.
    /// </summary>
    public ObservableCollection<RegionRowViewModel> Regions { get; } = [];

    /// <summary>True when this parameter is a list of the places to search.</summary>
    public bool IsRegion => Definition.Kind is ActionParameterKind.Region;

    /// <summary>True while the step has nowhere in particular to look, which is the whole screen.</summary>
    public bool HasRegions => Regions.Count > 0;

    /// <summary>Puts a place to look at at the end of the list, for the user to fill in.</summary>
    public void AddRegion(RegionRowViewModel? row = null)
    {
        var added = row ?? new RegionRowViewModel();
        added.Take = RemoveRegion;
        Regions.Add(added);
        OnPropertyChanged(nameof(HasRegions));
    }

    /// <summary>Takes one place to look at out of the list.</summary>
    public void RemoveRegion(RegionRowViewModel row)
    {
        Regions.Remove(row);
        OnPropertyChanged(nameof(HasRegions));
    }

    /// <summary>
    /// Variables offered while editing a field that may name one, with what choosing one takes.
    /// </summary>
    public IReadOnlyList<VariableChoice> Variables { get; }

    /// <summary>The names on their own, which is what the expression reader and the checks use.</summary>
    private IReadOnlyList<string> Named { get; }

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

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JitterRange))]
    [NotifyPropertyChangedFor(nameof(HasJitterRange))]
    public partial decimal? NumberValue { get; set; }

    /// <summary>
    /// How far a length of time may move each run, as a percentage of it: 20 means the wait comes
    /// out somewhere between 80% and 120% of what is written. Empty means "use it as written".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JitterRange))]
    [NotifyPropertyChangedFor(nameof(HasJitterRange))]
    public partial decimal? JitterPercent { get; set; }

    /// <summary>
    /// The unit the box is showing a length of time in. Switching it keeps the moment the same
    /// and changes only how it reads: 1000 milliseconds becomes 1 second, and the step still
    /// stores 1000.
    /// </summary>
    public DurationUnit Unit
    {
        get => _unit;
        set
        {
            if (value is null || value == _unit)
            {
                return;
            }

            // The number on screen is written in the unit, so it moves across rather than changing
            // what it means. An empty box stays empty.
            var milliseconds = Milliseconds;
            _unit = value;
            OnPropertyChanged();
            if (NumberValue is not null)
            {
                NumberValue = milliseconds / value.Factor;
            }

            OnPropertyChanged(nameof(Minimum));
            OnPropertyChanged(nameof(Maximum));
            OnPropertyChanged(nameof(CurrentText));
            OnPropertyChanged(nameof(JitterRange));
            OnPropertyChanged(nameof(HasJitterRange));
        }
    }

    /// <summary>The value in milliseconds, whichever unit the box is showing it in.</summary>
    private decimal Milliseconds => (NumberValue ?? 0m) * _unit.Factor;

    /// <summary>
    /// Shows a length of time that is stored in milliseconds as milliseconds. The unit box starts
    /// on milliseconds whatever the number is, so every time field in a step reads the same way;
    /// writing "5 分" is something the user asks for by picking the unit, not something the dialog
    /// decides for them.
    /// </summary>
    private void ShowMilliseconds(decimal milliseconds)
    {
        Unit = DurationUnits[0];
        NumberValue = milliseconds;
    }

    /// <summary>
    /// True while a number is being written as an expression (<c>$match.x</c>, <c>$count + 1</c>)
    /// instead of being dialled in with the spinner. The engine reads both the same way.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNumberValue))]
    [NotifyPropertyChangedFor(nameof(IsNumberFormula))]
    [NotifyPropertyChangedFor(nameof(IsDurationEditor))]
    [NotifyPropertyChangedFor(nameof(IsDurationFormula))]
    [NotifyPropertyChangedFor(nameof(CanToggleFormula))]
    [NotifyPropertyChangedFor(nameof(FormulaTip))]
    [NotifyPropertyChangedFor(nameof(JitterRange))]
    [NotifyPropertyChangedFor(nameof(HasJitterRange))]
    public partial bool UseFormula { get; set; }

    [ObservableProperty]
    public partial bool Flag { get; set; }

    [ObservableProperty]
    public partial ActionParameterOption? Option { get; set; }

    /// <summary>What the expression field shows underneath: the result, or why it fails.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpressionError))]
    [NotifyPropertyChangedFor(nameof(ShowExpressionSuccess))]
    public partial string ExpressionMessage { get; set; } = string.Empty;

    /// <summary>True while <see cref="ExpressionMessage"/> describes a problem.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpressionError))]
    [NotifyPropertyChangedFor(nameof(ShowExpressionSuccess))]
    public partial bool HasExpressionError { get; set; }

    /// <summary>True while the message is a problem worth flagging in red.</summary>
    public bool ShowExpressionError => HasExpressionError && ExpressionMessage.Length > 0;

    /// <summary>True while the message is a result worth showing in green.</summary>
    public bool ShowExpressionSuccess => !HasExpressionError && ExpressionMessage.Length > 0;

    public bool IsText => Definition.Kind
        is ActionParameterKind.Text or ActionParameterKind.Key or ActionParameterKind.Keys;

    /// <summary>
    /// True when the parameter is written in a plain one-line field with nothing beside it. A
    /// selector is left out of that group because it carries the element picker's button, an
    /// element of a page carries the page picker's button, and a field the variables are offered
    /// in has a list of its own over it.
    /// </summary>
    public bool IsPlainText => IsText && !IsKeyField && !IsSelector && !IsBrowserTarget && !OffersVariables;

    /// <summary>
    /// True when this parameter names a key, which is written in a box the key names are offered
    /// in: a key has a name a person should not have to remember the spelling of, and the box
    /// stays editable so one still may be typed.
    /// </summary>
    public bool IsKey => Definition.Kind is ActionParameterKind.Key;

    /// <summary>
    /// True when the parameter is a combination of keys rather than one key. The keyboard drawn on
    /// screen answers the same question either way; what differs is what is done with the key it
    /// hands back, and the view asks this to know which.
    /// </summary>
    public bool IsKeys => Definition.Kind is ActionParameterKind.Keys;

    /// <summary>True when a key is written here, whether on its own or joined to others.</summary>
    public bool IsKeyField => IsKey || IsKeys;

    /// <summary>
    /// Adds a key to what is written, which is how a combination gets built: what is there stays
    /// and the new key is joined to it with a plus, the way the engine reads one. A field with
    /// nothing in it yet takes the key on its own, so a combination never starts with a plus.
    /// </summary>
    public void AddKey(string key)
    {
        var held = Text.Trim();
        Text = held.Length == 0 ? key : held + "+" + key;
    }

    /// <summary>The key names the picker offers, which is every name a macro may write.</summary>
    public IReadOnlyList<string> KeyChoices => KeyNames.Names;

    /// <summary>
    /// True when this choice is a control of the virtual controller, so the answer can be given by
    /// clicking the control on a pad drawn on screen as well as by picking it out of the list. The
    /// action and the parameter are both named because the other choices of those actions — how a
    /// button is held, how far a trigger is pulled — are not controls of anything.
    /// </summary>
    public bool IsGamepadPad => (Definition.OwnerKey, Definition.Name) is
        ("gamepad.button", "button") or ("gamepad.stick", "stick") or ("gamepad.trigger", "trigger");

    /// <summary>
    /// Takes one of the choices this parameter offers, which is how the pads write back what was
    /// clicked. A value this parameter has no choice for is left alone rather than stored.
    /// </summary>
    public void Choose(string value)
    {
        if (Choices.FirstOrDefault(choice => choice.Value == value) is { } picked)
        {
            Option = picked;
        }
    }

    /// <summary>
    /// True when the field is offered the variables it may name. A hint that says "written out or
    /// held in a variable" is only honest if the name can be picked instead of remembered, which
    /// is what this drives.
    /// </summary>
    public bool OffersVariables => IsText
        && !IsSelector
        && (Definition.AcceptsVariables || Definition.AcceptsFormula);

    /// <summary>
    /// True when the whole value is read as one, so the expression editor may build it: what it
    /// makes there is what the engine reads. Text that is only filled in stays out, because a
    /// formula written into it would be taken literally. It holds for a value written in a text
    /// box and for one picked from a variable list alike — an operand of a calculation is the
    /// second kind, and being able to build it is the difference between this and the editor.
    /// </summary>
    public bool OffersFormula => Definition.AcceptsFormula;

    /// <summary>
    /// True when this parameter is a UI Automation selector, which the element picker can take
    /// off the screen instead of it being written out by hand. The control a step measures from is
    /// one of these as well: it names a control the same way, so it is picked the same way.
    /// </summary>
    public bool IsSelector => Definition.Kind is ActionParameterKind.Text
                              && Definition.Name is "selector" or "anchorSelector";

    /// <summary>
    /// True when this parameter names an element inside a page a browser action drives. It is
    /// picked off the page itself — by pointing at it in the browser — rather than off the Windows
    /// desktop the way a UI Automation selector is, so it carries a button of its own.
    /// </summary>
    public bool IsBrowserTarget => Definition.Kind is ActionParameterKind.Text
                                   && Definition.Name is "target"
                                   && Definition.OwnerKey.StartsWith("browser.", StringComparison.Ordinal);

    /// <summary>True when this parameter is a picture the action looks for on screen.</summary>
    public bool IsImage => Definition.Kind is ActionParameterKind.Image;

    /// <summary>True when this parameter names a window, which the window picker can fill in.</summary>
    public bool IsWindow => Definition.Kind is ActionParameterKind.Window;

    /// <summary>
    /// Where pictures live while this dialog is open: beside the macro package when it has a
    /// path, and WhaleGenie's own folder otherwise. A relative picture value is looked up here.
    /// </summary>
    public string AssetFolder
    {
        get => _assetFolder;
        set
        {
            if (string.Equals(_assetFolder, value, StringComparison.Ordinal))
            {
                return;
            }

            _assetFolder = value;
            RefreshThumbnail();
        }
    }

    /// <summary>The picture this parameter names, shown underneath its field, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    public partial Bitmap? Thumbnail { get; set; }

    /// <summary>True when there is a picture to show.</summary>
    public bool HasThumbnail => Thumbnail is not null;

    /// <summary>Re-reads the picture the field names, so the preview always matches the text.</summary>
    public void RefreshThumbnail()
    {
        var previous = Thumbnail;
        Thumbnail = IsImage ? LoadThumbnail() : null;
        previous?.Dispose();
    }

    private Bitmap? LoadThumbnail()
    {
        if (ImageAssets.Resolve(Text, _assetFolder) is not { } path)
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch (Exception)
        {
            // A file that turns out not to be a picture is not worth a crash: the field
            // still shows the path, and the run reports it if the macro is used.
            return null;
        }
    }

    /// <summary>True when this parameter is a colour, edited with the screen picker.</summary>
    public bool IsColor => Definition.Kind is ActionParameterKind.Color;

    /// <summary>
    /// True when this parameter is one half of a screen position. Set by the dialog once it
    /// knows the action carries both an x and a y, so either field can take the pointer's place.
    /// </summary>
    public bool IsCoordinate { get; set; }

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
    /// True on the parameter whose line carries the button that tries this step's looking out.
    /// Set by the dialog on the field that says what the step is looking for — the picture, the
    /// colour, the area, the writing — and only for the actions that look at the screen.
    /// </summary>
    public bool IsLookAnchor { get; set; }

    /// <summary>
    /// True on the parameter whose line carries the button that picks a click point on the
    /// reference picture. Set by the dialog on the first half of the offset of an action that
    /// looks for a picture, which is the only place there is something to point at.
    /// </summary>
    public bool IsOffsetAnchor { get; set; }

    public bool IsMultiline => Definition.Kind is ActionParameterKind.MultilineText;

    public bool IsNumber => Definition.Kind is ActionParameterKind.Number;

    /// <summary>True when the number is dialled in with the spinner.</summary>
    public bool IsNumberValue => IsNumber && !UseFormula;

    /// <summary>True when the number is written as an expression.</summary>
    public bool IsNumberFormula => IsNumber && UseFormula;

    /// <summary>True when the number is a length of time rather than a count or a pixel.</summary>
    public bool IsDuration => IsNumber && Definition.IsDuration;

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
    /// Whether the field can go back to the spinner. An expression that reads a variable has no
    /// plain number to fall back on, so the switch stays off until the text is a number again.
    /// </summary>
    public bool CanToggleFormula => IsNumber && (!UseFormula || TextIsNumber);

    /// <summary>
    /// What the expression toggle offers right now, so the button never looks stuck or does
    /// something the field cannot do.
    /// </summary>
    public string FormulaTip => UseFormula
        ? Strings.Get(TextIsNumber ? "Add.FormulaBack" : "Add.FormulaLocked")
        : Strings.Get("Add.FormulaUse");

    /// <summary>True when what the field holds is a plain number rather than an expression.</summary>
    private bool TextIsNumber
        => decimal.TryParse((Text ?? string.Empty).Trim(), NumberStyles.Number,
            CultureInfo.InvariantCulture, out _);

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
    /// True when this field names a variable the step creates. Such a field is filled in with a
    /// name of its own rather than left as "output", so two steps of the same action do not write
    /// into each other.
    /// </summary>
    public bool IsOutputVariable => Definition.IsOutputVariable;

    /// <summary>True once the user has typed a name of their own into this field.</summary>
    public bool IsNameChanged { get; private set; }

    /// <summary>
    /// True when the name this field holds is already in use somewhere the macro can see, which is
    /// how two steps end up writing into one variable.
    /// </summary>
    public bool IsNameTaken => IsOutputVariable
        && CurrentText.Trim().Length > 0
        && Named.Contains(CurrentText.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>What the line under such a field says when its name is already taken.</summary>
    public string NameWarning => IsNameTaken
        ? Strings.Format("Add.NameTaken", CurrentText.Trim())
        : string.Empty;

    /// <summary>
    /// Writes the suggested name, unless the user has already chosen one. Called again whenever the
    /// note changes, so the name follows the note for as long as nobody has touched it.
    /// </summary>
    public void SuggestName(string name)
    {
        if (!IsOutputVariable || IsNameChanged || name.Length == 0 || Text == name)
        {
            return;
        }

        // Written down before the text moves: the change handler tells a name the dialog chose
        // from one the user typed by comparing the two.
        _suggested = name;
        Text = name;
    }

    /// <summary>
    /// Puts a chosen variable into the field at the caret. A field that is being built out of
    /// several pieces — a folder, a slash and a name — is why this is not simply an append.
    /// </summary>
    public void InsertVariable(string token, int caret)
    {
        var current = Text ?? string.Empty;
        var at = Math.Clamp(caret, 0, current.Length);
        Text = current[..at] + token + current[at..];
    }

    /// <summary>Gives the field a free name of its own, next to the one that was taken.</summary>
    public void MakeNameUnique()
    {
        var wanted = CurrentText.Trim();
        if (wanted.Length == 0)
        {
            return;
        }

        var free = VariableNames.Free(wanted, Named);
        if (free == wanted)
        {
            return;
        }

        IsNameChanged = true;
        _suggested = free;
        Text = free;
        OnPropertyChanged(nameof(IsNameTaken));
        OnPropertyChanged(nameof(NameWarning));
    }

    /// <summary>What the button beside the field should say.</summary>
    public string PathButton => Definition.PathIntent switch
    {
        PathIntent.Write => Strings.Get("Add.BrowseSave"),
        PathIntent.Folder => Strings.Get("Add.BrowseFolder"),
        _ => Strings.Get("Add.BrowseOpen"),
    };

    /// <summary>True when this parameter is edited with the nested step editor.</summary>
    public bool IsNested => List is not null;

    /// <summary>
    /// True when what the nested editor holds is a condition: either the condition of an if, a
    /// while or a wait, or the conditions a logic group combines. Conditions are picked here,
    /// because a condition is "what this step needs" rather than a step of the macro.
    /// </summary>
    public bool IsConditionList => List is not null
        && (Definition.Kind is ActionParameterKind.Condition || Definition.ConditionsOnly);

    /// <summary>
    /// True when this parameter holds the steps of a block. Those are edited in the editor's
    /// list, so the dialog only says so.
    /// </summary>
    public bool IsStepList => List is not null && !IsConditionList;

    /// <summary>Where the steps of a block went, which is what the dialog shows in their place.</summary>
    public string StepsNote => IsStepList
        ? Strings.Format("Add.StepsInList", Strings.Format("Common.StepCount", List?.Steps.Count ?? 0))
        : string.Empty;

    /// <summary>True when this parameter is edited as an expression.</summary>
    public bool IsExpression => Definition.Kind is ActionParameterKind.Expression;

    /// <summary>True when this parameter names a variable and offers suggestions.</summary>
    public bool IsVariable => Definition.Kind is ActionParameterKind.Variable;

    /// <summary>True when this parameter names another macro and offers the project's macros.</summary>
    public bool IsMacro => Definition.Kind is ActionParameterKind.Macro;

    /// <summary>True when the parameter belongs behind the dialog's "advanced" fold.</summary>
    public bool IsAdvanced => Definition.Advanced;

    /// <summary>
    /// True while the parameter still holds what it started with. A folded setting that has been
    /// changed is then worth putting back in front of the user when the step is opened again.
    /// </summary>
    public bool IsDefault => Definition.Kind switch
    {
        ActionParameterKind.Bool => Flag == string.Equals(Definition.DefaultValue, "true",
            StringComparison.OrdinalIgnoreCase),
        ActionParameterKind.Choice => string.Equals(Option?.Value ?? string.Empty,
            Definition.DefaultValue, StringComparison.Ordinal),
        ActionParameterKind.Steps or ActionParameterKind.Condition => List?.Steps.Count is null or 0,
        _ => string.Equals(CurrentText.Trim(), Definition.DefaultValue, StringComparison.Ordinal),
    };

    /// <summary>
    /// Choices offered by a <see cref="ActionParameterKind.Choice"/> editor, and the steps offered
    /// by a <see cref="ActionParameterKind.Step"/> one. It is a list rather than a fixed set because
    /// a step name that is not in the macro any more still has to be shown.
    /// </summary>
    public List<ActionParameterOption> Choices { get; }

    /// <summary>The bottom of the number box, in whichever unit the box is showing.</summary>
    public decimal Minimum => Definition.Minimum / _unit.Factor;

    /// <summary>The top of the number box, in whichever unit the box is showing.</summary>
    public decimal Maximum => Definition.Maximum / _unit.Factor;

    public decimal Increment => Definition.Increment;

    public string Label => Definition.Required
        ? Definition.LocalLabel
        : Strings.Format("Common.OptionalSuffix", Definition.LocalLabel);

    public string Hint => Definition.LocalHint;

    public bool HasHint => !string.IsNullOrWhiteSpace(Definition.Hint);

    public string Placeholder => Definition.Placeholder;

    /// <summary>What the expression editor of a number field hints at.</summary>
    public string FormulaPlaceholder => Strings.Get("Add.FormulaPlaceholder");

    /// <summary>True when a required parameter is still empty, which blocks saving.</summary>
    public bool IsMissing => IsEnabled && Definition.Required && Definition.Kind switch
    {
        ActionParameterKind.Bool => false,
        ActionParameterKind.Steps or ActionParameterKind.Condition => !HasNestedSteps,
        _ => string.IsNullOrWhiteSpace(CurrentText),
    };

    /// <summary>Optional parameters left blank are dropped from the saved node.</summary>
    /// <remarks>
    /// A list of places to look at is there when it holds a place: an empty one means the whole
    /// screen, which is what the step does when the list is not written down at all.
    /// </remarks>
    public bool IsIncluded => IsEnabled && Definition.Kind switch
    {
        ActionParameterKind.Steps or ActionParameterKind.Condition => HasNestedSteps,
        ActionParameterKind.Region => Regions.Count > 0,
        _ => Definition.Required || !string.IsNullOrWhiteSpace(CurrentText),
    };

    /// <summary>
    /// Recomputes whether this parameter applies, from the item count of the sibling
    /// list it waits on. Logic needs at least two conditions to combine.
    /// </summary>
    public void UpdateGate(int siblingItemCount)
    {
        if (string.IsNullOrEmpty(Definition.EnabledBySibling))
        {
            return;
        }

        var enabled = siblingItemCount >= 2;
        if (enabled == IsEnabled)
        {
            return;
        }

        IsEnabled = enabled;
        OnPropertyChanged(nameof(IsEnabled));
    }

    /// <summary>True when the nested editor holds at least one step.</summary>
    public bool HasNestedSteps => List?.Steps.Count > 0;

    /// <summary>Re-checks an expression as it is typed, so the editor can show the outcome.</summary>
    partial void OnTextChanged(string value)
    {
        // A name the dialog did not suggest is one the user chose, and it stops following the note
        // from then on: a name that kept moving while it was being written would be no name at all.
        if (IsOutputVariable && !string.Equals(value, _suggested, StringComparison.Ordinal))
        {
            IsNameChanged = true;
        }

        if (IsOutputVariable)
        {
            OnPropertyChanged(nameof(IsNameTaken));
            OnPropertyChanged(nameof(NameWarning));
        }

        if (IsImage)
        {
            RefreshThumbnail();
        }

        if (IsNumberFormula)
        {
            OnPropertyChanged(nameof(CanToggleFormula));
            OnPropertyChanged(nameof(FormulaTip));
        }

        if (IsExpression || IsNumberFormula)
        {
            UpdateExpression(value);
        }
    }

    /// <summary>Switches a number field between the spinner and an expression.</summary>
    public void ToggleFormula()
    {
        if (!CanToggleFormula)
        {
            return;
        }

        UseFormula = !UseFormula;
    }

    /// <summary>
    /// Puts a plain number in the field, dropping an expression that was there. This is what the
    /// screen picker writes when it takes the pointer's place.
    /// </summary>
    public void SetNumber(int value)
    {
        UseFormula = false;
        NumberValue = value;
    }

    partial void OnUseFormulaChanged(bool value)
    {
        if (value)
        {
            // Start from the number that was showing, so the field is never left blank. The engine
            // reads an expression in milliseconds, so a time is written down in the stored unit
            // rather than the one the box happened to be showing.
            Text = Milliseconds.ToString(CultureInfo.InvariantCulture);
            return;
        }

        if (decimal.TryParse((Text ?? string.Empty).Trim(), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var number))
        {
            ShowMilliseconds(number);
        }
    }

    private void UpdateExpression(string? source)
    {
        ExpressionMessage = string.Empty;
        HasExpressionError = false;

        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        if (!Expression.TryEvaluate(source, new KnownVariables(Named), out var value, out var error))
        {
            HasExpressionError = true;
            ExpressionMessage = ExpressionText.Describe(error!);
            return;
        }

        // Without variables the answer is exact, so it is worth showing. With variables
        // there is nothing to run yet, so the field only confirms that the text reads.
        ExpressionMessage = source.Contains('$')
            ? Strings.Get("Add.ExpressionValid")
            : Strings.Format("Add.ExpressionResult", value.AsText());
    }

    /// <summary>The value as text, taken from whichever editor this parameter uses.</summary>
    public string CurrentText => Definition.Kind switch
    {
        ActionParameterKind.Number => UseFormula
            ? Text ?? string.Empty
            : IsDuration
                ? Milliseconds.ToString("0.####", CultureInfo.InvariantCulture)
                : (NumberValue ?? 0m).ToString(CultureInfo.InvariantCulture),
        ActionParameterKind.Bool => Flag ? "true" : "false",
        ActionParameterKind.Choice or ActionParameterKind.Step => Option?.Value ?? string.Empty,
        _ => Text ?? string.Empty,
    };

    /// <summary>Flattens the edited value into the model the step is built from.</summary>
    public StepParameter ToStepParameter() => new()
    {
        Name = Definition.Name,
        Kind = Definition.Kind,
        Value = CurrentText.Trim(),
        // A blank field carries nothing to move, so a give typed beside nothing is dropped with it.
        Jitter = IsIncluded && JitterPercent is > 0m
            ? Math.Min(100m, JitterPercent.Value) / 100m
            : 0m,
        Steps = Definition.Kind is ActionParameterKind.Steps
            ? List?.Steps.ToList() ?? []
            : [],
        Rows = Definition.Kind is ActionParameterKind.Region
            ? [.. Regions.Select(row => row.ToRow())]
            : [],
        Condition = Definition.Kind is ActionParameterKind.Condition
            ? List?.Steps.FirstOrDefault()
            : null,
    };

    /// <summary>Loads a value that was stored on an existing step.</summary>
    public void ApplyValue(StepParameter stored)
    {
        if (Definition.Kind is ActionParameterKind.Steps)
        {
            List?.Load(stored.Steps);
            return;
        }

        if (Definition.Kind is ActionParameterKind.Region)
        {
            Regions.Clear();
            foreach (var row in stored.Rows)
            {
                AddRegion(RegionRowViewModel.From(row));
            }

            return;
        }

        if (Definition.Kind is ActionParameterKind.Condition)
        {
            if (stored.Condition is not null)
            {
                List?.Load([stored.Condition]);
            }

            return;
        }

        JitterPercent = stored.HasJitter ? stored.Jitter * 100m : null;

        var raw = stored.Value;
        switch (Definition.Kind)
        {
            case ActionParameterKind.Number:
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    UseFormula = false;
                    ShowMilliseconds(number);
                }
                else
                {
                    // Anything that is not a plain number is an expression the engine can still
                    // evaluate, so the text has to survive opening and saving the step.
                    UseFormula = true;
                    Text = raw;
                }
                break;
            case ActionParameterKind.Bool:
                Flag = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case ActionParameterKind.Choice:
                Option = Choices.FirstOrDefault(choice => choice.Value == raw) ?? Choices.FirstOrDefault();
                break;
            case ActionParameterKind.Step:
                {
                    // A name that is not one of the macro's steps — pointing at a step that has
                    // since been deleted, or written by hand into the file — is kept and shown as
                    // itself, rather than being quietly swapped for whichever step is first.
                    var step = Choices.FirstOrDefault(choice => choice.Value == raw);
                    if (step is null && raw.Length > 0)
                    {
                        step = new ActionParameterOption(raw, Strings.Format("Add.StepNotInMacro", raw));
                        Choices.Add(step);
                    }

                    Option = step;
                }

                break;
            default:
                Text = raw;
                break;
        }
    }

    private static ActionParameterOption? PickInitialOption(
        IReadOnlyList<ActionParameterOption> choices, string defaultValue)
    {
        if (choices.Count == 0)
        {
            return null;
        }

        return choices.FirstOrDefault(choice => choice.Value == defaultValue) ?? choices[0];
    }
}
