using System;
using System.Globalization;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Finding the window a step is about and reading the rule it named. Every family that acts on a
/// window goes through these, so a step that names one names it the same way everywhere, and the
/// rule a step wrote is kept for the steps that say "the same as last time".
/// </summary>
internal sealed class WindowFacts(IDeviceLayer devices, VariableStore variables, RunState run)
{
    /// <summary>The fields of one step, read the way the catalogue says they are written.</summary>
    private StepArguments Args(ExecutableStep step)
        => new(step, variables, devices.Screen.RefreshMilliseconds);

    /// <summary>
    /// The window a step names, or a failure saying which one could not be found. "Match by" says
    /// what the text is held up against and how; an empty text means whatever window is in front.
    /// </summary>
    public WindowInfo Locate(ExecutableStep step)
        => Lookup(step) ?? throw new StepFailure(RunKeys.WindowNotFound, Text(step));

    /// <summary>The same, for the steps that report rather than fail when no window matches.</summary>
    public WindowInfo? Lookup(ExecutableStep step)
    {
        var rule = RuleOf(step);
        if (!rule.HasTarget)
        {
            return devices.Windows.Foreground();
        }

        return devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
            rule.Options);
    }

    /// <summary>
    /// The one window rule every step family reads. A literal rule is written as a record and a
    /// variable may hand over a record read earlier; no older title-only shape is read.
    /// </summary>
    public WindowRule RuleOf(ExecutableStep step, string field = "window")
    {
        var source = step.Written(field);
        var written = Formula.Plain(source).Trim();
        if (written.Length == 0 && Formula.IsNothing(source))
        {
            return WindowRule.Empty;
        }

        var value = ValueOf(step, field);
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
            return run.LastWindowRule;
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
            run.LastWindowRule = resolved;
        }

        return resolved;
    }

    /// <summary>What a step wrote into its window field, with the variables in it resolved.</summary>
    public string Text(ExecutableStep step) => RuleOf(step).MatchValue.Trim();

    /// <summary>The window a background step posts at, or a failure naming the missing rule.</summary>
    public WindowInfo Background(ExecutableStep step)
    {
        var rule = RuleOf(step);
        if (rule.MatchValue.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingTargetWindow);
        }

        return devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
                   rule.Options)
               ?? throw new StepFailure(RunKeys.WindowNotFound, rule.MatchValue);
    }

    /// <summary>
    /// The corner a step's numbers are measured from, or <c>null</c> when they are screen pixels
    /// already, together with the window those numbers were counted from when there was one. The
    /// window is also where the step's pixels are to be read from: a step that says "a hundred
    /// pixels into the game window" and a step that says "read the game window" are saying the same
    /// thing about which window, and one of them should not have to say it twice.
    /// </summary>
    public (ScreenPoint? Corner, WindowInfo? Window) AnchorOf(ExecutableStep step)
    {
        var mode = Args(step).Text("anchorMode").Trim().ToLowerInvariant();
        if (mode.Length > 0 && mode is not ("screen" or "window"))
        {
            throw new StepFailure(RunKeys.BadOption, "anchorMode");
        }

        if (mode != "window")
        {
            return (null, null);
        }

        // The window is recognised by the same captured rule every window action reads.
        var rule = RuleOf(step);
        if (!rule.HasTarget)
        {
            var front = devices.Windows.Foreground();
            return (front is { } there ? devices.Windows.ClientOrigin(there.Handle) : null, front);
        }

        var window = devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
                         rule.Options)
                     ?? throw new StepFailure(RunKeys.WindowNotFound, rule.MatchValue);

        var corner = devices.Windows.ClientOrigin(window.Handle);
        return (corner, window);
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
                ? windowHandle.ToString(CultureInfo.InvariantCulture)
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
    private Value ValueOf(ExecutableStep step, string field)
    {
        var written = step.Written(field);
        if (written is VariableNode name && VariableName.IsName(name.Name)
            && variables.TryGet(name.Name, out var value))
        {
            return value;
        }

        return Expression.Evaluate(written, variables);
    }
}
