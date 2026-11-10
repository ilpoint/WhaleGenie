using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// What the row says under the name: the parameters that matter, and the sentence a flow step reads as.
public partial class MacroStep : INotifyPropertyChanged
{
    /// <summary>Action key plus the parameters that have a value, shown under the name.</summary>
    public string Detail
    {
        get
        {
            if (Type == "control.switch")
            {
                return SwitchDetail;
            }

            // A condition reads as the sentence it is. The type key and the field name in front of
            // it say which action and which box, and neither is anything the person reading the
            // list needs to be told again: the row already carries the action's name and icon.
            if (Type == "control.if")
            {
                var condition = Text("condition").Trim();
                return condition.Length == 0 ? Strings.Get("Editor.IfNoCondition") : condition;
            }

            // The other flow actions stand for one sentence each: how many rounds, which items,
            // which value is compared. Their field names are how the form asks the question, and
            // six "name = value" pairs never fit in a thirty-pixel row.
            if (FlowDetail() is { } flow)
            {
                return flow;
            }

            var parts = new List<string>();
            foreach (var parameter in Parameters)
            {
                var label = ParameterLabel(parameter);
                switch (parameter.Kind)
                {
                    case ActionParameterKind.Region when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.RegionCount", parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.KeySequence when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.KeyCount", parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.Images when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.PictureCount", parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.Returns when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.ReturnCount", parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.Arguments when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.ArgumentCount", parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.Narrow when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.NarrowCount", parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.ColourPoints when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.ColourPointCount",
                            parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.Steps or ActionParameterKind.Region
                        or ActionParameterKind.KeySequence
                        or ActionParameterKind.Images or ActionParameterKind.Returns
                        or ActionParameterKind.Arguments or ActionParameterKind.Narrow
                        or ActionParameterKind.ColourPoints:
                        break;
                    default:
                        if (!string.IsNullOrWhiteSpace(parameter.Value))
                        {
                            parts.Add($"{label} = {DescribeValue(parameter)}{JitterNote(parameter)}");
                        }
                        break;
                }
            }

            var values = string.Join(", ", parts);
            return values.Length == 0 ? Type : $"{Type} · {values}";
        }
    }

    /// <summary>How much a value is allowed to move, shown after it, or nothing when it is fixed.</summary>
    private static string JitterNote(StepParameter parameter)
        => parameter.HasJitter
            ? " " + Strings.Format("Editor.JitterDetail",
                (parameter.Jitter * 100m).ToString("0.##", CultureInfo.InvariantCulture))
            : string.Empty;

    /// <summary>
    /// What a switch says on its own row: the value it compares and how it compares them. The two
    /// field names ("value (operand)", "match mode (operator)") are how the form asks for them, not
    /// how the block reads — with the branches laid out underneath, the one sentence the block
    /// stands for is "this value, compared this way", and spelling out which field is which twice
    /// per switch costs the row its whole line without saying anything more.
    /// </summary>
    private string SwitchDetail
    {
        get
        {
            var value = Text("switchValue").Trim();
            var mode = Text("switchMode").Trim();
            var how = mode.Length == 0
                ? string.Empty
                : Strings.Get($"control.switch.switchMode.option.{mode}", mode);
            if (value.Length == 0)
            {
                return Strings.Get("Editor.SwitchNoValue");
            }

            return how.Length == 0 ? value : $"{value} {how}";
        }
    }

    /// <summary>
    /// The one sentence a flow action stands for, or null when this step is some other action.
    /// Every branch keeps the field names out of the sentence: the row already says which action
    /// it is, and "次数 = 10" is the form's way of asking rather than the reader's way of saying.
    /// </summary>
    private string? FlowDetail() => Type switch
    {
        "control.repeat" => FlowRepeat,
        "control.for" => FlowFor,
        "control.forEach" => FlowForEach,
        "control.while" => FlowWhile,
        "control.waitUntil" => FlowWaitUntil,
        "control.try" => FlowTry,
        "control.ask" or "control.input" => Text("message").Trim(),
        "control.runMacro" => FlowCall,
        _ => null,
    };

    private string FlowRepeat
    {
        get
        {
            var times = Text("times").Trim();
            if (times.Length == 0)
            {
                return string.Empty;
            }

            var sentence = Strings.Format("Editor.FlowRepeat", times);
            var interval = Text("intervalMs").Trim();
            if (interval.Length > 0 && SpansTime(interval))
            {
                sentence = Strings.Format("Editor.FlowInterval", sentence, interval);
            }

            return WithResults(sentence);
        }
    }

    private string FlowFor
    {
        get
        {
            var from = Text("rangeFrom").Trim();
            var to = Text("rangeTo").Trim();
            if (from.Length == 0 || to.Length == 0)
            {
                return string.Empty;
            }

            var sentence = Strings.Format("Editor.FlowFor", from, to);
            var step = WholeNumber(Text("stepSize").Trim());
            if (step > 0)
            {
                sentence = Strings.Format("Editor.FlowForUp", sentence, step.Value);
            }
            else if (step < 0)
            {
                sentence = Strings.Format("Editor.FlowForDown", sentence, -step.Value);
            }

            var name = Text("name").Trim();
            if (name.Length > 0)
            {
                sentence = Strings.Format("Editor.FlowForNamed", sentence, name);
            }

            return WithResults(sentence);
        }
    }

    private string FlowForEach
    {
        get
        {
            var items = Text("items").Trim();
            if (items.Length == 0)
            {
                return Strings.Get("Editor.FlowNoItems");
            }

            var sentence = Strings.Format("Editor.FlowForEach", items);
            if (string.Equals(Text("reverse").Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                sentence = Strings.Format("Editor.FlowReverse", sentence);
            }

            return WithResults(sentence);
        }
    }

    private string FlowWhile
    {
        get
        {
            var condition = Text("condition").Trim();
            if (condition.Length == 0)
            {
                return Strings.Get("Editor.IfNoCondition");
            }

            var limit = Text("maxIterations").Trim();
            var sentence = limit.Length == 0
                ? condition
                : Strings.Format("Editor.FlowAtMost", condition, limit);

            var interval = Text("intervalMs").Trim();
            if (interval.Length > 0 && SpansTime(interval))
            {
                sentence = Strings.Format("Editor.FlowInterval", sentence, interval);
            }

            return WithResults(sentence);
        }
    }

    private string FlowWaitUntil
    {
        get
        {
            var condition = Text("condition").Trim();
            return condition.Length == 0
                ? Strings.Get("Editor.IfNoCondition")
                : WithResults(condition);
        }
    }

    /// <summary>
    /// How the attempt is built: three lists of steps, and the only thing worth saying on the row
    /// is how much sits in each. The counts come from the lists themselves, so a row cannot claim
    /// a block has steps it does not have.
    /// </summary>
    private string FlowTry
        => WithResults(Strings.Format("Editor.FlowTry",
            StepsIn("body"), StepsIn("catch"), StepsIn("finally")));

    private string FlowCall
    {
        get
        {
            var macro = Text("macro").Trim();
            return macro.Length == 0 ? Strings.Get("Editor.FlowNoMacro") : WithResults(macro);
        }
    }

    /// <summary>What the step hands over, added as a closing clause when it hands anything over.</summary>
    private string WithResults(string sentence)
    {
        var returns = Parameters.FirstOrDefault(parameter => parameter.Kind == ActionParameterKind.Returns);
        if (returns is null || returns.Rows.Count == 0)
        {
            return sentence;
        }

        var kept = Strings.Format("Editor.FlowReturns", returns.Rows.Count);
        return sentence.Length == 0 ? kept : Strings.Format("Editor.FlowWithResults", sentence, kept);
    }

    private int StepsIn(string name)
        => Parameters.FirstOrDefault(parameter => parameter.Name == name)?.Steps.Count ?? 0;

    /// <summary>
    /// Whether the cell asks for a wait. An expression counts as one: it is read at run time, so
    /// the row cannot tell, and leaving the clause in is the honest reading.
    /// </summary>
    private static bool SpansTime(string text) => WholeNumber(text) != 0;

    /// <summary>The number as written, or null when the cell holds an expression instead.</summary>
    private static int? WholeNumber(string text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
