using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// What the form says before saving: the preview, the problem, and the window and clipboard shown beside it.
public partial class AddActionViewModel
{
    public string Description => SelectedDefinition?.LocalDescription
        ?? Strings.Get("Add.SelectHint");



    /// <summary>
    /// Formatting for the preview only. The relaxed encoder keeps Chinese text and the
    /// operators an expression uses (<c>+ ( ) &gt; &amp;</c>) readable instead of writing
    /// them as <c>\uXXXX</c> escapes. The saved file keeps the default encoder.
    /// </summary>
    private static readonly JsonSerializerOptions PreviewFormat = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Live preview of the JSON node that will be appended to the macro tree.</summary>
    public string JsonPreview => SelectedDefinition is null
        ? Strings.Get("Add.JsonPlaceholder")
        : BuildStep().ToJson().ToJsonString(PreviewFormat);

    /// <summary>Explains why the step cannot be saved yet, or is empty when it can.</summary>
    public string ValidationMessage
    {
        get
        {
            if (SelectedDefinition is null)
            {
                return Strings.Get("Add.SelectFirst");
            }

            if (ScopeError() is { } error)
            {
                return error;
            }

            if (GlobalVariableProblem is { Length: > 0 } globalProblem)
            {
                return globalProblem;
            }

            if (AwaitsAnUntouchedCondition)
            {
                return Strings.Get("Add.NeedCondition");
            }

            return MissingParameters() is { Count: > 0 } missing
                ? Strings.Format("Add.StillNeeded", string.Join(", ", missing))
                : string.Empty;
        }
    }

    /// <summary>
    /// True when <see cref="ValidationMessage"/> is a problem to fix rather than the empty dialog's
    /// "pick an action first". Only a problem is worth shouting about: a line written in red as
    /// soon as the dialog opens would report a mistake nobody has had the chance to make. A
    /// condition with nothing written in it is the same case: the page already shows the row with a
    /// word in each box, so the line says what is still to do in the quiet colour.
    /// </summary>
    public bool ValidationIsProblem => SelectedDefinition is not null
        && ValidationMessage.Length > 0
        && !AwaitsAnUntouchedCondition;

    /// <summary>
    /// True when the one thing missing is a condition nobody has begun. Filled in half is a
    /// different matter and stays red: there the row itself claims to say something.
    /// </summary>
    private bool AwaitsAnUntouchedCondition
        => MissingParameters().Count == 1
            && Parameters.Any(parameter => parameter.IsMissing
                && parameter.Definition.Kind is ActionParameterKind.Condition
                && parameter.ConditionEditor is { HasInput: false });

    public bool CanSave => SelectedDefinition is not null
        && ScopeError() is null
        && GlobalVariableProblem.Length == 0
        && MissingParameters().Count == 0;

    /// <summary>True when this step can be run on its own from the dialog.</summary>
    public bool CanRun => ShowsRun && CanSave;

    /// <summary>True when what is being edited is something a run can do at all.</summary>
    public bool ShowsRun => SelectedDefinition is not null;

    /// <summary>
    /// True while the step is out on the machine. The button says so and stops taking clicks: a
    /// second press while the first is still going would send everything twice.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunLabel))]
    public partial bool IsRunning { get; set; }

    /// <summary>What the run button says, which is that it is busy while the step is out.</summary>
    public string RunLabel => Strings.Get(IsRunning ? "Add.Running" : "Add.RunStep");

    /// <summary>
    /// What just happened: what a run did, or why a step could not go where it was being put. A step
    /// that quietly does nothing looks the same as one that was never run, so it is said under the
    /// form rather than left for the user to work out.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    [NotifyPropertyChangedFor(nameof(ShowsNotice))]
    [NotifyPropertyChangedFor(nameof(ShowsWarning))]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>True when the notice is a problem rather than a note about what just happened.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNotice))]
    [NotifyPropertyChangedFor(nameof(ShowsWarning))]
    public partial bool NoticeIsWarning { get; set; }

    /// <summary>True while there is something to say.</summary>
    public bool HasNotice => Notice.Length > 0;

    /// <summary>True when the line under the form carries a note.</summary>
    public bool ShowsNotice => HasNotice && !NoticeIsWarning;

    /// <summary>True when it carries a problem.</summary>
    public bool ShowsWarning => HasNotice && NoticeIsWarning;

    /// <summary>Says what just happened, in the line under the form.</summary>
    public void Say(string message, bool warning = false)
    {
        NoticeIsWarning = warning;
        Notice = message;
    }

    partial void OnSelectedDefinitionChanged(ActionDefinition? value)
    {
        // A step that waits for something starts out with the length the action suggests, so the
        // wait has a length at all without anyone having to go looking for the setting; picking
        // another action is what puts it there, and editing a step overwrites it with what the
        // step itself says.
        MetaTimeout.Load(value?.StepTimeoutMs ?? 0);
        BuildParameters(value);
        SyncGlobalVariable();
        // Choosing an action is what turns "pick one first" into a real answer, so whether the line
        // is a problem changes here as well as when a parameter is filled in.
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(ValidationIsProblem));
    }

    /// <summary>
    /// Chooses an action by its catalogue key. A palette shortcut opens this dialog on the
    /// action it stands for, so the user only has to fill the blanks in.
    /// </summary>
    public void SelectAction(string key)
    {
        if (AvailableActions.FirstOrDefault(definition => definition.Key == key) is not { } definition)
        {
            return;
        }

        SelectedDefinition = definition;

    }

    /// <summary>
    /// Which part of a window one of the step's window fields is held against, which is what the
    /// window picker has to take away from that field: a class picked while the step compares
    /// titles would match nothing.
    /// </summary>
    public WindowMatch WindowMatchFor(StepParameterViewModel text)
        => RuleOf(text).Reading.Match;

    /// <summary>
    /// Writes the title a picker read into the field that holds a window rule's text, and puts the
    /// way that text is compared back to "the title holds it". A picker reads titles, so a rule left
    /// on the process name, the class or a pattern would be holding a title up against something
    /// else and would recognise no window at all. A field that already says which window this is
    /// keeps what it says: nothing was picked into it.
    /// </summary>
    public bool FillWindowFromPick(string field, string title)
    {
        if (Parameters.FirstOrDefault(item => item.Definition.Name == field) is not { } text
            || (text.Text ?? string.Empty).Trim().Length > 0)
        {
            return false;
        }

        text.ApplyWindowRule(new WindowRule(Title: title));

        return true;
    }

    /// <summary>
    /// The windows this step would recognise right now, which is shown under the field that
    /// completes the rule. Null while the action recognises no window by text of its own.
    /// </summary>
    public WindowPreviewViewModel? WindowPreview { get; private set; }

    /// <summary>The field the preview stands under, and what has to be true of the step for the rule
    /// it follows to be read at all.</summary>
    private StepParameterViewModel? _previewField;
    private PreviewGate _previewGate;

    /// <summary>
    /// What the window a step names is named for. Some steps name one to count their numbers from,
    /// some to post their input at; either way the rule is only read while that is what the step is
    /// set up to do, and a list of windows standing under a rule nothing reads says the step does
    /// something it does not.
    /// </summary>
    private enum PreviewGate
    {
        /// <summary>The rule is the step's own, read whenever the step runs.</summary>
        Always,

        /// <summary>Read only while the numbers are counted from a window.</summary>
        AnchorOrigin,

        /// <summary>Read only while the input is posted at a window.</summary>
        BackgroundPost,
    }

    /// <summary>
    /// Hangs the preview on the field the rule is about — the text the window is recognised by — so
    /// the list stands right under the box being written rather than between a field and its own
    /// editor. An action with no such rule has nothing to preview. A step that looks at the screen
    /// names its window beside its coordinate origin, and one that posts its input names it beside
    /// the input mode; both get the same list, and both are only read while that is what the step
    /// does, so the list is only there then.
    /// </summary>
    private void WireWindowPreview()
    {
        WindowPreview?.Stop();
        WindowPreview = null;
        _previewField = null;
        _previewGate = PreviewGate.Always;

        var text = Parameter("window");
        if (text is not null)
        {
            _previewGate = Parameter("inputMode") is not null
                ? PreviewGate.BackgroundPost
                : Parameter("anchorMode") is not null
                    ? PreviewGate.AnchorOrigin
                    : PreviewGate.Always;
        }

        if (text is null)
        {
            // No rule of its own, so there is no list and no frames either.
            OnPropertyChanged(nameof(WindowPreview));
            return;
        }

        _previewField = text;
        WindowPreview = new WindowPreviewViewModel();
        WindowPreview.Watch(() => Windows);
        text.WindowPreview = WindowPreview;

        // The list is a different one for every action, so whoever is drawing the frames around it
        // has to be told to look at this one instead of the one that just went away.
        OnPropertyChanged(nameof(WindowPreview));
    }

    /// <summary>
    /// True when this action's form shows what the clipboard is holding right now. The catalogue
    /// says which actions those are: a step that takes the picture the clipboard holds has nothing
    /// else to ask, so that picture is what the form puts in front of the reader.
    /// </summary>
    public bool ShowsClipboardPreview => SelectedDefinition?.Preview is ActionPreview.ClipboardImage;

    /// <summary>The picture on the clipboard as it stands, or null when it holds none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClipboardPreview))]
    public partial Bitmap? ClipboardPreview { get; set; }

    /// <summary>True when there is a picture to show.</summary>
    public bool HasClipboardPreview => ClipboardPreview is not null;

    /// <summary>
    /// One line under the title: how big the picture is, or why there is none to show. It stays
    /// visible when the picture does not, so "the clipboard holds nothing" is said rather than left
    /// to an empty space the reader has to interpret.
    /// </summary>
    [ObservableProperty]
    public partial string ClipboardPreviewNote { get; set; } = string.Empty;

    /// <summary>
    /// Reads the clipboard once: the picture, its size, or the reason there is neither. It is read
    /// when an action is chosen and when the button asks again, never on a keystroke — another
    /// program holding the clipboard makes the read wait up to a fifth of a second, which on the
    /// way to every letter typed would read as the form hanging.
    /// </summary>
    private void RefreshClipboardPreview()
    {
        var previous = ClipboardPreview;
        ClipboardPreview = null;
        var note = string.Empty;

        if (ShowsClipboardPreview && _clipboard is not null)
        {
            try
            {
                if (_clipboard.ReadImage() is { IsEmpty: false } picture)
                {
                    ClipboardPreview = ImageAssets.ToBitmap(picture);
                    note = Strings.Format("Add.ClipboardPreviewSize", picture.Width, picture.Height);
                }
                else
                {
                    note = Strings.Get("Add.ClipboardPreviewEmpty");
                }
            }
            catch (DeviceActionException)
            {
                note = Strings.Get("Add.ClipboardPreviewBusy");
            }
            catch (Exception)
            {
                // A preview is worth a look, not a crash: a machine with no clipboard device still
                // has to open the form.
                note = Strings.Get("Add.ClipboardPreviewEmpty");
            }
        }

        ClipboardPreviewNote = note;
        previous?.Dispose();
    }

    /// <summary>Asks the clipboard again, which the button beside the preview does.</summary>
    [RelayCommand]
    private void RefreshClipboard() => RefreshClipboardPreview();

    /// <summary>
    /// True when this step counts its numbers from a window, which is the only time the window it
    /// names is read: the screen and element origins do not use it.
    /// </summary>
    private bool AnchorNamesWindow()
        => Parameter("anchorMode")?.CurrentText.Trim().ToLowerInvariant() is "window";

    /// <summary>
    /// True when this step posts its input at a window, which is the only time the window it names
    /// is read: the other input modes send to whatever has the focus.
    /// </summary>
    private bool InputPostsToWindow()
        => Parameter("inputMode")?.CurrentText.Trim().ToLowerInvariant() == "background";

    /// <summary>
    /// True for an action that holds steps of its own: it is a block in the macro rather than
    /// something the run just does.
    /// </summary>
    private static bool HoldsSteps(ActionDefinition action)
        => action.Parameters.Any(parameter => parameter.Kind is ActionParameterKind.Steps);
}
