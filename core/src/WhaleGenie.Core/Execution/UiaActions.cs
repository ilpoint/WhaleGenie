using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The UI Automation actions: asking whether an element is there, finding it, working it, reading
/// what it says. A selector is turned into a query here, together with the window it is looked for
/// in and the conditions that narrow it, so every action of this family searches the same way.
/// </summary>
internal static class UiaActions
{
    /// <summary>
    /// The uia actions, each answered by the method written for it. A step of another family is not
    /// in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["uia.exists"] = (run, step, depth, _) =>
            {
                Exists(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["uia.find"] = (run, step, depth, _) =>
            {
                FindElement(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["uia.waitElement"] = async (run, step, depth, token) =>
            {
                await WaitElement(run, step, depth, token);
                return Signal.Normal;
            },

            ["uia.click"] = async (run, step, depth, token) =>
            {
                await ClickElement(run, step, depth, token);
                return Signal.Normal;
            },

            ["uia.invoke"] = async (run, step, depth, token) =>
            {
                await InvokeElement(run, step, depth, token);
                return Signal.Normal;
            },

            ["uia.setText"] = (run, step, depth, _) =>
            {
                SetElementText(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["uia.read"] = (run, step, depth, _) =>
            {
                ReadElement(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["uia.select"] = (run, step, depth, _) =>
            {
                SelectItem(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["uia.check"] = (run, step, depth, _) =>
            {
                SetElementChecked(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["uia.expand"] = (run, step, depth, _) =>
            {
                SetElementExpanded(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["uia.scrollIntoView"] = (run, step, depth, _) =>
            {
                ScrollElementIntoView(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["uia.readTable"] = (run, step, depth, _) =>
            {
                ReadElementTable(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["uia.focusWindow"] = (run, step, depth, _) =>
            {
                FocusWindow(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },
        };

    private static void Exists(IRunContext run, ExecutableStep step, int depth)
    {
        var found = run.Devices.Ui.Exists(Query(run, step), 0);
        run.Store(step, "exists", Value.FromBool(found), depth);
    }

    private static async Task WaitElement(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var query = Query(run, step);
        var timeout = step.Meta.TimeoutMs;
        var poll = run.Args(step).Number("intervalMs");
        if (poll <= 0)
        {
            poll = 200;
        }

        if (!await Pacing.WaitForFlagAsync(() => run.Devices.Ui.Exists(query, 0), timeout, poll,
                token))
        {
            throw new StepFailure(RunKeys.ElementNotFound, run.Args(step).Text("uiSelector"));
        }

        // What turned up, handed over the way uia.read hands over the same thing: a step that waited
        // for a box and then reads it is two queries for one answer, and the second one can find a
        // different element than the one that made the wait end.
        if (run.Wants(step, "element") && run.Devices.Ui.Facts(query) is { } found)
        {
            run.Give(step, "element", ElementRecord(found));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ElementFound,
            run.Args(step).Text("uiSelector"));
    }

    /// <summary>
    /// One element as a record: what it says about itself and where it is. Shared by reading an
    /// element and by waiting for one, so both spell the same fields the same way.
    /// </summary>
    private static Value ElementRecord(UiElementFacts facts)
    {
        var middle = ScreenMath.Middle(facts.Location, facts.Size);
        return Value.FromRecord(
        [
            (FieldNames.Enabled, Value.FromBool(facts.Enabled)),
            (FieldNames.Visible, Value.FromBool(facts.Visible)),
            (FieldNames.Value, Value.FromText(facts.Value)),
            (FieldNames.Name, Value.FromText(facts.Name)),
            (FieldNames.AutomationId, Value.FromText(facts.AutomationId)),
            (FieldNames.ControlType, Value.FromText(facts.ControlType)),
            (FieldNames.X, Value.FromNumber(middle.X)),
            (FieldNames.Y, Value.FromNumber(middle.Y)),
            (FieldNames.Left, Value.FromNumber(facts.Location.X)),
            (FieldNames.Top, Value.FromNumber(facts.Location.Y)),
            (FieldNames.Width, Value.FromNumber(facts.Size.Width)),
            (FieldNames.Height, Value.FromNumber(facts.Size.Height)),
            (FieldNames.Box, Value.FromText(
                $"{facts.Location.X},{facts.Location.Y},{facts.Size.Width},{facts.Size.Height}")),
        ]);
    }

    /// <summary>
    /// Where an element sits, written into a variable the way a found picture is: the centre as
    /// "x,y", and the parts as name.x, name.y, name.width and name.height. That is what lets a
    /// later step point at the element — or a little way from it — without hunting for it again.
    /// </summary>
    private static void FindElement(IRunContext run, ExecutableStep step, int depth)
    {
        var every = run.Wants(step, "every");
        var index = run.Index(step, "matchIndex", 1);

        // UIA reads no picture, so the source report is the window the element was looked for in
        // rather than whatever the last vision step happened to look at.
        var source = run.Lookup(step);

        // Asking for the whole list is what makes the list worth collecting; otherwise the search
        // only has to reach as far as the hit the step named.
        var hits = run.Devices.Ui.FindAll(Query(run, step), every ? RunLimits.Matches : index);
        var match = index <= hits.Count ? hits[index - 1] : null;
        run.Give(step, "found", Value.FromBool(match is not null));

        if (match is null)
        {
            run.GiveNothing(step);
        }
        else
        {
            run.GiveMatch(step, match.Location, match.Size, 1, match.Name);
            run.Give(step, "window", Value.FromText(source?.Title ?? string.Empty));
        }

        run.GiveEvery(step, hits.Select(element =>
            HitRecord.Of(element.Location, element.Size, 1, element.Name)));
        run.Log(LogLevel.Info, depth, step.Type,
            match is null ? LogKeys.ElementMissing : LogKeys.ElementWhere,
            match is null ? string.Empty : $"{Centre(match).X},{Centre(match).Y}");
    }

    /// <summary>The middle of an element, which is the point a click aimed at it would land on.</summary>
    private static ScreenPoint Centre(UiElementInfo element)
        => ScreenMath.Middle(element.Location, element.Size);

    private static async Task ClickElement(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var query = Query(run, step);
        var timeout = step.Meta.TimeoutMs;

        if (!await Pacing.WaitForFlagAsync(() => run.Devices.Ui.Exists(query, 0), timeout, 200,
                token))
        {
            throw new StepFailure(RunKeys.ElementNotFound, run.Args(step).Text("uiSelector"));
        }

        if (!run.Devices.Ui.Click(query, run.Button(step)))
        {
            throw new StepFailure(RunKeys.ElementNotClickable, run.Args(step).Text("uiSelector"));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedElement,
            run.Args(step).Text("uiSelector"));
    }

    private static void SetElementText(IRunContext run, ExecutableStep step, int depth)
    {
        var query = Query(run, step);
        var text = run.Args(step).ValueOf("valueText").AsText();
        var clear = !string.Equals(run.Args(step).Text("clearFirst").Trim(), "false",
            StringComparison.OrdinalIgnoreCase);

        if (!run.Devices.Ui.SetText(query, text, clear))
        {
            throw new StepFailure(RunKeys.ElementNotWritable, run.Args(step).Text("uiSelector"));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ElementFilled,
            run.Args(step).Text("uiSelector"), text);
    }

    /// <summary>
    /// Works the element the way the element itself knows how, which is what a step wants when the
    /// pointer has to stay where it is: a background window's button can be pressed without the
    /// window coming forward and without the mouse moving.
    /// </summary>
    private static async Task InvokeElement(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var query = Query(run, step);
        var timeout = step.Meta.TimeoutMs;

        if (!await Pacing.WaitForFlagAsync(() => run.Devices.Ui.Exists(query, 0), timeout, 200,
                token))
        {
            throw new StepFailure(RunKeys.ElementNotFound, run.Args(step).Text("uiSelector"));
        }

        if (!run.Devices.Ui.Invoke(query))
        {
            throw new StepFailure(RunKeys.ElementNotInvocable, run.Args(step).Text("uiSelector"));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.InvokedElement,
            run.Args(step).Text("uiSelector"));
    }

    /// <summary>
    /// Everything an element says about itself, as one record: whether it can be used, whether it
    /// is showing, what it holds, what it is called and where it sits. One record rather than a
    /// variable each is the same shape a hit and a window already come in, so a macro reads one
    /// part of it by name.
    /// </summary>
    private static void ReadElement(IRunContext run, ExecutableStep step, int depth)
    {
        var facts = run.Devices.Ui.Facts(Query(run, step))
            ?? throw new StepFailure(RunKeys.ElementNotFound, run.Args(step).Text("uiSelector"));

        run.Give(step, "element", ElementRecord(facts));

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ReadElement,
            run.Args(step).Text("uiSelector"), facts.Value);
    }

    private static void FocusWindow(IRunContext run, ExecutableStep step, int depth)
    {
        var rule = run.WindowRuleOf(step);
        if (!run.Devices.Ui.FocusWindow(UiQuery.Parse(string.Empty, rule.MatchValue, 1,
                rule.Reading.Match, rule.Reading.Compare, rule.Options)))
        {
            throw new StepFailure(RunKeys.WindowNotFound, rule.MatchValue);
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.FocusedWindow, rule.MatchValue);
    }

    /// <summary>
    /// Picks one entry of a list, a drop-down or a set of tabs. The entry is named either by the
    /// text it shows — which a variable may hold, so a value read somewhere else can be chosen
    /// straight away — or by its number, counted from one.
    /// </summary>
    private static void SelectItem(IRunContext run, ExecutableStep step, int depth)
    {
        var item = run.Args(step).ValueOf("item").AsText().Trim();
        var index = run.Args(step).Number("itemIndex");
        var which = item.Length > 0 ? item : index.ToString(CultureInfo.InvariantCulture);
        if (item.Length == 0 && index <= 0)
        {
            throw new StepFailure(RunKeys.MissingItem);
        }

        if (!run.Devices.Ui.Select(Query(run, step), item, index))
        {
            throw new StepFailure(RunKeys.ItemNotFound, which);
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.SelectedItem,
            run.Args(step).Text("uiSelector"), which);
    }

    /// <summary>Turns a check box on, off, or the other way round.</summary>
    private static void SetElementChecked(IRunContext run, ExecutableStep step, int depth)
    {
        if (!run.Devices.Ui.SetChecked(Query(run, step), Switch(run.Args(step).Text("checkState"))))
        {
            throw new StepFailure(RunKeys.ElementNotCheckable, run.Args(step).Text("uiSelector"));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ElementChecked,
            run.Args(step).Text("uiSelector"));
    }

    /// <summary>Opens or closes a node: a tree branch, an accordion, a collapsed panel.</summary>
    private static void SetElementExpanded(IRunContext run, ExecutableStep step, int depth)
    {
        var action = run.Args(step).Text("expandState").Trim();
        if (!run.Devices.Ui.SetExpanded(Query(run, step), action.Length == 0 ? "expand" : action))
        {
            throw new StepFailure(RunKeys.ElementNotExpandable, run.Args(step).Text("uiSelector"));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ElementExpanded,
            run.Args(step).Text("uiSelector"));
    }

    /// <summary>Scrolls an element into view inside whatever list or panel holds it.</summary>
    private static void ScrollElementIntoView(IRunContext run, ExecutableStep step, int depth)
    {
        if (!run.Devices.Ui.ScrollIntoView(Query(run, step)))
        {
            throw new StepFailure(RunKeys.ElementNotScrollable, run.Args(step).Text("uiSelector"));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ScrolledElement,
            run.Args(step).Text("uiSelector"));
    }

    /// <summary>
    /// Reads a table into a variable as a list of rows, each row a record whose keys are the names
    /// the table writes above its columns — the same shape the CSV reader hands back, so the same
    /// steps walk either one and a cell is read by name rather than by counting along the line.
    /// </summary>
    private static void ReadElementTable(IRunContext run, ExecutableStep step, int depth)
    {
        // A step with no row limit at all still gets one, the same default the editor shows, so a
        // table nobody bounded cannot be read forever.
        var limit = Math.Clamp(run.Args(step).Number("maxRows"), 1, RunLimits.Rows);
        var table = run.Devices.Ui.ReadTable(Query(run, step), limit);

        // A grid that keeps no strip of column names still has columns: they are the places its
        // cells sit in, so the widest row says how many there are.
        var width = table.Columns.Count > 0
            ? table.Columns.Count
            : table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Count);
        var names = TableColumns.Names([.. table.Columns.Select(Value.FromText)], width);

        var name = run.Give(step, "table", Value.FromList(
            table.Rows.Select(row => Value.FromRecord(
                Enumerable.Range(0, width).Select(index => (
                    names[index],
                    Value.FromText(index < row.Count ? row[index] : string.Empty)))))));

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ReadTable, name, table.Rows.Count);
    }

    /// <summary>
    /// What a three-way setting means for something that can be on, off, or left to decide itself:
    /// true, false, or nothing at all for "the other way round".
    /// </summary>
    private static bool? Switch(string text) => text.Trim().ToLowerInvariant() switch
    {
        "on" or "true" or "yes" => true,
        "off" or "false" or "no" => false,
        _ => null,
    };

    /// <summary>
    /// Reads a selector such as <c>Button[name='Save']</c> or <c>Edit[automationId='input']</c>,
    /// together with the window it is looked for in. That window is recognised by the same rule
    /// every other step recognises one with, and the search is narrowed by it rather than by a title
    /// alone: a step that aims at a window by its program or by a pattern finds the same window a
    /// window action would.
    /// </summary>
    private static UiQuery Query(IRunContext run, ExecutableStep step)
    {
        var rule = run.WindowRuleOf(step);
        var query = UiQuery.Parse(run.Args(step).Text("uiSelector"), rule.MatchValue,
            run.Index(step, "matchIndex", 1), rule.Reading.Match, rule.Reading.Compare,
            rule.Options);
        return query with { Narrow = NarrowConditions(run, step), Anchor = AnchorOf(run, step, rule) };
    }

    /// <summary>
    /// 两个同名按钮里要右边那一个时写的那一行：挨着谁、在哪一边、至少隔开多少。锚点选择器与
    /// 主选择器走同一个解析器、同一条窗口规则，因为它说的也是这个窗口里的一个元素。
    /// </summary>
    /// <remarks>
    /// 那一格空着就不设锚点，不是「间隔为零的锚点」：这个动作本来就不问锚点。哪一边写了目录
    /// 之外的词由目录的选项表拦下（Run.BadOption），不在这里另写一份判断；间隔的数值范围同理
    /// 由目录的 Minimum/Maximum 管。
    /// </remarks>
    private static UiAnchor? AnchorOf(IRunContext run, ExecutableStep step, WindowRule rule)
    {
        var selector = run.Args(step).Text("anchorElement").Trim();
        if (selector.Length == 0)
        {
            return null;
        }

        var side = run.Args(step).Text("anchorSide").Trim().ToLowerInvariant() switch
        {
            "right" => UiSide.Right,
            "above" => UiSide.Above,
            "below" => UiSide.Below,
            _ => UiSide.Left,
        };

        var anchor = UiQuery.Parse(selector, rule.MatchValue, 1,
            rule.Reading.Match, rule.Reading.Compare, rule.Options);

        return new UiAnchor(anchor, side, Math.Max(0, (int)run.Args(step).Number("anchorGap")));
    }

    /// <summary>
    /// 元素除了选择器说的那几项，还得像什么样：一行一条，全部同时成立。两件事分得很清楚——
    /// 选择器说它叫什么，这里说它在不在屏幕上、能不能用、名字里有没有一段字。
    /// </summary>
    /// <remarks>
    /// 没填「哪一种」的行当它不存在：那是一个还没写完的表单，不是一条谁都没要的条件。写了
    /// 一个引擎不认识的方式则报错，不放过：少了一个条件，这一步会落到另一个元素上，而且不说。
    /// 要一段字的两条，字没写也当不存在；「看得见」「能用」本来就不需要字，空着才是它们的样子。
    /// </remarks>
    private static IReadOnlyList<UiCondition> NarrowConditions(IRunContext run,
        ExecutableStep step)
    {
        var conditions = new List<UiCondition>();
        foreach (var row in step.Rows("narrow"))
        {
            var how = run.Word(row, FieldNames.How);
            if (how.Length == 0)
            {
                continue;
            }

            if (!Enum.TryParse<UiConditionHow>(how, ignoreCase: true, out var kind))
            {
                throw new StepFailure(RunKeys.ElementBadCondition, how);
            }

            var value = kind is UiConditionHow.NameHas or UiConditionHow.NameHasNot
                ? run.Text(row, FieldNames.Value).Trim()
                : string.Empty;

            if (kind is (UiConditionHow.NameHas or UiConditionHow.NameHasNot) && value.Length == 0)
            {
                continue;
            }

            conditions.Add(new UiCondition(kind, value));
        }

        return conditions;
    }
}
