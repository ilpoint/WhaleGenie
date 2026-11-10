using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// The settings around a step, what they read as in one line, and how the step is named.
public partial class MacroStep : INotifyPropertyChanged
{
    /// <summary>
    /// Settings that apply to the step itself whatever its action is: a note, whether it
    /// is skipped, its timeout, its retries and the pauses around it.
    /// </summary>
    public StepMeta Meta
    {
        get => _meta;
        set
        {
            _meta = value;
            Raise(nameof(Meta));
            Raise(nameof(Comment));
            Raise(nameof(HasComment));
            Raise(nameof(IsSkipped));
            Raise(nameof(SkipLabel));
            Raise(nameof(RowOpacity));
            Raise(nameof(MetaSummary));
            Raise(nameof(HasMetaSummary));
        }
    }

    /// <summary>
    /// The step settings worth seeing without opening the dialog: the retries it gets, the timeout
    /// it is held to, the pauses around it, and what a failure does instead of stopping the macro.
    /// Empty for an ordinary step, so the list stays quiet.
    /// </summary>
    public string MetaSummary
    {
        get
        {
            var meta = Meta;
            var parts = new List<string>();

            if (meta.RetryCount > 0)
            {
                parts.Add(Strings.Format(meta.RetryBackoff switch
                {
                    RetryBackoff.Doubling => "Editor.Meta.RetryDoubling",
                    RetryBackoff.Jitter => "Editor.Meta.RetryJitter",
                    _ => "Editor.Meta.Retry",
                }, meta.RetryCount));
            }

            if (meta.TimeoutMs > 0)
            {
                parts.Add(Strings.Format("Editor.Meta.Timeout", DurationUnit.Written(meta.TimeoutMs)));
            }

            if (meta.DelayBeforeMs > 0 || meta.DelayAfterMs > 0)
            {
                parts.Add(Strings.Format("Editor.Meta.Pauses",
                    DurationUnit.Written(meta.DelayBeforeMs),
                    DurationUnit.Written(meta.DelayAfterMs)));
            }

            switch (meta.OnError)
            {
                case StepErrorAction.Continue:
                    parts.Add(Strings.Get("Editor.Meta.OnErrorContinue"));
                    break;
                case StepErrorAction.NextIteration:
                    parts.Add(Strings.Get("Editor.Meta.OnErrorNextIteration"));
                    break;
                case StepErrorAction.AskUser:
                    parts.Add(Strings.Get("Editor.Meta.OnErrorAsk"));
                    break;
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>True when there is something about this step worth showing.</summary>
    public bool HasMetaSummary => MetaSummary.Length > 0;

    /// <summary>Note the user wrote about this step, shown under its name.</summary>
    public string Comment => Meta.Comment;

    public bool HasComment => Meta.Comment.Length > 0;

    /// <summary>True when the macro skips this step.</summary>
    public bool IsSkipped => !Meta.IsEnabled;

    /// <summary>Marker drawn on a step the macro will skip.</summary>
    public string SkipLabel => Strings.Get("Editor.StepDisabled");

    /// <summary>Name of the field that says where a step's input is sent.</summary>
    private const string InputModeParameter = "inputMode";

    /// <summary>
    /// 这一步的输入往哪儿送：前台 / 后台消息 / 驱动级。Empty for a step that sends no input at
    /// all, and for an action that has no such field.
    /// </summary>
    /// <remarks>
    /// Whether the input goes in front, is posted at a window, or goes through the virtual device is
    /// what the user pays for — a window that has to be in front, a window that has to be named
    /// rightly, or a driver installed and a server running — and it is the first thing to check when
    /// a step that works by hand does nothing in a macro. Reading it off the row beats opening the
    /// step. A step that never wrote the field is in front, because that is what the catalogue
    /// starts the field at.
    /// </remarks>
    public string InputMark
    {
        get
        {
            var parameter = Parameters.FirstOrDefault(item => item.Name == InputModeParameter);
            if (parameter is null)
            {
                var declared = Definition?.Parameters
                    .Any(item => item.Name == InputModeParameter) ?? false;
                return declared ? Strings.Get("Editor.InputForeground") : string.Empty;
            }

            return parameter.Value switch
            {
                "foreground" => Strings.Get("Editor.InputForeground"),
                "background" => Strings.Get("Editor.InputBackground"),
                "driver" => Strings.Get("Editor.InputDriver"),
                _ => string.Empty,
            };
        }
    }

    /// <summary>A skipped step fades so it is easy to tell apart while reading the list.</summary>
    public double RowOpacity => Meta.IsEnabled ? 1 : 0.45;

    public ActionDefinition? Definition => ActionCatalog.Find(Type);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Human readable name, falling back to the raw key when it is unknown.</summary>
    public string DisplayName => Definition?.LocalName ?? Type;

    /// <summary>
    /// This step named the way anything that has to point at one names it: the note the user wrote,
    /// the action it is, and the short name it goes by. The note comes first because that is the
    /// one part that says what the step is for; the name is last because that is what a condition
    /// stores. Whichever parts are empty are left out rather than leaving a gap.
    /// </summary>
    public string PickerLabel => string.Join(" · ",
        new[] { Comment, DisplayName, Id }.Where(part => part.Length > 0));
}
