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

// The condition editor a logic field opens, and the window preview beside it.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>
    /// Puts a chosen variable into the field at the caret. A field that is being built out of
    /// several pieces — a folder, a slash and a name — is why this is not simply an append.
    /// </summary>
    /// <remarks>
    /// A field that names a variable takes the name itself: the engine writes that text down as the
    /// name, so the <c>$name</c> the wall hands back would make a variable called <c>$count</c>,
    /// which nothing can read back and which the variable list shows beside the real ones. Every
    /// other field takes the <c>$name</c> form, which is what the reader understands.
    /// </remarks>
    public void InsertVariable(string token, int caret)
    {
        var text = Definition.NamesVariable && token.StartsWith('$') ? token[1..] : token;
        var current = Text ?? string.Empty;
        var at = Math.Clamp(caret, 0, current.Length);
        Text = current[..at] + text + current[at..];
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

    /// <summary>True when this parameter is edited as an expression.</summary>
    public bool IsExpression => Definition.Kind is ActionParameterKind.Expression;

    /// <summary>True when this parameter is a block's condition, edited as rows and as a tree.</summary>
    public bool IsCondition => Definition.Kind is ActionParameterKind.Condition;

    /// <summary>
    /// The condition of a block, seen two ways — a table of comparisons and a tree of them — over
    /// one model. It writes the same expression the engine reads, so the macro file is unchanged
    /// and a condition written by hand still opens.
    /// </summary>
    public ConditionEditorViewModel? ConditionEditor { get; private set; }

    /// <summary>True while the condition is writing the value rather than reading it.</summary>
    private bool _writingCondition;

    /// <summary>Takes what the condition editor says as the value of this parameter.</summary>
    private void WriteCondition()
    {
        if (ConditionEditor is null)
        {
            return;
        }

        _writingCondition = true;
        try
        {
            Text = ConditionEditor.Text;
        }
        finally
        {
            _writingCondition = false;
        }

        // A half-filled row writes nothing, so the text is the same as it was a keystroke ago and
        // setting it says nothing to whoever is listening. What did change is that somebody has
        // started answering the question, and that is what the dialog's own line is about: a
        // condition nobody has touched is not a mistake, one an answer has begun is unfinished.
        OnPropertyChanged(nameof(Text));
    }

    /// <summary>True when this parameter names a variable and offers suggestions.</summary>
    public bool IsVariable => Definition.Kind is ActionParameterKind.Variable;

    /// <summary>
    /// The windows this step would recognise right now, when this is the field the dialog hangs the
    /// preview on. Null on every other field, so the block is drawn only under the rule it belongs to.
    /// </summary>
    public WindowPreviewViewModel? WindowPreview
    {
        get => _windowPreview;
        set
        {
            if (ReferenceEquals(_windowPreview, value))
            {
                return;
            }

            _windowPreview = value;
            OnPropertyChanged(nameof(WindowPreview));
            OnPropertyChanged(nameof(ShowsWindowPreview));
        }
    }

    /// <summary>True when the preview of the windows this step recognises belongs under this field.</summary>
    public bool ShowsWindowPreview => _windowPreview is not null;

    /// <summary>True when this parameter names another macro and offers the project's macros.</summary>
    public bool IsMacro => Definition.Kind is ActionParameterKind.Macro;

    /// <summary>True when the parameter belongs behind the dialog's "advanced" fold.</summary>
    /// <summary>
    /// The block of the form this field answers, empty when it answers no block of its own.
    /// </summary>
    public string Group => Definition.Group;

    public bool IsAdvanced => Definition.Advanced;

    /// <summary>
    /// True when this value is written by a tool rather than typed, so the form has no line for it:
    /// the colours around a base colour are picked off the picture.
    /// </summary>
    public bool WrittenByTool => Definition.WrittenByTool;

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
        ActionParameterKind.Steps => List?.Steps.Count is null or 0,
        // A list of them is at its default when it is empty: what such a field holds is not in its
        // text, so reading the text alone would call a filled-in list of pictures untouched.
        ActionParameterKind.Region => Regions.Count == 0,
        ActionParameterKind.ColourPoints => ColourPointRows.Count == 0,
        ActionParameterKind.KeySequence => !HasKeyPress,
        ActionParameterKind.Images => Pictures.Count == 0,
        // What a page step adds to a selector is at its default while it adds nothing, which is
        // what every step written before that field existed does.
        ActionParameterKind.Narrow => NarrowRows.Count == 0,
        // The results are at their default while exactly the ones the action nearly always wants
        // are ticked: what they are called is not a change worth a mark on the tab.
        ActionParameterKind.Returns => ReturnsRows.All(row => row.Ticked == row.IsPrimary),
        // What a field opened with is the example of the language it opened in, so a step that has
        // not been touched since is at its default whichever language that was.
        _ => string.Equals(CurrentText.Trim(), Definition.LocalDefault.Trim(),
            StringComparison.Ordinal),
    };
}
