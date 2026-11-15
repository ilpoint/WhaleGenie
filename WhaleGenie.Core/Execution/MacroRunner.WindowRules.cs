using System;
using System.Globalization;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

// Finding the window a step is about, and reading the rule it named. Every family that acts on a
// window goes through these, so a step that names one names it the same way everywhere.
public sealed partial class MacroRunner
{
    /// <summary>
    /// The window a step names, or a failure saying which one could not be found. "Match by" says
    /// what the text is held up against and how; an empty text means whatever window is in front.
    /// </summary>
    private WindowInfo Locate(ExecutableStep step)
        => Lookup(step) ?? throw new StepFailure(RunKeys.WindowNotFound, WindowText(step));

    /// <summary>The same, for the steps that report rather than fail when no window matches.</summary>
    private WindowInfo? Lookup(ExecutableStep step)
    {
        var rule = WindowRuleOf(step);
        if (!rule.HasTarget)
        {
            return _devices.Windows.Foreground();
        }

        return _devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
            rule.Options);
    }

    /// <summary>
    /// The one window rule every step family reads. A literal rule is written as a record and a
    /// variable may hand over a record read earlier; no older title-only shape is read.
    /// </summary>
    private WindowRule WindowRuleOf(ExecutableStep step, string field = "window")
    {
        var source = step.Written(field);
        var written = Formula.Plain(source).Trim();
        if (written.Length == 0 && Formula.IsNothing(source))
        {
            return WindowRule.Empty;
        }

        var value = WindowValue(step, field);
        WindowRule rule;
        if (value.IsRecord)
        {
            rule = RuleFromRecord(value);
        }
        else if (WindowRule.TryParse(written, out var parsed))
        {
            rule = parsed;
        }
        else
        {
            throw new StepFailure(RunKeys.BadWindowRule,
                written.Length > 0 ? written : Formula.Speak(source));
        }

        if (rule.Follow)
        {
            // 「沿用上一次用的窗口」：取这一轮里上一个自己指定了窗口的步骤那句话。一次都还没有过
            // 时（它排在第一个，或者上面的步骤都没提窗口）跟空着一样，用前台窗口。
            return _run.LastWindowRule;
        }

        var (match, compare) = rule.Reading;
        var selected = rule.MatchValue.Trim();

        if (match is WindowMatch.Handle && rule.Handle == 0)
        {
            throw new StepFailure(RunKeys.BadWindowRule,
                "a window handle rule needs a non-zero handle");
        }

        // A pattern the machine cannot read is the macro's problem to see, not a stack trace to
        // puzzle over, so it comes back as a failed step before anything is looked at — the same
        // words whichever step asked.
        try
        {
            WindowHow.Check(selected, compare, rule.Options);
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadPattern, selected);
        }

        var resolved = rule with
        {
            How = string.IsNullOrWhiteSpace(rule.How) ? WindowHow.TitleContains : rule.How,
        };

        // 记住了，后面的步骤「沿用上一次」才有东西可沿用。这一次不一定要找到它：没找到的规则
        // 也是这一步要的那个窗口，下一步跟着它一起失败，比悄悄换成前台窗口更容易查。
        if (resolved.HasTarget)
        {
            _run.LastWindowRule = resolved;
        }

        return resolved;
    }

    /// <summary>A window record read by a step, with the rule fields a match needs.</summary>
    private static WindowRule RuleFromRecord(Value record)
    {
        string Text(string name, string fallback = "")
            => record.Field(name)?.AsText().Trim() ?? fallback;

        bool Flag(string name, bool fallback)
            => record.Field(name)?.AsBool() ?? fallback;

        var how = Text(FieldNames.How, WindowHow.TitleContains);
        var selected = WindowHow.Read(how).Match switch
        {
            WindowMatch.Process => Text(FieldNames.Process),
            WindowMatch.ClassName => Text(FieldNames.Class),
            WindowMatch.Handle => record.Field(FieldNames.Handle)?.AsNumber() is { } windowHandle
                && windowHandle != 0
                ? windowHandle.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            _ => Text(FieldNames.Title),
        };
        if (selected.Length == 0)
        {
            var needed = WindowHow.Read(how).Match switch
            {
                WindowMatch.Process => FieldNames.Process,
                WindowMatch.ClassName => FieldNames.Class,
                WindowMatch.Handle => FieldNames.Handle,
                _ => FieldNames.Title,
            };
            throw new StepFailure(RunKeys.WindowRecordField, needed);
        }

        return new WindowRule(
            Text(FieldNames.Title),
            Text(FieldNames.Process),
            Text(FieldNames.Class),
            record.Field(FieldNames.Handle)?.AsNumber() is { } handle ? (long)handle : 0,
            how,
            Flag(FieldNames.CaseSensitive, false),
            Flag(FieldNames.Trim, true),
            Flag(FieldNames.Multiline, false));
    }

    /// <summary>
    /// What a step wrote into a window field: the value of the variable it names when that variable
    /// holds a window record, and the literal text otherwise. A cell that is one known name is read
    /// as that value so a record can be handed over whole.
    /// </summary>
    private Value WindowValue(ExecutableStep step, string field)
    {
        var written = step.Written(field);
        if (written is VariableNode name && VariableName.IsName(name.Name)
            && Variables.TryGet(name.Name, out var value))
        {
            return value;
        }

        return Read(written);
    }

    /// <summary>What a step wrote into its window field, with the variables in it resolved.</summary>
    private string WindowText(ExecutableStep step) => WindowRuleOf(step).MatchValue.Trim();
}
