using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests.Bench;

/// <summary>Where one action's step has to sit to mean anything: a loop body or a switch case.</summary>
internal enum BenchWrapper
{
    /// <summary>The step stands on its own.</summary>
    None,

    /// <summary>Inside <c>control.repeat</c>, for the flow words that only read inside a loop.</summary>
    Repeat,

    /// <summary>Inside <c>control.switch</c> as its only case.</summary>
    Switch,

    /// <summary>Inside the catch of a <c>control.try</c> whose body fails.</summary>
    Catch,
}

/// <summary>How the run is expected to end when the action does what it says.</summary>
internal enum BenchEnding
{
    Completed,
    Stopped,
    Failed,
}

/// <summary>
/// One action's step, ready to run, plus everything the bench could not resolve. <see cref="Unfilled"/>
/// is the honest half: a parameter nobody could give a value to is named here rather than left blank.
/// </summary>
internal sealed record BenchFixture(
    string Key,
    ExecutableStep? Step,
    IReadOnlyList<string> Unfilled,
    string? Unreachable,
    BenchWrapper Wrapper,
    BenchEnding Ending,
    IReadOnlyList<ExecutableStep> Prelude)
{
    public static BenchFixture Built(string key, ExecutableStep step, BenchWrapper wrapper,
        BenchEnding ending, IReadOnlyList<string> unfilled, IReadOnlyList<ExecutableStep> prelude)
        => new(key, step, unfilled, null, wrapper, ending, prelude);

    public static BenchFixture Absent(string key, string why)
        => new(key, null, [], why, BenchWrapper.None, BenchEnding.Completed, []);
}

/// <summary>
/// Turns the catalogue into a step: every parameter the action declares gets a value that satisfies
/// it, worked out from the shape, the picker and the name rather than written out 176 times. What
/// cannot be worked out is returned as a hole instead of being quietly left empty, because a bench
/// that skips what it cannot build reads as if everything passed.
/// </summary>
internal static class BenchFixtures
{
    /// <summary>A text file the bench's own device layer holds.</summary>
    public const string TextFile = "bench.txt";

    /// <summary>A second text file, for the actions that copy or move one onto another.</summary>
    public const string OtherTextFile = "bench-copy.txt";

    /// <summary>A JSON file, shaped so a query can walk into it.</summary>
    public const string JsonFile = "bench.json";

    /// <summary>A CSV file with a header, so the reading actions have columns to name.</summary>
    public const string CsvFile = "bench.csv";

    /// <summary>A variables file the load action reads back.</summary>
    public const string VarsFile = "bench-vars.json";

    /// <summary>A folder the bench's own device layer holds.</summary>
    public const string Folder = "bench";

    /// <summary>A picture the bench's own device layer hands back for any path.</summary>
    public const string Picture = "bench.png";

    /// <summary>The selector every element action is aimed at.</summary>
    public const string Selector = "Button[name='Bench']";

    /// <summary>The page selector every browser action is aimed at.</summary>
    public const string PageSelector = "#bench";

    /// <summary>The name of the macro the bench's library offers, for the two call actions.</summary>
    public const string CalledMacro = "bench";

    /// <summary>The one argument that macro declares.</summary>
    public const string CalledArgument = "count";

    /// <summary>The one result that macro declares.</summary>
    public const string CalledResult = "answer";

    /// <summary>A list the actions that read one can take apart.</summary>
    public const string ListName = "benchList";

    /// <summary>A list of rows, for the action that writes one out as CSV.</summary>
    public const string RowsName = "benchRows";

    /// <summary>A name the global actions work on.</summary>
    public const string GlobalName = "benchGlobal";

    /// <summary>A running program the process actions can find.</summary>
    public const int ProcessId = 1001;

    /// <summary>The program name that process answers to.</summary>
    public const string ProgramName = "bench";

    /// <summary>The window every window rule names; the bench's device layer has one called this.</summary>
    public static readonly WindowRule BenchWindow = new(Title: "Bench", Process: ProgramName, ClassName: "Bench");

    /// <summary>The rule as the file writes it, which is what a window parameter holds.</summary>
    public static string WindowText => BenchWindow.ToJson().ToJsonString();

    /// <summary>
    /// Actions that need something else running first — a handle from a background call, a key
    /// somebody presses. They are named here rather than guessed at, so the report says "not
    /// covered" instead of "passed".
    /// </summary>
    private static readonly Dictionary<string, string> NeedsContext = new(StringComparer.Ordinal)
    {
    };

    /// <summary>
    /// What a parameter of this name holds when the catalogue wrote no default: the value the name
    /// itself asks for, so the step is aimed at the bench's own file, window, element or page.
    /// </summary>
    private static readonly Dictionary<string, string> ByName = new(StringComparer.Ordinal)
    {
        ["condition"] = "true",
        ["catchWhen"] = "true",
        ["macro"] = CalledMacro,
        ["uiSelector"] = Selector,
        ["pageSelector"] = PageSelector,
        ["anchorMode"] = "window",
        ["inputMode"] = "background",
        ["key"] = "F5",
        ["keys"] = "Ctrl+S",
        ["keyChord"] = "Enter",
        ["programFile"] = "bench.exe",
        ["commandFile"] = "bench.cmd",
        ["processName"] = ProgramName,
        ["processTarget"] = ProgramName,
        ["environmentName"] = "PATH",
        ["model"] = "bench.onnx",
        ["labelFile"] = TextFile,
        ["queryText"] = "Bench",
        ["switchValue"] = "bench",
        ["url"] = "https://example.com",
        ["urlMatch"] = "example",
        ["scriptFile"] = "bench.ps1",
        ["interpreter"] = "powershell",
        ["extension"] = "ps1",
        ["packages"] = "requests",
        ["name"] = ListName,
        ["items"] = "$" + ListName,
        ["listIndex"] = "1",
        ["item"] = "bench",
        ["runHandle"] = "$" + HandleName,
        ["runHandles"] = "$" + HandleName,
        ["note"] = "bench",
        ["message"] = "bench",
        ["label"] = "bench",
        ["reason"] = "bench",
        ["text"] = "bench",
        ["value"] = "bench",
        ["fileContent"] = "bench",
        ["logLine"] = "bench",
        ["clipboardText"] = "bench",
        ["typedText"] = "bench",
        ["fillText"] = "bench",
        ["valueText"] = "bench",
        ["dataText"] = "bench",
        ["heading"] = "bench",
        ["notificationText"] = "bench",
        ["layout"] = "bench",
        ["fileName"] = TextFile,
        ["pattern"] = "*.txt",
        ["files"] = TextFile,
        ["uploadFiles"] = TextFile,
        ["tabMatch"] = "Bench",
        ["optionValue"] = "bench",
        ["names"] = GlobalName,
        ["separatorText"] = ";",
        ["columns"] = "a,b",
        ["matchColumn"] = "a",
        ["matchValue"] = "1",
        ["rows"] = "$" + RowsName,
        ["header"] = "a,b",
        ["query"] = "a",
        ["writeValue"] = "1",
        ["values"] = "bench",
        ["points"] = "0,0,#FFFFFF",
        ["ignoreColor"] = "#FFFFFF",
        ["colorFilter"] = "#FFFFFF",
        ["fixText"] = "bench=bench",
        ["contentPattern"] = "bench",
        ["expected"] = "bench",
        ["scriptArguments"] = "bench",
        ["python"] = "python",
        ["folder"] = Folder,
        ["to"] = OtherTextFile,
        ["from"] = TextFile,
        ["source"] = TextFile,
        ["path"] = TextFile,
        ["file"] = TextFile,
    };

    /// <summary>
    /// Values that depend on the action rather than the parameter's name: a JSON read points at the
    /// JSON file, the window move needs four sizes, the global actions need the seeded name.
    /// </summary>
    private static readonly Dictionary<(string Action, string Parameter), string> ByAction = new()
    {
        [("file.readJson", "path")] = JsonFile,
        [("file.readJson", "query")] = "a.b",
        [("file.writeJson", "path")] = "bench-write.json",
        [("file.writeJson", "query")] = "a",
        [("file.readCsv", "path")] = CsvFile,
        [("file.writeCsv", "path")] = "bench-write.csv",
        [("file.loadVariables", "path")] = VarsFile,
        [("file.saveVariables", "path")] = "bench-save.json",
        [("data.base64Decode", "dataText")] = "YmVuY2g=",
        [("control.incrementGlobal", "name")] = GlobalName,
        [("window.move", "x")] = "10",
        [("window.move", "y")] = "10",
        [("window.move", "width")] = "320",
        [("window.move", "height")] = "240",
        [("process.waitExit", "id")] = "1001",
        [("vision.capture", "path")] = "bench-capture.png",
        [("vision.findColors", "color")] = "#000000",
        [("input.mouseMoveRelative", "inputMode")] = "foreground",
        [("vision.findColors", "points")] = "0,0,#000000",
        [("vision.captureWindow", "path")] = "bench-capture.png",
    };

    /// <summary>
    /// The actions that need a handle from a background call, and the call that makes one. What the
    /// bench runs is the call and then the step, which is the order a macro author writes.
    /// </summary>
    private static readonly HashSet<string> AfterBackground = new(StringComparer.Ordinal)
    {
        "control.waitMacro", "control.runStatus", "control.cancelMacro",
        "control.waitAll", "control.waitAny",
    };

    /// <summary>The handle a background call hands back, by the name the macro keeps it under.</summary>
    public const string HandleName = "benchRun";

    /// <summary>
    /// Builds the step for one action. A parameter whose value cannot be worked out is reported in
    /// <see cref="BenchFixture.Unfilled"/>, and the step is still built so the report can say which
    /// holes kept it from running.
    /// </summary>
    public static BenchFixture Build(ActionSemantics action)
    {
        if (NeedsContext.TryGetValue(action.Key, out var missing))
        {
            return BenchFixture.Absent(action.Key, missing);
        }

        var unfilled = new List<string>();
        var parameters = new List<ExecutableParameter>();

        foreach (var parameter in action.Parameters)
        {
            if (parameter.Name == "returns")
            {
                continue;
            }

            if (Value(action.Key, parameter, unfilled) is { } one)
            {
                parameters.Add(one);
            }
        }

        // A try with nothing in it catches nothing, so the step that hands over the caught failure
        // is given a body that fails: the only way for the result to exist at all.
        if (action.Key == "control.try")
        {
            parameters.RemoveAll(parameter => parameter.Name == "body");
            parameters.Add(new ExecutableParameter
            {
                Name = "body",
                Steps =
                [
                    new ExecutableStep
                    {
                        Type = "control.fail",
                        Parameters = [new ExecutableParameter { Name = "reason", Text = "bench" }],
                    },
                ],
            });
        }

        if (action.Returns.Count > 0)
        {
            parameters.Add(new ExecutableParameter
            {
                Name = "returns",
                Rows = [.. action.Returns.Select(result => new Dictionary<string, string>
                {
                    [FieldNames.Key] = result.Key,
                    [FieldNames.Name] = Name(result.Key),
                })],
            });
        }

        var step = new ExecutableStep
        {
            Type = action.Key,
            Id = "bnch",
            Parameters = [.. parameters],
            Meta = new StepMeta { TimeoutMs = Timeout(action) },
        };

        var prelude = AfterBackground.Contains(action.Key) ? [Background()] : Array.Empty<ExecutableStep>();
        return BenchFixture.Built(action.Key, step, Wrapper(action.Key), Ending(action.Key), unfilled, prelude);
    }

    /// <summary>A background call of the bench's macro, which hands back a handle.</summary>
    private static ExecutableStep Background() => new()
    {
        Type = "control.startMacro",
        Parameters =
        [
            new ExecutableParameter { Name = "macro", Text = CalledMacro },
            new ExecutableParameter { Name = "runHandleName", Text = HandleName },
        ],
    };

    /// <summary>The name a result is kept under while the bench looks at it.</summary>
    public static string Name(string key) => "bench_" + key;

    /// <summary>How long one step may take: short, because the point is whether it ends at all.</summary>
    private static int Timeout(ActionSemantics action)
        => action.TimeoutSource == ActionTimeoutSource.Step ? Math.Min(action.StepTimeoutMs, 600) : 600;

    /// <summary>Where these steps only read inside something else.</summary>
    private static BenchWrapper Wrapper(string key) => key switch
    {
        "control.break" or "control.continue" or "control.nextIteration" => BenchWrapper.Repeat,
        "control.case" => BenchWrapper.Switch,
        "control.rethrow" => BenchWrapper.Catch,
        _ => BenchWrapper.None,
    };

    /// <summary>How a run that does what the action says ends.</summary>
    private static BenchEnding Ending(string key) => key switch
    {
        "control.stop" => BenchEnding.Stopped,
        "control.fail" or "control.rethrow" => BenchEnding.Failed,
        _ => BenchEnding.Completed,
    };

    /// <summary>One parameter, or null when there is no value to give it.</summary>
    private static ExecutableParameter? Value(string action, ActionParameterSemantics parameter,
        List<string> unfilled)
    {
        if (ByAction.TryGetValue((action, parameter.Name), out var chosen))
        {
            return new ExecutableParameter { Name = parameter.Name, Text = chosen };
        }

        if (parameter.Shape is not (ActionValueShape.Rows or ActionValueShape.Steps)
            && ByName.TryGetValue(parameter.Name, out var named))
        {
            return new ExecutableParameter { Name = parameter.Name, Text = named };
        }

        switch (parameter.Shape)
        {
            case ActionValueShape.Steps:
                return new ExecutableParameter { Name = parameter.Name, Steps = [] };

            case ActionValueShape.Rows:
                return new ExecutableParameter { Name = parameter.Name, Rows = [Row(parameter)] };

            case ActionValueShape.Flag:
                return new ExecutableParameter { Name = parameter.Name, Text = Wrote(parameter) ? parameter.DefaultValue : "false" };

            case ActionValueShape.Number:
                return new ExecutableParameter { Name = parameter.Name, Text = Wrote(parameter) ? parameter.DefaultValue : Number(parameter) };

            case ActionValueShape.Choice:
                return new ExecutableParameter
                {
                    Name = parameter.Name,
                    Text = Wrote(parameter) ? parameter.DefaultValue : parameter.Options.FirstOrDefault() ?? string.Empty,
                };

            case ActionValueShape.WindowRule:
                return new ExecutableParameter { Name = parameter.Name, Text = Wrote(parameter) ? parameter.DefaultValue : WindowText };

            case ActionValueShape.Image:
                return new ExecutableParameter { Name = parameter.Name, Text = Picture };

            // A point or a rectangle written in the cell is an alternative to the numbers beside it,
            // not a second way of saying the same thing; the numbers are what a step normally uses.
            case ActionValueShape.Point:
            case ActionValueShape.Rect:
                return parameter.Required
                    ? new ExecutableParameter
                    {
                        Name = parameter.Name,
                        Text = parameter.Shape == ActionValueShape.Point ? "10,10" : "0,0,80,40",
                    }
                    : null;

            case ActionValueShape.List:
            case ActionValueShape.Record:
                return new ExecutableParameter { Name = parameter.Name, Text = "bench" };

            default:
                var text = Text(parameter);
                if (text.Length > 0)
                {
                    return new ExecutableParameter { Name = parameter.Name, Text = text };
                }

                if (parameter.Required)
                {
                    unfilled.Add(parameter.Name);
                }

                return null;
        }
    }

    /// <summary>True when the catalogue already wrote a value this parameter starts with.</summary>
    private static bool Wrote(ActionParameterSemantics parameter) => parameter.DefaultValue.Length > 0;

    /// <summary>A number inside the field's own range, taken from what the field counts.</summary>
    private static string Number(ActionParameterSemantics parameter) => parameter.Unit switch
    {
        ActionUnit.Percent => "50",
        ActionUnit.Axis => "50",
        ActionUnit.Integer => "1",
        ActionUnit.Milliseconds => "1",
        ActionUnit.Count => "1",
        ActionUnit.Pixels => Math.Max(0, (int)parameter.MinimumValue).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "1",
    };

    /// <summary>
    /// The text a parameter holds when the catalogue wrote nothing. A field nobody is required to
    /// answer stays empty unless its name has a meaning here: filling every optional field with a
    /// word would be inventing settings the action never asked for.
    /// </summary>
    private static string Text(ActionParameterSemantics parameter)
    {
        if (Wrote(parameter))
        {
            return parameter.DefaultValue;
        }

        if (ByName.TryGetValue(parameter.Name, out var byName))
        {
            return byName;
        }

        if (!parameter.Required)
        {
            return string.Empty;
        }

        return parameter.Picker switch
        {
            ActionPickerKind.Image => Picture,
            ActionPickerKind.Element => Selector,
            ActionPickerKind.Browser => PageSelector,
            ActionPickerKind.Window or ActionPickerKind.WindowBounds => WindowText,
            ActionPickerKind.Color => "#FFFFFF",
            _ => "bench",
        };
    }

    /// <summary>One row of a row list, with every column the catalogue declares.</summary>
    private static IReadOnlyDictionary<string, string> Row(ActionParameterSemantics parameter)
    {
        switch (parameter.Name)
        {
            // The reference picture a search looks for.
            case "images":
                return new Dictionary<string, string>(StringComparer.Ordinal) { ["image"] = Picture };

            // A place to search in: the four numbers, not a variable name, because a name that was
            // never written is a different failure from "the region is wrong".
            case "region":
                return new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["x"] = "0",
                    ["y"] = "0",
                    ["width"] = "80",
                    ["height"] = "40",
                };

            // A chord per row.
            case "keySequence":
                return new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["keys"] = "F5",
                    ["holdMs"] = "50",
                    ["gapMs"] = "120",
                };

            // The page narrowing: a way to find the page and the value of that way. The way has to
            // be one the engine parses, or the step is refused before it reaches the page.
            case "narrow":
                return new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["how"] = "hasText",
                    ["value"] = "bench",
                };

            // A call's real arguments, one row each. The name has to be one the called macro
            // declares, or the call would be refused as an undeclared argument.
            case "arguments":
                return new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [FieldNames.Name] = CalledArgument,
                    [FieldNames.Value] = "1",
                };

            default:
                return Generic(parameter);
        }
    }

    /// <summary>A row built only from what the columns say they hold.</summary>
    private static IReadOnlyDictionary<string, string> Generic(ActionParameterSemantics parameter)
    {
        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in parameter.Columns)
        {
            if (!column.Required && column.Role is ActionRowColumnRole.Flag)
            {
                continue;
            }

            row[column.Name] = column.Role switch
            {
                ActionRowColumnRole.Number => "1",
                ActionRowColumnRole.Image => Picture,
                ActionRowColumnRole.Flag => "true",
                ActionRowColumnRole.Name => "bench",
                ActionRowColumnRole.Key => "bench",
                _ => "bench",
            };
        }

        return row;
    }
}
