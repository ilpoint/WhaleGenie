using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

public sealed partial class MacroRunner
{
    // ---------------------------------------------------------------------- uia

    private void Exists(ExecutableStep step, int depth)
    {
        var found = _devices.Ui.Exists(Query(step), 0);
        Store(step, "exists", Value.FromBool(found), depth);
    }

    private async Task WaitElement(ExecutableStep step, int depth, CancellationToken token)
    {
        var query = Query(step);
        var timeout = step.Meta.TimeoutMs;
        var poll = Number(step, "intervalMs");
        if (poll <= 0)
        {
            poll = 200;
        }

        if (!await WaitForFlagAsync(() => _devices.Ui.Exists(query, 0), timeout, poll, token))
        {
            throw new StepFailure(RunKeys.ElementNotFound, ReadText(step, "uiSelector"));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.ElementFound, ReadText(step, "uiSelector"));
    }

    /// <summary>
    /// Where an element sits, written into a variable the way a found picture is: the centre as
    /// "x,y", and the parts as $name.x, $name.y, $name.width and $name.height. That is what lets a
    /// later step point at the element — or a little way from it — without hunting for it again.
    /// </summary>
    private void FindElement(ExecutableStep step, int depth)
    {
        var every = Wants(step, "every");
        var index = Index(step, "matchIndex", 1);

        // Asking for the whole list is what makes the list worth collecting; otherwise the search
        // only has to reach as far as the hit the step named.
        var hits = _devices.Ui.FindAll(Query(step), every ? MatchLimit : index);
        var match = index <= hits.Count ? hits[index - 1] : null;

        if (match is null)
        {
            GiveNothing(step);
        }
        else
        {
            GiveMatch(step, match.Location, match.Size, 1, match.Name);
        }

        GiveEvery(step, hits.Select(element => Hit(element.Location, element.Size, 1, element.Name)));
        Log(LogLevel.Info, depth, step.Type, match is null ? LogKeys.ElementMissing : LogKeys.ElementWhere,
            match is null ? string.Empty : $"{Centre(match).X},{Centre(match).Y}");
    }

    /// <summary>The middle of an element, which is the point a click aimed at it would land on.</summary>
    private static ScreenPoint Centre(UiElementInfo element)
        => new(element.Location.X + (element.Size.Width / 2),
            element.Location.Y + (element.Size.Height / 2));

    private async Task ClickElement(ExecutableStep step, int depth, CancellationToken token)
    {
        var query = Query(step);
        var timeout = step.Meta.TimeoutMs;

        if (!await WaitForFlagAsync(() => _devices.Ui.Exists(query, 0), timeout, 200, token))
        {
            throw new StepFailure(RunKeys.ElementNotFound, ReadText(step, "uiSelector"));
        }

        if (!_devices.Ui.Click(query, Button(step)))
        {
            throw new StepFailure(RunKeys.ElementNotClickable, ReadText(step, "uiSelector"));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedElement, ReadText(step, "uiSelector"));
    }

    private void SetElementText(ExecutableStep step, int depth)
    {
        var query = Query(step);
        var text = ReadValue(step, "valueText").AsText();
        var clear = !string.Equals(ReadText(step, "clearFirst").Trim(), "false", StringComparison.OrdinalIgnoreCase);

        if (!_devices.Ui.SetText(query, text, clear))
        {
            throw new StepFailure(RunKeys.ElementNotWritable, ReadText(step, "uiSelector"));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.ElementFilled, ReadText(step, "uiSelector"), text);
    }

    /// <summary>
    /// Works the element the way the element itself knows how, which is what a step wants when the
    /// pointer has to stay where it is: a background window's button can be pressed without the
    /// window coming forward and without the mouse moving.
    /// </summary>
    private async Task InvokeElement(ExecutableStep step, int depth, CancellationToken token)
    {
        var query = Query(step);
        var timeout = step.Meta.TimeoutMs;

        if (!await WaitForFlagAsync(() => _devices.Ui.Exists(query, 0), timeout, 200, token))
        {
            throw new StepFailure(RunKeys.ElementNotFound, ReadText(step, "uiSelector"));
        }

        if (!_devices.Ui.Invoke(query))
        {
            throw new StepFailure(RunKeys.ElementNotInvocable, ReadText(step, "uiSelector"));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.InvokedElement, ReadText(step, "uiSelector"));
    }

    /// <summary>
    /// Everything an element says about itself, as one record: whether it can be used, whether it
    /// is showing, what it holds, what it is called and where it sits. One record rather than a
    /// variable each is the same shape a hit and a window already come in, so a macro reads one
    /// part of it by name.
    /// </summary>
    private void ReadElement(ExecutableStep step, int depth)
    {
        var facts = _devices.Ui.Facts(Query(step))
            ?? throw new StepFailure(RunKeys.ElementNotFound, ReadText(step, "uiSelector"));

        var middle = Middle(facts.Location, facts.Size);
        Give(step, "element", Value.FromRecord(
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
        ]));

        Log(LogLevel.Info, depth, step.Type, LogKeys.ReadElement, ReadText(step, "uiSelector"), facts.Value);
    }

    private void FocusWindow(ExecutableStep step, int depth)
    {
        var rule = WindowRuleOf(step, "window", "how");
        if (!_devices.Ui.FocusWindow(UiQuery.Parse(string.Empty, rule.Text, 1, rule.Match,
                rule.Compare, rule.Options)))
        {
            throw new StepFailure(RunKeys.WindowNotFound, rule.Text);
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.FocusedWindow, rule.Text);
    }

    /// <summary>
    /// Picks one entry of a list, a drop-down or a set of tabs. The entry is named either by the
    /// text it shows — which a variable may hold, so a value read somewhere else can be chosen
    /// straight away — or by its number, counted from one.
    /// </summary>
    private void SelectItem(ExecutableStep step, int depth)
    {
        var item = ReadValue(step, "item").AsText().Trim();
        var index = Number(step, "itemIndex");
        var which = item.Length > 0 ? item : index.ToString(CultureInfo.InvariantCulture);
        if (item.Length == 0 && index <= 0)
        {
            throw new StepFailure(RunKeys.MissingItem);
        }

        if (!_devices.Ui.Select(Query(step), item, index))
        {
            throw new StepFailure(RunKeys.ItemNotFound, which);
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.SelectedItem, ReadText(step, "uiSelector"), which);
    }

    /// <summary>Turns a check box on, off, or the other way round.</summary>
    private void SetElementChecked(ExecutableStep step, int depth)
    {
        if (!_devices.Ui.SetChecked(Query(step), Switch(ReadText(step, "checkState"))))
        {
            throw new StepFailure(RunKeys.ElementNotCheckable, ReadText(step, "uiSelector"));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.ElementChecked, ReadText(step, "uiSelector"));
    }

    /// <summary>Opens or closes a node: a tree branch, an accordion, a collapsed panel.</summary>
    private void SetElementExpanded(ExecutableStep step, int depth)
    {
        var action = ReadText(step, "expandState").Trim();
        if (!_devices.Ui.SetExpanded(Query(step), action.Length == 0 ? "expand" : action))
        {
            throw new StepFailure(RunKeys.ElementNotExpandable, ReadText(step, "uiSelector"));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.ElementExpanded, ReadText(step, "uiSelector"));
    }

    /// <summary>Scrolls an element into view inside whatever list or panel holds it.</summary>
    private void ScrollElementIntoView(ExecutableStep step, int depth)
    {
        if (!_devices.Ui.ScrollIntoView(Query(step)))
        {
            throw new StepFailure(RunKeys.ElementNotScrollable, ReadText(step, "uiSelector"));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.ScrolledElement, ReadText(step, "uiSelector"));
    }

    /// <summary>
    /// Reads a table into a variable as a list of rows, each row a record whose keys are the names
    /// the table writes above its columns — the same shape the CSV reader hands back, so the same
    /// steps walk either one and a cell is read by name rather than by counting along the line.
    /// </summary>
    private void ReadElementTable(ExecutableStep step, int depth)
    {
        // A step with no row limit at all still gets one, the same default the editor shows, so a
        // table nobody bounded cannot be read forever.
        var limit = Math.Clamp(Number(step, "maxRows"), 1, MaxRows);
        var table = _devices.Ui.ReadTable(Query(step), limit);

        // A grid that keeps no strip of column names still has columns: they are the places its
        // cells sit in, so the widest row says how many there are.
        var width = table.Columns.Count > 0
            ? table.Columns.Count
            : table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Count);
        var names = ColumnNames([.. table.Columns.Select(Value.FromText)], width);

        var name = Give(step, "table", Value.FromList(
            table.Rows.Select(row => Value.FromRecord(
                Enumerable.Range(0, width).Select(index => (
                    names[index],
                    Value.FromText(index < row.Count ? row[index] : string.Empty)))))));

        Log(LogLevel.Info, depth, step.Type, LogKeys.ReadTable, name, table.Rows.Count);
    }

    /// <summary>The most rows one step reads, so a grid that keeps growing cannot fill memory.</summary>
    private const int MaxRows = 10000;

    /// <summary>How many rows a step reads when it does not say.</summary>
    private const int DefaultRows = 100;

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
    /// The uia actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchUia(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "uia.exists":
                Exists(step, depth);
                return Signal.Normal;
            case "uia.find":
                FindElement(step, depth);
                return Signal.Normal;
            case "uia.waitElement":
                await WaitElement(step, depth, token);
                return Signal.Normal;
            case "uia.click":
                await ClickElement(step, depth, token);
                return Signal.Normal;
            case "uia.invoke":
                await InvokeElement(step, depth, token);
                return Signal.Normal;
            case "uia.setText":
                SetElementText(step, depth);
                return Signal.Normal;
            case "uia.read":
                ReadElement(step, depth);
                return Signal.Normal;
            case "uia.select":
                SelectItem(step, depth);
                return Signal.Normal;
            case "uia.check":
                SetElementChecked(step, depth);
                return Signal.Normal;
            case "uia.expand":
                SetElementExpanded(step, depth);
                return Signal.Normal;
            case "uia.scrollIntoView":
                ScrollElementIntoView(step, depth);
                return Signal.Normal;
            case "uia.readTable":
                ReadElementTable(step, depth);
                return Signal.Normal;
            case "uia.focusWindow":
                FocusWindow(step, depth);
                return Signal.Normal;

            // --------------------------------------------------------------- browser
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }
}
