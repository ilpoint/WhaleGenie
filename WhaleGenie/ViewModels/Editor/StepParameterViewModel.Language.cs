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

// Re-reading the labels this field shows when the language changes.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>
    /// Re-reads the field's name, hint and choice labels. The list is a collection so the box
    /// watching it rebuilds its items when the words change; the chosen value stays chosen.
    /// </summary>
    public override void RefreshLanguage()
    {
        base.RefreshLanguage();

        // 窗口那一行字是用文案表拼出来的（"前台窗口"、"沿用上一次"、匹配方式的叫法），换语言时
        // 要跟着重拼，否则它会停在打开表单时的那种语言上。
        OnPropertyChanged(nameof(WindowRuleSummary));

        foreach (var row in KeyRows)
        {
            row.RefreshLanguage();
        }

        if (Definition.Kind is ActionParameterKind.Step)
        {
            // The entries are names of steps in this macro, not catalogue wording.
            return;
        }

        var selected = Option?.Value;
        var labels = Definition.OptionChoices
            .Select(choice => choice with
            {
                Display = Strings.Get($"{Definition.OwnerKey}.{Definition.Name}.option.{choice.Value}",
                    choice.Display),
            })
            .ToArray();

        Choices.Clear();
        foreach (var choice in labels)
        {
            Choices.Add(choice);
        }

        if (selected is not null)
        {
            Option = Choices.FirstOrDefault(choice => choice.Value == selected) ?? Option;
        }
    }

    /// <summary>The bottom of the number box, in whichever unit the box is showing.</summary>
    public decimal Minimum => Definition.Minimum / _unit.Factor;

    /// <summary>The top of the number box, in whichever unit the box is showing.</summary>
    public decimal Maximum => Definition.Maximum / _unit.Factor;

    public decimal Increment => Definition.Increment;

    /// <summary>The name this field goes by right now, which a field beside it can change.</summary>
    public string Label => Name(Definition.LocalLabel);

    /// <summary>
    /// What the field says under itself: first what a field of this kind takes and what an empty
    /// box means, then the catalogue's own words about this particular field. Both were once
    /// things to remember — a box read as one value and a box read as text look exactly alike, and
    /// so do a number that runs to the edge when left out and one that says nothing. Asking the
    /// definition is what keeps the sentence and the field in step.
    /// </summary>
    public string Hint => string.Join(" ", Hints());

    /// <summary>True when this field has anything to say about itself.</summary>
    public bool HasHint => Hint.Length > 0;


    private IEnumerable<string> Hints()
    {
        // 「可以写算式」「按文本读取」这两句不在这里说。一页上十来个字段，每个都先把同一句话
        // 复述一遍，真正属于这一格的话就被埋在底下，读的人会往下跳过而不是往下读。它们改由
        // 标签左边的 ƒ 与「名字」两个小标记承担，各自的含义在悬停里；这一行只留这一格自己的事。

        // A search region is a list of places rather than four numbers, but it is the same question
        // asked the same way: a place left out reaches the edge, so it says so the same way.
        if (Definition.Kind is ActionParameterKind.Region)
        {
            yield return Strings.Get("Add.EmptyReachesTheEdge");
        }
        else if (Definition.Kind is ActionParameterKind.Number)
        {
            var key = Definition.EmptyMeans switch
            {
                ActionEmptyMeans.ToEdge => "Add.EmptyReachesTheEdge",
                ActionEmptyMeans.Keep => "Add.EmptyKeeps",
                ActionEmptyMeans.ScreenRefresh => "Add.EmptyScreenRefresh",
                ActionEmptyMeans.Unset => "Add.EmptyUsesDefault",
                _ => string.Empty,
            };

            if (key.Length > 0)
            {
                yield return Strings.Get(key);
            }
        }

        if (Definition.LocalHint.Length > 0)
        {
            yield return Definition.LocalHint;
        }
    }

    public string Placeholder => Definition.Placeholder;

    /// <summary>
    /// True when the block this field stands in already says what the field is called, so the label
    /// above the editor would say it a second time. Set by the form, which is the only thing that
    /// knows what the block above the line is called.
    /// </summary>
    public bool HidesLabel { get; set; }

    /// <summary>True when the line above this editor is worth drawing.</summary>
    public bool ShowsLabel => !HidesLabel;

    /// <summary>An optional field says so, whichever of its names is the one showing.</summary>
    private string Name(string label)
        => Definition.Required ? label : Strings.Format("Common.OptionalSuffix", label);

    /// <summary>What the expression editor of a number field hints at.</summary>
    public string FormulaPlaceholder => Strings.Get("Add.FormulaPlaceholder");

    /// <summary>True when a required parameter is still empty, which blocks saving.</summary>
    public bool IsMissing => IsEnabled && IsApplicable && Definition.Required && Definition.Kind switch
    {
        ActionParameterKind.Bool => false,
        ActionParameterKind.Steps => !HasNestedSteps,
        // A list of pictures is filled in when it holds a picture: a row that is there but says
        // nothing is not a picture, and a step that looks for one of those would look for nothing.
        ActionParameterKind.Images => !Pictures.Any(picture => !string.IsNullOrWhiteSpace(picture.Text)),
        // The other lists are read the same way: what they hold is in their rows rather than in the
        // field's text, so judging them by the text alone would call a filled-in run of keys empty.
        ActionParameterKind.KeySequence => !HasKeyPress,
        ActionParameterKind.Region => Regions.Count == 0,
        // A pattern is filled in when every row of it says all three things: a row that is half
        // written is a step that would be refused at run time, so it is refused here instead.
        ActionParameterKind.ColourPoints => ColourPointRows.Count == 0
            || ColourPointRows.Any(row => !row.IsComplete),
        // A list of colours may be left out altogether; a row of it that says nothing is a step the
        // engine would refuse, so it is refused here instead.
        ActionParameterKind.Colours => ColourRows.Any(row => row.Colour.Trim().Length == 0),
        // Handing nothing over is a legitimate answer here: the step simply leaves no result.
        ActionParameterKind.Returns => false,
        _ => string.IsNullOrWhiteSpace(CurrentText),
    };

    /// <summary>Optional parameters left blank are dropped from the saved node.</summary>
    /// <remarks>
    /// A list of places to look at is there when it holds a place: an empty one means the whole
    /// screen, which is what the step does when the list is not written down at all.
    /// </remarks>
    public bool IsIncluded => IsEnabled && IsApplicable && Definition.Kind switch
    {
        ActionParameterKind.Bool => Definition.Required
            || Flag != string.Equals(Definition.DefaultValue, "true",
                StringComparison.OrdinalIgnoreCase),
        ActionParameterKind.Steps => HasNestedSteps,
        ActionParameterKind.Region => Regions.Count > 0,
        ActionParameterKind.ColourPoints => ColourPointRows.Count > 0,
        ActionParameterKind.Colours => ColourRows.Count > 0,
        ActionParameterKind.KeySequence => HasKeyPress,
        ActionParameterKind.Images => Pictures.Any(picture => !string.IsNullOrWhiteSpace(picture.Text)),
        ActionParameterKind.Narrow => HasNarrowRows,
        ActionParameterKind.Returns => HandsOverAnything,
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
}
