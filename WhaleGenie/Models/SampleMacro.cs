using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// The macros a person is handed when they ask for a sample: a main one that walks through what a
/// macro is made of, and a small one it calls. Every step it really runs works on a machine with
/// nothing set up yet — reading a fact about it, asking about a window, looping, logging, calling a
/// macro — and the two steps that would need something of the user's own (a reference picture and a
/// line of text on screen) come switched off with a note saying what to put there. Whoever opens
/// this has a macro that runs before they have written a step.
/// </summary>
public static class SampleMacro
{
    /// <summary>
    /// The process the sample looks for. It is the same word in every language, and it is a value
    /// rather than a sentence of ours, so it is written here rather than in the string table.
    /// </summary>
    private const string Window = "notepad";

    /// <summary>The two macros, the main one first.</summary>
    public static IReadOnlyList<MacroItem> All()
    {
        var user = Strings.Get("Sample.Variable.User");
        var person = Strings.Get("Sample.Person");

        var sub = new MacroItem { Name = Strings.Get("Sample.Sub.Name") };
        sub.Parameters.Add(new MacroParameter
        {
            Name = person,
            Description = Strings.Get("Sample.Person.Description"),
        });
        sub.Steps.AddRange(
        [
            Note(Strings.Get("Sample.Sub.Note")),
            Step("control.log",
                Field("message", ActionParameterKind.MultilineText,
                    Strings.Format("Sample.Sub.Log", "$" + person))),
        ]);

        var main = new MacroItem { Name = Strings.Get("Sample.Main.Name") };
        main.Steps.AddRange(
        [
            Note(Strings.Get("Sample.Main.Note1")),
            Step("system.info",
                Field("field", ActionParameterKind.Choice, "userName"),
                Returns(("value", user))),
            Note(Strings.Get("Sample.Main.Note2")),
            Step("control.if",
                new StepParameter
                {
                    Name = "condition",
                    Kind = ActionParameterKind.Condition,
                    Condition = Step("condition.windowExists",
                        Field("window", ActionParameterKind.Text, Window),
                        Field("how", ActionParameterKind.Choice, "process")),
                },
                Block("then",
                    Note(Strings.Get("Sample.Main.Then")),
                    Step("window.info",
                        Field("window", ActionParameterKind.Text, Window),
                        Field("how", ActionParameterKind.Choice, "process"),
                        Returns(("window", Strings.Get("Sample.Variable.Window")))),
                    Note(Strings.Get("Sample.Main.Call")),
                    Step("control.runMacro",
                        Field("macro", ActionParameterKind.Text, sub.Name),
                        Field("arg." + person, ActionParameterKind.Text, "$" + user))),
                Block("else", Note(Strings.Get("Sample.Main.Else")))),
            Note(Strings.Get("Sample.Main.Note3")),
            Step("control.repeat",
                Field("times", ActionParameterKind.Number, "2"),
                Block("body",
                    Step("control.log",
                        Field("message", ActionParameterKind.MultilineText,
                            Strings.Get("Sample.Main.Log"))))),
            Note(Strings.Get("Sample.Main.Note4")),
            SwitchedOff(Step("vision.findImage",
                Rows("image", @"C:\images\ok.png"),
                Field("confidence", ActionParameterKind.Number, "90"))),
            SwitchedOff(Step("ocr.findText",
                Field("text", ActionParameterKind.Text, Strings.Get("Sample.Main.FindText")))),
        ]);

        // Both macros are numbered together: the call above runs inside the same run as the macro
        // that made it, and two steps of one run sharing an id would be two steps with one name.
        StepIds.Settle([.. main.Steps, .. sub.Steps]);

        return [main, sub];
    }

    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    private static MacroStep Note(string text)
        => Step("control.note", Field("text", ActionParameterKind.MultilineText, text));

    /// <summary>A step the macro skips until the user has something of their own to put in it.</summary>
    private static MacroStep SwitchedOff(MacroStep step)
    {
        step.Meta = new StepMeta { IsEnabled = false };
        return step;
    }

    private static StepParameter Field(string name, ActionParameterKind kind, string value)
        => new() { Name = name, Kind = kind, Value = value };

    private static StepParameter Block(string name, params MacroStep[] steps)
        => new() { Name = name, Kind = ActionParameterKind.Steps, Steps = [.. steps] };

    private static StepParameter Rows(string name, string value)
        => new()
        {
            Name = name,
            Kind = ActionParameterKind.Images,
            Rows = [StepParameterRow.Of("image", value)],
        };

    /// <summary>
    /// The results a step hands over, written the way the file writes them: which result it is, and
    /// the name the macro keeps it under.
    /// </summary>
    private static StepParameter Returns(params (string Key, string Name)[] rows)
        => new()
        {
            Name = "returns",
            Kind = ActionParameterKind.Returns,
            Rows =
            [
                .. rows.Select(row => new StepParameterRow
                {
                    Columns = new Dictionary<string, string>
                    {
                        ["key"] = row.Key,
                        ["name"] = row.Name,
                    },
                }),
            ],
        };
}
