using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Models;

/// <summary>
/// Every action the editor can build, together with the parameter metadata the
/// add-action dialog renders. Adding an entry here is all it takes for the UI to
/// offer it, show its editors and write its JSON node.
/// </summary>
public static class ActionCatalog
{
    static ActionCatalog()
    {
        // Gives every parameter the key its translated label and hint are looked up by.
        foreach (var definition in Definitions)
        {
            foreach (var parameter in definition.Parameters)
            {
                parameter.OwnerKey = definition.Key;
            }

            foreach (var result in definition.Returns)
            {
                result.OwnerKey = definition.Key;
            }
        }
    }

    private static readonly Geometry ControlIcon =
        Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,7 L12,12 L15,14");

    private static readonly Geometry InputIcon =
        Geometry.Parse("M3,7 H21 V17 H3 Z M6,10 H8 M10,10 H12 M14,10 H16 M18,10 H18.01 M6,14 H10 M12,14 H16");

    private static readonly Geometry VisionIcon =
        Geometry.Parse("M2,12 C6,6 18,6 22,12 C18,18 6,18 2,12 Z M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9 Z");

    private static readonly Geometry OcrIcon =
        Geometry.Parse("M5,4 H19 V20 H5 Z M8,9 H16 M8,12.5 H16 M8,16 H13");

    private static readonly Geometry UiaIcon =
        Geometry.Parse("M3,5 H21 V19 H3 Z M3,9 H21 M6,7 H6.01");

    private static readonly Geometry ScriptIcon =
        Geometry.Parse("M3,5 H21 V19 H3 Z M3,8.5 H21 M6,12 L8.5,14.5 L6,17 M11,17 H15");

    private static readonly Geometry FileIcon =
        Geometry.Parse("M6,3 H14 L18,7 V21 H6 Z M14,3 V7 H18 M9,12 H15 M9,16 H15");

    private static readonly Geometry DataIcon =
        Geometry.Parse("M9,4 L6,12 L9,20 M15,4 L18,12 L15,20");

    private static readonly Geometry BrowserIcon =
        Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M3,9 H21 M3,15 H21 M12,3 C9,7 9,17 12,21");

    private static readonly Geometry ClipboardIcon =
        Geometry.Parse("M6,5 H18 V21 H6 Z M9,3 H15 V6 H9 Z M9,11 H15 M9,15 H13");

    private static readonly Geometry ProcessIcon =
        Geometry.Parse("M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 " +
                       "M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22 " +
                       "M5,5 L7,7 M17,17 L19,19 M19,5 L17,7 M7,17 L5,19");

    private static readonly Geometry SystemIcon =
        Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,10.5 V16.5 M12,7.5 H12.01");

    private static readonly Geometry WindowIcon =
        Geometry.Parse("M3,5 H21 V19 H3 Z M3,9 H21 M6,7 H6.01 M13,12 H18 V16 H13 Z");

    private static readonly Geometry ConditionIcon =
        Geometry.Parse("M12,3 A2.5,2.5 0 1 0 12,8 A2.5,2.5 0 1 0 12,3 M12,8 V12.5 " +
                       "M12,12.5 L7,15.5 M12,12.5 L17,15.5 " +
                       "M7,15.5 A2.5,2.5 0 1 0 7,20.5 A2.5,2.5 0 1 0 7,15.5 " +
                       "M17,15.5 A2.5,2.5 0 1 0 17,20.5 A2.5,2.5 0 1 0 17,15.5");

    /// <summary>Comparison operators offered by <c>condition.compare</c>, written to JSON as-is.</summary>
    private static readonly string[] ComparisonOperators =
    [
        "equals", "notEquals", "greaterThan", "greaterOrEqual", "lessThan", "lessOrEqual",
        "contains", "notContains", "exists", "regexMatch",
    ];

    /// <summary>English fallback text for <see cref="ComparisonOperators"/>; the UI translates it.</summary>
    private static readonly string[] ComparisonOperatorLabels =
    [
        "Equals", "Not equals", "Greater than", "Greater or equal", "Less than", "Less or equal",
        "Contains", "Does not contain", "Exists", "Regex match",
    ];

    /// <summary>
    /// How a switch compares its value against the values of a case. It carries the same kinds of
    /// comparison a condition does, so a many-way branch is not a poorer test than a two-way one,
    /// and it stays one reading per branch: the value on the left is the switch's, the operator
    /// and the value on the right are the branch's.
    /// </summary>
    private static readonly string[] MatchModes =
    [
        "equals", "notEquals", "contains", "startsWith", "endsWith",
        "greaterThan", "greaterOrEqual", "lessThan", "lessOrEqual", "regex",
    ];

    /// <summary>English fallback text for <see cref="MatchModes"/>; the UI translates it.</summary>
    private static readonly string[] MatchModeLabels =
    [
        "Equals", "Not equals", "Contains", "Starts with", "Ends with",
        "Greater than", "Greater or equal", "Less than", "Less or equal", "Regex",
    ];

    /// <summary>Every action offered by the "Select Action" dropdown, grouped by category.</summary>
    public static IReadOnlyList<ActionDefinition> Definitions { get; } =
    [
        // ---------------------------------------------------------------- control
        new()
        {
            Key = "control.sequence",
            Category = ActionCategory.Control,
            DisplayName = "Sequence",
            Description = "Run a list of child steps in order.",
            Parameters =
            [
                Text("name", "Name",
                    hint: "Optional label shown in the step list; leaving it empty shows the "
                          + "action's own name.", required: false),
                Steps("steps", "Steps", "Child steps that run in order."),
            ],
        },
        new()
        {
            Key = "control.delay",
            Category = ActionCategory.Control,
            DisplayName = "Delay",
            Description = "Wait a fixed amount of time before the next step runs.",
            Parameters =
            [
                Number("ms", "Wait", 1000, "How long to wait, in milliseconds; 1000 by default."),
            ],
        },
        new()
        {
            Key = "control.delayRandom",
            Category = ActionCategory.Control,
            DisplayName = "Random Delay",
            Description = "Wait a random amount of time so the macro looks less mechanical.",
            Parameters =
            [
                Number("minMs", "Shortest wait", 400,
                    "Shortest wait, in milliseconds; 400 by default."),
                Number("maxMs", "Longest wait", 1200,
                    "Longest wait, in milliseconds; 1200 by default, and no shorter than the "
                    + "shortest wait. Each run picks a number between the two and waits that long."),
            ],
        },
        new()
        {
            Key = "control.waitUntil",
            Returns =
            [
                Return("elapsed", "Waited ms", primary: true, type: ActionReturnType.Number,
                    hint: "How long the wait actually lasted, in milliseconds."),
            ],
            Category = ActionCategory.Control,
            DisplayName = "Wait Until",
            Description = "Wait until a condition holds, then carry on.",
            StepTimeoutMs = 10000,
            Parameters =
            [
                Condition("condition", "Condition", "Asked again and again until it holds. "
                    + "Any condition can be waited on: a colour, a picture, a piece of text, an "
                    + "element on screen, or a comparison between variables."),
                Number("pollMs", "Check every", 200,
                    "How long to wait between two checks, in milliseconds; 200 by default, and "
                    + "anywhere from 10 to 60000.", min: 10m, max: 60000m, advanced: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "control.repeat",
            Category = ActionCategory.Control,
            DisplayName = "Repeat",
            Description = "Run child steps a fixed number of times, for when you already know how "
                + "many rounds it takes.",
            Parameters =
            [
                Number("times", "Times", 10, "How many iterations to run, from 1 up; 10 by default.",
                    min: 1),
                Number("intervalMs", "Interval", 0,
                    "How long to wait between iterations, in milliseconds; 0 by default.",
                    advanced: true),
                Steps("body", "Body steps", "Steps that run once per iteration."),
            ],
        },
        new()
        {
            Key = "control.while",
            Category = ActionCategory.Control,
            DisplayName = "While",
            Description = "Repeat child steps while a condition stays true, for when only the state "
                + "decides when to stop.",
            Parameters =
            [
                Condition("condition", "Condition",
                    "Checked before every iteration; the round runs only while it holds."),
                Number("maxIterations", "Max iterations", 1000,
                    "Most rounds this loop runs, from 1 up; 1000 by default. Reaching it ends the "
                    + "loop rather than failing, which is what keeps a condition that never goes "
                    + "false from running on.", min: 1),
                Steps("body", "Body steps", "Steps that run while the condition holds."),
            ],
        },
        new()
        {
            Key = "control.forEach",
            Category = ActionCategory.Control,
            DisplayName = "For Each",
            Description = "Run child steps once per item in a list or variable, for a batch of "
                + "values that each want the same handling.",
            Parameters =
            [
                Expression("items", "Items", "$names",
                    "A list, or a $variable holding one. For example [1, 2, 3], split($text, \",\") "
                    + "or $names."),
                // Naming a variable rather than putting a value in: the counter of "Count From To"
                // asks the same question, so it is asked here the same way — a box that offers the
                // names rather than one that makes the name be spelled out from memory.
                Variable("itemVariable", "Item variable", "item",
                    "Variable that receives the current item; item by default.",
                    namesVariable: true, defaultValue: "item"),
                Variable("indexVariable", "Index variable", "n",
                    "Optional variable that receives which round this is, counted from 0 in "
                    + "the order the items are walked in. Leave it empty to keep the round "
                    + "number out of the variables.",
                    required: false, namesVariable: true),
                Toggle("reverse", "Reverse order", false,
                    "Iterate from the last item to the first; off by default.", advanced: true),
                Steps("body", "Body steps", "Steps that run once per item."),
            ],
        },
        new()
        {
            Key = "control.for",
            Category = ActionCategory.Control,
            DisplayName = "Count From To",
            Description = "Count from one number to another, running the child steps each time, for "
                + "when the steps need the number of the round they are on.",
            Parameters =
            [
                Number("from", "From", 1,
                    "The first value the counter takes; 1 by default, and no less than -1000000.",
                    min: -1000000),
                Number("to", "To", 5,
                    "The last value the counter takes; 5 by default, and the value itself is "
                    + "included, so counting 1 to 3 runs three times.", min: -1000000),
                Number("step", "Step", 1,
                    "What the counter is increased by each round; 1 by default. A negative step "
                    + "counts down; 0 works the direction out from the two ends, counting down when "
                    + "From is above To.", min: -1000000, advanced: true),
                Number("intervalMs", "Interval", 0,
                    "How long to wait between rounds, in milliseconds; 0 by default.", advanced: true),
                Variable("variable", "Counter variable", "i",
                    "Variable that receives the value of this round; i by default, and the child "
                    + "steps can read it.",
                    namesVariable: true, defaultValue: "i"),
                Steps("body", "Body steps", "Steps that run once per round."),
            ],
        },
        new()
        {
            Key = "control.if",
            Category = ActionCategory.Control,
            DisplayName = "If",
            Description = "Run one of two child blocks depending on a condition.",
            Parameters =
            [
                Condition("condition", "Condition",
                    "Checked once when the step runs: true runs the Then steps, false runs the "
                    + "Else steps."),
                Steps("then", "Then steps", "Steps to run when the condition is true."),
                Steps("else", "Else steps", "Steps to run when the condition is false."),
            ],
        },
        new()
        {
            Key = "control.switch",
            Category = ActionCategory.Control,
            DisplayName = "Switch",
            Description = "Run the one case whose values match, and the otherwise steps when none do.",
            Parameters =
            [
                // The value is an operand rather than a value that has to be an expression: it may
                // be a name, a number or text of the user's own, so it is read from the variable
                // list rather than being checked as a formula. A formula still works there, which
                // is what the expression builder writes into it.
                Variable("value", "Value (operand)", "count",
                    "What the cases are compared against, read once when the step runs. Pick a "
                    + "variable from the list or type a name, a number or text of your own.",
                    acceptsFormula: true),
                Choice("matchMode", "Match mode (operator)", MatchModes, "equals",
                    "How a case's values are compared with the value.",
                    labels: MatchModeLabels),
                CaseList("cases", "Cases",
                    "One entry per case, tried from the top down. The first match runs and the "
                    + "rest are left alone."),
                Steps("otherwise", "Otherwise steps", "Steps to run when no case matched."),
            ],
        },
        new()
        {
            Key = "control.case",
            Category = ActionCategory.Control,
            DisplayName = "Case",
            Description = "One branch of a switch: the values it answers to and the steps to run.",
            Hidden = true,
            Parameters =
            [
                // The other half of the comparison the switch names. It is an operand too, so the
                // two sides of a case read the same way and neither has to be spelled from memory.
                Variable("values", "Values (operand)", "a; b; c",
                    "Values this case answers to, picked from the variable list or typed in, "
                    + "separated by \";\". Each one is compared with the switch's value the way "
                    + "its match mode says."),
                Steps("body", "Case steps", "Steps that run when this case matches."),
            ],
        },
        new()
        {
            Key = "control.try",
            Returns =
            [
                Return("error", "Error", primary: true,
                    hint: "Why the attempt failed, or empty when it did not."),
            ],
            Category = ActionCategory.Control,
            DisplayName = "Try",
            Description = "Attempt some steps, handle a failure in another block, and always "
                + "run a third.",
            Parameters =
            [
                Steps("body", "Try steps", "Steps to attempt."),
                Steps("catch", "Catch steps", "Steps to run when the attempt fails."),
                Steps("finally", "Finally steps", "Steps that always run, however it ended."),
                Returns(),
            ],
        },
        new()
        {
            Key = "control.break",
            Category = ActionCategory.Control,
            DisplayName = "Break",
            Description = "Leave the innermost loop immediately.",
        },
        new()
        {
            Key = "control.continue",
            Category = ActionCategory.Control,
            DisplayName = "Continue",
            Description = "Skip the rest of this iteration and start the next one.",
        },
        new()
        {
            Key = "control.stop",
            Category = ActionCategory.Control,
            DisplayName = "Stop",
            Description = "Stop the running macro.",
            Parameters =
            [
                Text("reason", "Reason", hint: "Optional note written to the log.", required: false,
                    acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "control.fail",
            Category = ActionCategory.Control,
            DisplayName = "Fail",
            Description = "Count this step as failed, so the failure rule written on it takes over.",
            Parameters =
            [
                Text("reason", "Reason", hint: "What went wrong, written into the log and into the "
                    + "step's own error value so later steps can read it. Optional.",
                    required: false, acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "control.note",
            Category = ActionCategory.Control,
            DisplayName = "Note",
            Description = "Leave a line of explanation in the middle of a macro; running it does "
                + "nothing.",
            Parameters =
            [
                Multiline("text", "Note", "What this part of the macro is for, written for whoever "
                    + "reads the macro next. It is taken as it stands: no variable is filled in, and "
                    + "nothing is written to the log.", "Set the export window up first."),
            ],
        },
        new()
        {
            Key = "control.setVariable",
            Category = ActionCategory.Control,
            DisplayName = "Set Variable",
            Description = "Store a value that later steps can read.",
            Parameters =
            [
                Variable("name", "Variable name", "count",
                    "Local variables are created here. Global variables must exist already; "
                    + "add them in the Variable Center.",
                    namesVariable: true),
                Choice("scope", "Scope", ["local", "global"], "local",
                    "Which layer the value goes in; local by default. Local values belong to this "
                    + "macro; global values are shared by every macro and must be created in the "
                    + "Variable Center first.",
                    labels: ["Local (this macro)", "Global (shared)"]),
                Expression("value", "Value", "3",
                    "The value to store: a number, text, a $variable, or a formula such as "
                    + "$count + 1 or upper($name)."),
            ],
        },
        new()
        {
            Key = "control.calculate",
            Category = ActionCategory.Control,
            DisplayName = "Calculate",
            Description = "Work out an expression and store the answer in a variable.",
            Parameters =
            [
                Variable("name", "Variable name", "count",
                    "Where the answer is stored. Local variables are created here; global "
                    + "variables must exist already, add them in the Variable Center, and every "
                    + "macro shares them.",
                    namesVariable: true),
                Choice("scope", "Scope", ["local", "global"], "local",
                    "Which layer the value goes in; local by default. Local values belong to this "
                    + "macro; global values are shared by every macro and must be created in the "
                    + "Variable Center first.",
                    labels: ["Local (this macro)", "Global (shared)"]),
                Expression("value", "Expression", "$count + 1",
                    "A formula to work out, such as $count + 1, $total * 0.85 or concat($first, \" \", $last). "
                    + "The expression editor writes it piece by piece."),
            ],
        },
        new()
        {
            Key = "control.listCreate",
            Category = ActionCategory.Control,
            DisplayName = "Create List",
            Description = "Start a list variable, replacing whatever it held before.",
            Parameters =
            [
                Variable("name", "Variable name", "names",
                    "Local lists belong to this macro; global lists are shared by every macro. "
                    + "The name is created here for a local list; a global list must exist already, "
                    + "added in the Variable Center. Lists hold any mixture of values.",
                    namesVariable: true),
                Choice("scope", "Scope", ["local", "global"], "local",
                    "Which layer the list goes in; local by default. Local lists belong to this "
                    + "macro; global lists are shared by every macro and must be created in the "
                    + "Variable Center first.",
                    labels: ["Local (this macro)", "Global (shared)"]),
                Expression("items", "Starting items", "[1, 2, 3]",
                    "What the list starts with, written as a list or a $variable; leaving it empty "
                    + "starts an empty list.", required: false),
            ],
        },
        new()
        {
            Key = "control.listAdd",
            Category = ActionCategory.Control,
            DisplayName = "Add To List",
            Description = "Add one or more values to the end of a list.",
            Parameters =
            [
                Variable("name", "List variable", "names",
                    "The list to add to; the list offers the ones the macro can see.",
                    namesVariable: true),
                Expression("value", "Value", "$item",
                    "Value put at the end of the list; a variable or an expression may be used."),
            ],
        },
        new()
        {
            Key = "control.listInsert",
            Category = ActionCategory.Control,
            DisplayName = "Insert Into List",
            Description = "Put a value into a list at a chosen position.",
            Parameters =
            [
                Variable("name", "List variable", "names",
                    "The list to insert into; the list offers the ones the macro can see.",
                    namesVariable: true),
                Expression("index", "Index", "0",
                    "Position to insert at, counted from 0; 0 is the front. Write a position between "
                    + "0 and the list's length; anything further out is taken as the nearest end."),
                Expression("value", "Value", "$item",
                    "Value inserted at that position; whatever was there moves along one."),
            ],
        },
        new()
        {
            Key = "control.listRemoveAt",
            Category = ActionCategory.Control,
            DisplayName = "Remove From List",
            Description = "Take the item at a chosen position out of a list.",
            Parameters =
            [
                Variable("name", "List variable", "names",
                    "The list to take something out of; the list offers the ones the macro can see.",
                    namesVariable: true),
                Expression("index", "Index", "0",
                    "Position to remove, counted from 0; -1 removes the last item."),
            ],
        },
        new()
        {
            Key = "control.listSet",
            Category = ActionCategory.Control,
            DisplayName = "Change One Item Of A List",
            Description = "Put another value in one position of a list, leaving the rest alone.",
            Parameters =
            [
                Variable("name", "List variable", "names",
                    "The list to change; the list offers the ones the macro can see.",
                    namesVariable: true),
                Expression("index", "Index", "0",
                    "Position to change, counted from 0; -1 is the last item. A position the list "
                    + "has not got stops the step rather than quietly doing nothing."),
                Expression("value", "Value", "$item",
                    "Value put at that position; the rest of the list is left alone."),
            ],
        },
        new()
        {
            Key = "control.listClear",
            Category = ActionCategory.Control,
            DisplayName = "Clear List",
            Description = "Empty a list without removing the variable itself.",
            Parameters =
            [
                Variable("name", "List variable", "names",
                    "The list to empty; the list offers the ones the macro can see. The variable "
                    + "itself stays, it is only what is inside it that goes.",
                    namesVariable: true),
            ],
        },
        new()
        {
            Key = "control.log",
            Category = ActionCategory.Control,
            DisplayName = "Log",
            Description = "Write a message to the WhaleGenie log.",
            Parameters =
            [
                Multiline("message", "Message",
                    "Text written to the log; it may run over several lines, and it may be a "
                    + "variable or an expression.", "Loop finished", acceptsFormula: true),
                Choice("level", "Level", ["info", "warn", "error", "debug"], "info",
                    "How the line is marked in the log; information by default."),
            ],
        },
        new()
        {
            Key = "control.ask",
            Returns =
            [
                Return("confirmed", "Confirmed", primary: true, type: ActionReturnType.Flag,
                    hint: "True when the person said yes, false when they said no, and also false "
                        + "when there was nobody to ask, which is what a run a trigger started gets."),
            ],
            Category = ActionCategory.Control,
            DisplayName = "Ask the Person",
            Description = "Ask a yes-or-no question and go on according to the answer.",
            Parameters =
            [
                Multiline("message", "Question",
                    "The question, written in full. It is shown to whoever is at the machine while "
                    + "the macro waits for an answer.", "Ready to go on?", acceptsFormula: true),
                Multiline("detail", "Detail",
                    "Anything worth adding under the question, such as what the macro is about to "
                    + "do. Left empty, the question stands on its own.", "", required: false,
                    acceptsFormula: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "control.input",
            Returns =
            [
                Return("text", "Answer", primary: true,
                    hint: "What the person wrote. Empty when they cancelled, and when there was "
                        + "nobody to ask."),
            ],
            Category = ActionCategory.Control,
            DisplayName = "Ask for Text",
            Description = "Ask for a line of writing and keep it in a variable.",
            Parameters =
            [
                Multiline("message", "Question",
                    "The question, written in full. It is shown to whoever is at the machine while "
                    + "the macro waits for an answer.", "Order number?", acceptsFormula: true),
                Text("default", "Answer box starts with",
                    "What the answer box holds before the person types anything. It is only a "
                    + "starting point, and stands as the answer when they leave it alone.",
                    "A1001", required: false, acceptsFormula: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "control.runMacro",
            Category = ActionCategory.Control,
            DisplayName = "Run Another Macro",
            Description = "Run another macro in this project, as though its steps had been written "
                + "here. The called macro works on values of its own: this step fills in the "
                + "arguments that macro declares, and asks for whichever of its results it wants.",
            Parameters =
            [
                MacroName("macro", "Macro", "Another macro in this project."),
                Returns(),
            ],
        },

        // ------------------------------------------------------------------- file
        new()
        {
            Key = "file.readText",
            Returns =
            [
                Return("text", "Text", primary: true,
                    hint: "What the file held. With “one item per line” it is a list of lines."),
            ],
            Category = ActionCategory.File,
            DisplayName = "Read Text",
            Description = "Read a text file into a variable.",
            Parameters =
            [
                FilePath("path", "File", "notes.txt", "File to read.", PathIntent.Read),
                Returns(),
                Choice("storeAs", "Store as", ["text", "lines"], "text",
                    "Keep the file as one piece of text, or hand each line over as an item of a "
                    + "list, which is what walking through a file of names or paths wants. One "
                    + "piece of text is the default.",
                    labels: ["One piece of text", "One item per line"], advanced: true),
                Number("limit", "Only the first", 0,
                    "Read only the first this many lines, from 0 to 1000000 and 0 by default. "
                    + "Zero reads the whole file, which a file of lines to pick a few out of does "
                    + "not want.", min: 0m, max: 1_000_000m, advanced: true),
                Toggle("skipBlankLines", "Leave out blank lines", false,
                    "Drop the lines that hold nothing, so a blank line in the middle of a list of "
                    + "names does not become an item. Off by default.", advanced: true),
                Toggle("trim", "Trim each line", false,
                    "Take the spaces off both ends of every line. Off by default.", advanced: true),
                Encoding(),
            ],
        },
        new()
        {
            Key = "file.writeText",
            Category = ActionCategory.File,
            DisplayName = "Write Text",
            Description = "Write text to a file, replacing it or adding to the end.",
            Parameters =
            [
                FilePath("path", "File", "notes.txt", "File to write.", PathIntent.Write),
                Multiline("text", "Text", "Text to write. Read as one value, so a variable or an "
                    + "expression may stand for it.", "", required: false,
                    acceptsFormula: true),
                Choice("mode", "Mode", ["overwrite", "append"], "overwrite",
                    "Replace the file, or add to what is already there. Replacing is the default.",
                    labels: ["Replace it", "Add to the end"]),
                Choice("newline", "Line break", ["none", "end", "start"], "none",
                    "Whether to put a line break around the text: none, one after it, or one in "
                    + "front of it when adding to a file whose last line never ended. None by "
                    + "default.",
                    labels: ["None", "After the text", "In front of the text when it is needed"],
                    advanced: true),
                Encoding(),
            ],
        },
        new()
        {
            Key = "file.appendLog",
            Category = ActionCategory.File,
            DisplayName = "Append to Log",
            Description = "Add one line to a log file, creating the file when it is not there.",
            Parameters =
            [
                FilePath("path", "File", "log.txt", "The log file the line is added to.", PathIntent.Write),
                Text("text", "Line", "Finished the run",
                    "What the line says. It is read as one value, so a variable or an expression "
                    + "may stand for it; a line break ends the line, and adding to a file whose last "
                    + "line never ended writes one in front of it.",
                    acceptsFormula: true),
                Toggle("timestamp", "Put the time first", true,
                    "Write the date and time in front of the line. On by default; a file of lines "
                    + "without them is hard to read afterwards."),
            ],
        },
        new()
        {
            Key = "file.exists",
            Returns =
            [
                Return("exists", "Exists", primary: true, type: ActionReturnType.Flag,
                    hint: "True when the file or folder is there."),
            ],
            Category = ActionCategory.File,
            DisplayName = "File Exists",
            Description = "Check whether a file or folder is there.",
            Parameters =
            [
                FilePath("path", "File", "notes.txt", "File or folder to look for.", PathIntent.Read),
                Returns(),
            ],
        },
        new()
        {
            Key = "file.delete",
            Category = ActionCategory.File,
            DisplayName = "Delete File",
            Description = "Remove a file.",
            Parameters =
            [
                FilePath("path", "File", "old.txt", "File to remove.", PathIntent.Read),
                Toggle("toRecycleBin", "To recycle bin", false,
                    "Put the file in the recycle bin, where it can still be restored, instead of "
                    + "removing it for good. Off by default."),
            ],
        },
        new()
        {
            Key = "file.copy",
            Category = ActionCategory.File,
            DisplayName = "Copy File",
            Description = "Copy a file to another place.",
            Parameters =
            [
                FilePath("from", "From", "report.csv", "File to copy.", PathIntent.Read),
                FilePath("to", "To", @"backup\report.csv", "Where the copy goes.", PathIntent.Write),
                IfExists(),
            ],
        },
        new()
        {
            Key = "file.move",
            Category = ActionCategory.File,
            DisplayName = "Move File",
            Description = "Move a file to another place, or rename it in the same folder.",
            Parameters =
            [
                FilePath("from", "From", "report.csv", "File to move.", PathIntent.Read),
                FilePath("to", "To", @"archive\report.csv", "Where it goes, name and all.", PathIntent.Write),
                IfExists(),
            ],
        },
        new()
        {
            Key = "file.createFolder",
            Category = ActionCategory.File,
            DisplayName = "Create Folder",
            Description = "Make a folder, along with any folders above it that are missing.",
            Parameters =
            [
                FilePath("path", "Folder", @"output\reports", "Folder to make.", PathIntent.Folder),
            ],
        },
        new()
        {
            Key = "file.path",
            Returns =
            [
                Return("path", "Path", primary: true,
                    hint: "The path that was worked out."),
            ],
            Category = ActionCategory.File,
            DisplayName = "Path",
            Description = "Work out part of a path: join, split, or find where it points.",
            Parameters =
            [
                Choice("operation", "Operation",
                    ["combine", "folder", "name", "baseName", "extension", "full", "temp", "macros"],
                    "combine",
                    "What to work out. Combine joins the path and the name beside it; the others "
                    + "read one part off the path, or hand back a folder that is always in the "
                    + "same place. Combine is the default.",
                    labels:
                    [
                        "Join folder and name", "The folder it is in", "The file name",
                        "The file name without its extension", "The extension", "The full path",
                        "The temporary folder", "The macros folder",
                    ]),
                FilePath("path", "Path", @"reports\day.csv",
                    "The path to work on. The two folder answers ignore it.", PathIntent.Read),
                Text("name", "Name", "report.csv", required: false,
                    hint: "The second half of a join. Only \"join folder and name\" uses it."),
                Returns(),
            ],
        },
        new()
        {
            Key = "file.unzip",
            Category = ActionCategory.File,
            DisplayName = "Unzip",
            Description = "Unpack a zip file into a folder.",
            Parameters =
            [
                FilePath("from", "Zip file", "download.zip", "Zip file to unpack.", PathIntent.Read,
                    "*.zip"),
                FilePath("folder", "Into folder", @"unpacked", "Folder the contents come out in.",
                    PathIntent.Folder),
                Toggle("overwrite", "Overwrite", true,
                    "Replace files that are already there. On by default."),
            ],
        },
        new()
        {
            Key = "file.zip",
            Category = ActionCategory.File,
            DisplayName = "Zip Folder",
            Description = "Pack a folder into a zip file.",
            Parameters =
            [
                FilePath("folder", "Folder", @"reports", "Folder to pack.", PathIntent.Folder),
                FilePath("to", "Zip file", "reports.zip",
                    "Zip file to write. One that is already there is replaced.", PathIntent.Write,
                    "*.zip"),
            ],
        },
        new()
        {
            Key = "file.deleteFolder",
            Category = ActionCategory.File,
            DisplayName = "Delete Folder",
            Description = "Remove a folder, and everything inside it when it is asked to.",
            Parameters =
            [
                FilePath("path", "Folder", @"output\old", "Folder to remove.", PathIntent.Folder),
                Toggle("recurse", "Delete what is inside", false,
                    "Delete what is inside the folder along with the folder itself. Off by default; "
                    + "a folder that still holds something is otherwise left alone."),
            ],
        },
        new()
        {
            Key = "file.listFiles",
            Returns =
            [
                Return("files", "Files", primary: true, type: ActionReturnType.List,
                    hint: "The full paths of the files that were collected."),
            ],
            Category = ActionCategory.File,
            DisplayName = "List Files",
            Description = "Collect the files in a folder into a list.",
            Parameters =
            [
                FilePath("folder", "Folder", ".", "Folder to look in.", PathIntent.Folder),
                Text("pattern", "Pattern", "*.txt",
                    "Which files to collect. Wildcards work, and a semi-colon asks for several at "
                    + "once, for example *.txt; report?.xlsx. Empty, which is the default, collects "
                    + "every file.",
                    acceptsVariables: true, required: false),
                Toggle("recurse", "Include subfolders", false,
                    "Look inside the folders under the one named above as well; off by default, "
                    + "which lists that folder alone."),
                Returns(),
                Choice("sortBy", "Order", ["name", "modified", "created", "size", "extension"],
                    "name",
                    "What to put the files in order by, name by default. Files that agree are "
                    + "ordered by name, so the same folder comes out in the same order on every run.",
                    labels: ["Name", "Last modified", "Created", "Size", "Extension"],
                    advanced: true),
                Toggle("descending", "The other way round", false,
                    "Newest, largest or Z to A first instead of the plain order. Off by default.",
                    advanced: true),
                Number("depth", "How deep", 0,
                    "How many folders deep to look while subfolders are included, from 0 to 64 and "
                    + "0 by default. Zero is as deep as they go.", min: 0m, max: 64m,
                    advanced: true),
                Number("withinCount", "Changed in the last", 0,
                    "Keep only the files that changed in the last so many minutes, hours or days, "
                    + "which is how a macro asks for \"what changed today\". From 0 to 100000, 0 by "
                    + "default, and zero keeps every file.",
                    min: 0m, max: 100_000m, advanced: true),
                Choice("withinUnit", "Unit", ["minutes", "hours", "days"], "days",
                    "What the number above counts in. Days by default.",
                    labels: ["Minutes", "Hours", "Days"], advanced: true),
                Number("minKb", "At least this big", 0,
                    "Keep only the files of this many kilobytes or more, from 0 to 1000000; zero, "
                    + "which is the default, keeps every size.",
                    min: 0m, max: 1_000_000m, advanced: true),
                Number("maxKb", "At most this big", 0,
                    "Keep only the files of this many kilobytes or less, from 0 to 1000000; zero, "
                    + "which is the default, keeps every size.",
                    min: 0m, max: 1_000_000m, advanced: true),
                Toggle("relative", "Paths inside the folder", false,
                    "Give each file as the path it has inside the folder that was listed, rather "
                    + "than the whole path on the disk. Off by default.", advanced: true),
            ],
        },
        new()
        {
            Key = "file.readJson",
            Returns =
            [
                Return("value", "Value", primary: true,
                    hint: "The value that was read. Missing reads as empty."),
            ],
            Category = ActionCategory.File,
            DisplayName = "Read JSON",
            Description = "Read a value out of a JSON file.",
            Parameters =
            [
                FilePath("path", "File", "config.json", "JSON file to read.", PathIntent.Read,
                    "*.json"),
                Text("query", "Where", "server.name",
                    "Path to the value, such as server.name or items[0].id. Empty, the "
                    + "default, reads the whole file.", acceptsFormula: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "file.writeJson",
            Category = ActionCategory.File,
            DisplayName = "Write JSON",
            Description = "Set a value in a JSON file, keeping the rest of the file.",
            Parameters =
            [
                FilePath("path", "File", "config.json", "JSON file to update.", PathIntent.Write,
                    "*.json"),
                Text("query", "Where", "server.name",
                    "Path to the value, such as server.name. Missing objects on the way are created.",
                    acceptsFormula: true),
                Text("value", "Value", "", "Value to store, read as one value, so a variable "
                    + "or an expression may stand for it.", required: false,
                    acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "file.readCsv",
            Returns =
            [
                Return("rows", "Rows", primary: true, type: ActionReturnType.List,
                    hint: "The rows that were read, each row a record: its keys are the names in the "
                        + "header row, and field(row, \"金额\") reads one cell by name. A file whose "
                        + "first row is not a header names its columns A, B, C."),
            ],
            Category = ActionCategory.File,
            DisplayName = "Read CSV",
            Description = "Read a CSV file into a list of rows.",
            Parameters =
            [
                FilePath("path", "File", "rows.csv", "CSV file to read.", PathIntent.Read, "*.csv"),
                Toggle("hasHeader", "First row is a header", true,
                    "On by default: leave the first row out of the result and use it to name the "
                    + "columns of every row, which is what lets a cell be read by name."),
                Text("columns", "Only these columns", "金额, 日期", required: false,
                    hint: "The columns to keep, written the way they are named in the header or as "
                        + "the letter they sit under: 金额, or B, or 金额,B. Each row comes back with "
                        + "those columns and no others, in the order written here. Leave empty for "
                        + "every column.",
                    advanced: true),
                Text("matchColumn", "Only rows where", "订单号", required: false,
                    hint: "Read just the rows whose cell in this column is the value below; the "
                        + "column is named the same way. Leave empty to read every row.",
                    advanced: true),
                Text("matchValue", "Is", "A123", required: false,
                    hint: "The value those rows hold. Written out, or named as a variable with "
                        + "$name.",
                    acceptsVariables: true, acceptsFormula: true, advanced: true,
                    onlyWith: [ActionParameter.AnyValue], onlyField: "matchColumn"),
                Returns(),
                Encoding(),
                ..Separator("auto", mayBeAutomatic: true),
                Toggle("skipBlankLines", "Skip blank lines", true,
                    "Drop lines that hold nothing; on by default. A file written by another "
                    + "program often ends with one, and it would otherwise read as a row of one empty "
                    + "cell.",
                    advanced: true),
                Number("startRow", "Start at row", 1,
                    "The line of the file to start at, counted from 1 and 1 by default; the "
                    + "header, when there is one, is this line.", min: 1, advanced: true),
                Number("maxRows", "At most rows", 0,
                    "Read this many rows at most, from 0 to 1000000 and 0 by default. Zero "
                    + "reads every row from there on.", max: 1000000, advanced: true),
                Toggle("trim", "Trim spaces", false,
                    "Drop spaces around each cell, which a file written by hand often has. Off "
                    + "by default.",
                    advanced: true),
                Choice("shape", "Result shape", ["rows", "values"], "rows",
                    "One record per row, or, when exactly one column was named above, the values of "
                    + "that column as one plain list, ready to walk through or add up. A record per "
                    + "row by default.",
                    labels: ["One record per row", "The values of one column"], advanced: true),
                Choice("matchMode", "Matches", MatchModes, "equals",
                    "How the cell is compared against the value: exactly, or parts of it. "
                    + "Exactly by default.",
                    labels: MatchModeLabels, advanced: true),
            ],
        },
        new()
        {
            Key = "file.writeCsv",
            Category = ActionCategory.File,
            DisplayName = "Write CSV",
            Description = "Write a list of rows to a CSV file.",
            Parameters =
            [
                FilePath("path", "File", "rows.csv", "CSV file to write.", PathIntent.Write,
                    "*.csv"),
                Variable("rows", "Rows", "$rows",
                    "A list of rows. Each row may itself be a list of cells.",
                    namesVariable: false, acceptsFormula: true),
                Variable("header", "Header row", "$columns",
                    "Column names to put above the data: a list, or the names written out on one "
                    + "line with the separator between them. Adding to a file that is not there "
                    + "yet writes it; adding to one that already is does not, so a log keeps one "
                    + "header at the top. Empty, which is the default, writes no header row.",
                    required: false),
                Choice("mode", "Mode", ["replace", "append"], "replace",
                    "Write the file from the start, or add these rows below what is already in "
                    + "it. Writing from the start is the default.",
                    labels: ["Replace the file", "Add below the last row"]),
                Toggle("align", "Put each value under the column with that name", false,
                    "Adding to a file that already holds a table: put every cell under the column "
                    + "whose name in the first line is the name this step gives that cell, rather "
                    + "than in the same place across the row. That is what keeps a column the file "
                    + "has and this step knows nothing about from shifting everything after it. The "
                    + "names of this step's cells come from the header row above, and a name the "
                    + "file has not got is reported rather than guessed at.",
                    advanced: true, onlyWith: ["append"], onlyField: "mode"),
                ..Separator("comma"),
                Choice("lineEnding", "Line ending", ["windows", "unix"], "windows",
                    "Which characters end a line: CRLF, which Windows writes, or LF, which "
                    + "everything else does. CRLF, which Windows writes, is the default.",
                    labels: ["Windows (CRLF)", "Unix (LF)"], advanced: true),
                Toggle("quoteAll", "Quote every cell", false,
                    "Put quotes around every cell rather than only around the ones that need them. "
                    + "Some programs insist on it. Off by default.", advanced: true),
                Choice("emptyCells", "Empty cells", ["bare", "quoted"], "bare",
                    "How a cell holding nothing is written: as an empty place between two "
                    + "separators, or as two quotes. Both read back as an empty cell, and the "
                    + "quotes are for the program that wants to see a cell there. Nothing "
                    + "between the separators is the default.",
                    labels: ["Nothing between the separators", "Two quotes"], advanced: true),
                Encoding(),
            ],
        },
        new()
        {
            Key = "file.saveVariables",
            Category = ActionCategory.File,
            DisplayName = "Save Variables",
            Description = "Write variables to a JSON file, so the next run can pick them up.",
            Parameters =
            [
                FilePath("path", "File", "state.json", "File to write.", PathIntent.Write,
                    "*.json"),
                Text("names", "Which", "",
                    "Names to save, separated by commas. Empty, the default, saves every local "
                    + "and shared variable.", required: false),
            ],
        },
        new()
        {
            Key = "file.loadVariables",
            Category = ActionCategory.File,
            DisplayName = "Load Variables",
            Description = "Read variables back from a file saved by Save Variables.",
            Parameters =
            [
                FilePath("path", "File", "state.json", "File to read.", PathIntent.Read, "*.json"),
            ],
        },

        // -------------------------------------------------------------------- data
        new()
        {
            Key = "data.base64Encode",
            Returns =
            [
                Return("encoded", "Encoded", primary: true,
                    hint: "The text written as Base64."),
            ],
            Category = ActionCategory.Data,
            DisplayName = "Encode as Base64",
            Description = "Turn text into Base64, the form a value takes inside a URL or a token.",
            Parameters =
            [
                Text("text", "Text", "hello",
                    "The text to encode. It is read as one value, so a variable or an expression "
                    + "may stand for it; the text is taken as UTF-8.", acceptsFormula: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "data.base64Decode",
            Returns =
            [
                Return("decoded", "Decoded", primary: true,
                    hint: "The text the Base64 held, read as UTF-8."),
            ],
            Category = ActionCategory.Data,
            DisplayName = "Decode from Base64",
            Description = "Turn Base64 back into the text it holds.",
            Parameters =
            [
                Text("text", "Base64", "aGVsbG8=",
                    "The Base64 text to decode. It is read as one value, so a variable or an "
                    + "expression may stand for it; the text it holds is read as UTF-8.",
                    acceptsFormula: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "data.hash",
            Returns =
            [
                Return("digest", "Checksum", primary: true,
                    hint: "The checksum, written in lowercase letters and digits."),
            ],
            Category = ActionCategory.Data,
            DisplayName = "Hash",
            Description = "Work out the checksum of a piece of text or of a file.",
            Parameters =
            [
                Choice("algorithm", "Algorithm", ["md5", "sha1", "sha256", "sha512"], "sha256",
                    "Which checksum to work out. SHA-256 is the ordinary choice; MD5 and SHA-1 are "
                    + "here for matching a value some other program hands out. SHA-256 by default.",
                    labels: ["MD5", "SHA-1", "SHA-256", "SHA-512"]),
                Choice("source", "Work out", ["text", "file"], "text",
                    "What the checksum is worked out over: the text below, or a file on disk. "
                    + "Text by default.",
                    labels: ["A piece of text", "A file"]),
                Text("text", "Text", "hello", "The text to work out the checksum of, read as one "
                    + "value and taken as UTF-8. A variable or an expression may stand for it.",
                    acceptsFormula: true, onlyWith: ["text"], onlyField: "source"),
                FilePath("path", "File", "report.pdf",
                    "File to work out the checksum of. It is read a piece at a time, so the size "
                    + "of the file does not decide whether the step can run.",
                    PathIntent.Read, "*.pdf", onlyWith: ["file"], onlyField: "source"),
                Returns(),
            ],
        },

        // --------------------------------------------------------------- clipboard
        new()
        {
            Key = "clipboard.writeText",
            Category = ActionCategory.Clipboard,
            DisplayName = "Copy to Clipboard",
            Description = "Put text on the clipboard, the same as copying it.",
            Parameters =
            [
                Multiline("text", "Text", "Text to put on the clipboard, read as one value, so a "
                    + "variable or an expression may stand for it.", acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "clipboard.readText",
            Returns =
            [
                Return("text", "Text", primary: true, hint: "The text that was on the clipboard."),
            ],
            Category = ActionCategory.Clipboard,
            DisplayName = "Read Clipboard",
            Description = "Read the text on the clipboard into a variable.",
            Parameters =
            [
                Returns(),
            ],
        },
        new()
        {
            Key = "clipboard.readImage",
            Returns =
            [
                Return("image", "Picture", primary: true, type: ActionReturnType.Image,
                    hint: "The picture, ready for a find-image step to look for on screen. A "
                        + "picture on the clipboard has no place on screen, so there is no position."),
            ],
            Category = ActionCategory.Clipboard,
            DisplayName = "Read Clipboard Picture",
            Description = "Take the picture on the clipboard into an image variable.",
            Parameters =
            [
                Returns(),
            ],
        },
        new()
        {
            Key = "clipboard.writeImage",
            Category = ActionCategory.Clipboard,
            DisplayName = "Copy Picture to Clipboard",
            Description = "Put a picture on the clipboard, the same as copying one.",
            Parameters =
            [
                Image("image", "Picture", @"C:\images\ok.png",
                    "A picture file, or an image variable an earlier step filled in."),
            ],
        },
        new()
        {
            Key = "clipboard.readFiles",
            Returns =
            [
                Return("files", "Files", primary: true, type: ActionReturnType.List,
                    hint: "The full paths of the files on the clipboard. Empty when it holds none."),
            ],
            Category = ActionCategory.Clipboard,
            DisplayName = "Read Clipboard Files",
            Description = "Take the paths of the files on the clipboard into a list.",
            Parameters =
            [
                Returns(),
            ],
        },
        new()
        {
            Key = "clipboard.writeFiles",
            Category = ActionCategory.Clipboard,
            DisplayName = "Copy Files to Clipboard",
            Description = "Put files on the clipboard, so a paste drops the files themselves.",
            Parameters =
            [
                Variable("files", "Files", "$files",
                    "A list of paths, or a single path. A relative one is taken from the macros "
                    + "folder, and every file has to be there, because a paste of a path that "
                    + "points nowhere fails quietly in whatever program receives it.",
                    namesVariable: false, acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "clipboard.clear",
            Category = ActionCategory.Clipboard,
            DisplayName = "Clear Clipboard",
            Description = "Empty the clipboard.",
            Parameters = [],
        },
        new()
        {
            Key = "clipboard.waitChange",
            Returns =
            [
                Return("text", "Text", primary: true, hint: "The new text that was copied."),
            ],
            Category = ActionCategory.Clipboard,
            DisplayName = "Wait for Clipboard",
            Description = "Wait until something new is copied to the clipboard.",
            Parameters =
            [
                Number("timeoutMs", "Timeout", 5000,
                    "How long to wait for the clipboard to change before giving up, in "
                    + "milliseconds; 5000 by default."),
                Returns(),
            ],
        },
        new()
        {
            Key = "clipboard.copy",
            Returns =
            [
                Return("text", "Text", primary: true, hint: "What the copy put on the clipboard."),
            ],
            Category = ActionCategory.Clipboard,
            DisplayName = "Copy Selection",
            Description = "Press the copy shortcut, then keep what landed on the clipboard.",
            Parameters =
            [
                Text("keys", "Shortcut", "Ctrl+C",
                    "Keys joined with + that copy the selection; Ctrl+C by default.",
                    required: true, defaultValue: "Ctrl+C"),
                Number("timeoutMs", "Timeout", 1500,
                    "How long to wait for the clipboard to change after the shortcut, in milliseconds; "
                    + "1500 by default."),
                Returns(),
            ],
        },
        new()
        {
            Key = "clipboard.paste",
            Category = ActionCategory.Clipboard,
            DisplayName = "Paste",
            Description = "Put text on the clipboard and press the paste shortcut.",
            Parameters =
            [
                Multiline("text", "Text", "Text to paste. Empty, which is the default, pastes what "
                    + "is already on the clipboard.", required: false, acceptsFormula: true),
                Text("keys", "Shortcut", "Ctrl+V",
                    "Keys joined with + that paste; Ctrl+V by default.",
                    required: true, defaultValue: "Ctrl+V", advanced: true),
            ],
        },

        // ---------------------------------------------------------------- process
        new()
        {
            Key = "process.start",
            Returns =
            [
                Return("processId", "Process id", primary: true, type: ActionReturnType.Number,
                    hint: "The id of the program that was started, which the wait and stop steps "
                        + "take."),
            ],
            Category = ActionCategory.Process,
            DisplayName = "Start Program",
            Description = "Start another program, optionally without showing its window.",
            Parameters =
            [
                PickablePath("file", "Program", "notepad.exe", "The program to start, such as notepad.exe "
                    + "or a full path to it.",
                    PathIntent.Read, "*.exe;*.com;*.bat;*.cmd"),
                Text("arguments", "Arguments", "", "What to pass to the program, read as one value. "
                    + "Empty, which is the default, passes nothing.", required: false,
                    acceptsFormula: true),
                PickablePath("workingDirectory", "Working folder", "",
                    "Folder to start the program in. Empty, which is the default, leaves it in the "
                    + "folder WhaleGenie itself was started in.", PathIntent.Folder, required: false),
                Toggle("hidden", "Hidden window", true,
                    "Start the program without showing its window; on by default. An installer "
                    + "that has to be clicked through wants this off.", advanced: true),
                Toggle("runAsAdmin", "Run as administrator", false,
                    "Ask Windows to start the program with administrator rights; off by default. "
                    + "Windows shows its own approval prompt, and refusing it fails the step. A program "
                    + "started elevated cannot be kept off the screen.", advanced: true),
                Multiline("environment", "Environment variables",
                    "One NAME=value per line for the program to start with. Empty, which is the "
                    + "default, starts it with WhaleGenie's own environment. Lines starting with # are "
                    + "skipped. Giving a program its own environment means naming the program "
                    + "itself, not a document or a shortcut.",
                    "LANG=zh_CN.UTF-8", required: false, advanced: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "process.waitFor",
            Returns =
            [
                Return("process", "Process id", primary: true, type: ActionReturnType.Number,
                    hint: "The id of the program that turned up."),
            ],
            Category = ActionCategory.Process,
            DisplayName = "Wait for Program",
            Description = "Wait until a program is running.",
            Parameters =
            [
                Text("name", "Program", "notepad", "Name of the program to watch for, matched in full "
                    + "and without regard to case, with the .exe left off or on as one likes.",
                    acceptsFormula: true),
                Number("timeoutMs", "Timeout", 10000,
                    "How long to wait for the program to turn up, in milliseconds; 10000 by "
                    + "default."),
                Returns(),
            ],
        },
        new()
        {
            Key = "process.waitExit",
            Returns =
            [
                Return("exitCode", "Exit code", primary: true, type: ActionReturnType.Number,
                    hint: "The code the program ended with; 0 usually means it was happy."),
            ],
            Category = ActionCategory.Process,
            DisplayName = "Wait for Exit",
            Description = "Wait until a program started by this macro has finished.",
            Parameters =
            [
                Text("id", "Process id", "$processId",
                    "The process to watch, usually the one Start Program gave back. Read as one "
                    + "value, so a variable or an expression may stand for it.",
                    acceptsFormula: true),
                Number("timeoutMs", "Timeout", 60000,
                    "How long to wait for the program to end, in milliseconds; 60000 by default."),
                Returns(),
            ],
        },
        new()
        {
            Key = "process.exists",
            Returns =
            [
                Return("running", "Running", primary: true, type: ActionReturnType.Flag,
                    hint: "True when the program is running."),
            ],
            Category = ActionCategory.Process,
            DisplayName = "Program Running",
            Description = "Check whether a program is running.",
            Parameters =
            [
                Text("name", "Program", "notepad", "Name of the program to look for, matched in full and "
                    + "without regard to case, with the .exe left off or on as one likes.",
                    acceptsFormula: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "process.list",
            Returns =
            [
                Return("processes", "Programs", primary: true, type: ActionReturnType.List,
                    hint: "The names of the programs that are running."),
            ],
            Category = ActionCategory.Process,
            DisplayName = "List Programs",
            Description = "Collect the names of the running programs into a list.",
            Parameters =
            [
                Returns(),
            ],
        },
        new()
        {
            Key = "process.info",
            Returns =
            [
                Return("program", "Program", primary: true, type: ActionReturnType.Record,
                    hint: "The program as one record: info is its file, empty when Windows will not "
                        + "say it; then id, name, memoryMb (the memory it holds, in megabytes) and "
                        + "cpuSeconds, the processor time it has used. Read one part of it with "
                        + "field($name, \"id\")."),
            ],
            Category = ActionCategory.Process,
            DisplayName = "Program Details",
            Description = "Read what is known about a running program.",
            Parameters =
            [
                Text("target", "Program", "notepad", "Name of the program, or a process id. A name is "
                    + "matched in full and without regard to case, with the .exe left off or on as one "
                    + "likes.", acceptsFormula: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "process.kill",
            Returns =
            [
                Return("stopped", "Closed", primary: true, type: ActionReturnType.Number,
                    hint: "How many programs were closed."),
            ],
            Category = ActionCategory.Process,
            DisplayName = "Stop Program",
            Description = "Close a program, asking it to exit first unless told to be firm.",
            Parameters =
            [
                Text("target", "Program", "notepad", "Name of the program, or a process id. A name is "
                    + "matched in full and without regard to case, with the .exe left off or on as one "
                    + "likes.", acceptsFormula: true),
                Toggle("force", "Force close", false,
                    "Close it at once instead of asking it to exit. Off by default."),
                Returns(),
            ],
        },
        new()
        {
            Key = "command.run",
            Returns =
            [
                Return("output", "Output", primary: true,
                    hint: "What the command printed on its standard output."),
                Return("error", "Error output",
                    hint: "What the command printed on its error stream."),
                Return("exitCode", "Exit code", type: ActionReturnType.Number,
                    hint: "The code the command ended with; 0 usually means it succeeded."),
            ],
            Category = ActionCategory.Process,
            DisplayName = "Run Command",
            Description = "Run a command line and collect what it printed.",
            Parameters =
            [
                PickablePath("file", "Command", "cmd.exe", "The program to run, such as cmd.exe or a full "
                    + "path to it.",
                    PathIntent.Read, "*.exe;*.com;*.bat;*.cmd"),
                Text("arguments", "Arguments", "/c dir",
                    "What to pass to the command, read as one value. Empty, which is the "
                    + "default, passes nothing.", required: false, acceptsFormula: true),
                PickablePath("workingDirectory", "Working folder", "",
                    "Folder to run the command in. Empty, which is the default, leaves it in the "
                    + "folder WhaleGenie itself was started in.", PathIntent.Folder, required: false),
                Multiline("environment", "Environment variables",
                    "One NAME=value per line for the command to run with. Empty, which is the "
                    + "default, runs it with WhaleGenie's own environment. Lines starting with # are "
                    + "skipped.",
                    "LANG=zh_CN.UTF-8", required: false, advanced: true),
                Multiline("standardInput", "Standard input",
                    "What the command reads on its standard input. Empty, which is the default, "
                    + "gives it nothing to read. Sent as UTF-8, and $name is replaced by what that "
                    + "variable holds.",
                    "first line\nsecond line", required: false, advanced: true),
                Number("timeoutMs", "Timeout", 30000,
                    "How long the command may run, in milliseconds; 30000 by default. The step "
                    + "fails when it runs past that."),
                Toggle("streamOutput", "Write output to the log as it arrives", false,
                    "Put every line the command prints into the run log while it is still running, "
                    + "which is how a long build or script can be watched. The output result still "
                    + "holds everything the command printed. Off by default.", advanced: true),
                Choice("outputEncoding", "Output encoding", ["system", "utf8", "utf16"], "system",
                    "The code page the command prints in; this machine's own by default. That is "
                    + "what the programs Windows ships with, cmd.exe, Windows PowerShell and Python, "
                    + "write in when they are not talking to a screen. A program that prints UTF-8 "
                    + "whichever machine it is on, Node.js for one, has to be read that way instead.",
                    labels: ["This machine's", "UTF-8", "UTF-16"], advanced: true),
                Returns(),
            ],
        },

        // ----------------------------------------------------------------- system
        new()
        {
            Key = "system.info",
            Returns =
            [
                Return("value", "Value", primary: true, hint: "The fact that was read."),
            ],
            Category = ActionCategory.System,
            DisplayName = "System Info",
            Description = "Read a fact about this machine, such as the user name or a folder.",
            Parameters =
            [
                Choice("field", "Field",
                    [
                        "userName", "machineName", "userDomain", "osVersion", "processorCount",
                        "systemFolder", "tempFolder", "userFolder", "desktopFolder",
                        "documentsFolder", "downloadsFolder", "startupFolder", "appDataFolder",
                        "programFiles", "networkConnected", "networkNames", "localIp",
                        "batteryPercent", "onAcPower", "dpi",
                        "dpiPercent", "uptimeSeconds",
                    ],
                    "userName", "Which fact to read; the user name by default.",
                    labels:
                    [
                        "User name", "Computer name", "Domain", "Windows version",
                        "Processor count", "Windows folder", "Temp folder", "User folder",
                        "Desktop", "Documents", "Downloads", "Startup folder", "App data",
                        "Program Files", "Network connected", "Network names", "Local address",
                        "Battery %", "On mains power",
                        "DPI", "DPI percent", "Seconds since start",
                    ]),
                Returns(),
            ],
        },
        new()
        {
            Key = "screen.list",
            Returns =
            [
                Return("displays", "Displays", primary: true, type: ActionReturnType.List,
                    hint: "One record to a display, in the order Windows lists them: x and y, the "
                        + "middle of the display, then left, top, width, height, right, bottom, "
                        + "primary, and box, the rectangle written as left,top,width,height, which "
                        + "a region field takes as it stands. A display to the left of the primary "
                        + "one starts at a negative left, which is what a step aiming at it needs. "
                        + "Read one part of one of them with field(get($name, 0), \"left\"), and walk "
                        + "them all with forEach."),
            ],
            Category = ActionCategory.System,
            DisplayName = "List Displays",
            Description = "Read how the desk is laid out: every display and the rectangle it holds.",
            Parameters =
            [
                Returns(),
            ],
        },
        new()
        {
            Key = "system.environment",
            Returns =
            [
                Return("value", "Value", primary: true,
                    hint: "What the environment variable holds; empty when it is not set."),
            ],
            Category = ActionCategory.System,
            DisplayName = "Environment Variable",
            Description = "Read an environment variable.",
            Parameters =
            [
                Text("name", "Name", "TEMP",
                    "Name of the environment variable, without regard to case. Read as one value, "
                    + "so a variable or an expression may stand for it.", acceptsFormula: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "system.power",
            Category = ActionCategory.System,
            DisplayName = "Power",
            Description = "Lock the screen, sleep, sign out, restart, or shut the machine down.",
            Parameters =
            [
                Choice("what", "Do what",
                    ["lock", "monitorOff", "signOut", "sleep", "hibernate", "restart", "shutDown",
                        "abortShutdown"],
                    "lock",
                    "Which of the things the Start menu's power button does; locking the screen by "
                    + "default. Locking the screen and turning the monitor off cost nothing. Signing "
                    + "out, restarting and shutting down close what is open, so anything unsaved goes "
                    + "with it.",
                    labels:
                    [
                        "Lock the screen", "Turn the monitor off", "Sign out", "Sleep", "Hibernate",
                        "Restart", "Shut down", "Cancel a countdown",
                    ]),
                Number("graceSeconds", "Warning seconds", 0,
                    "How much warning Windows gives before restarting or shutting down, from 0 to "
                    + "600 and 0 by default: a countdown appears on screen and the shutdown can still "
                    + "be called off with \"Cancel a countdown\" or shutdown /a. Zero means at once. "
                    + "The rest of the answers ignore it.",
                    max: 600),
            ],
        },
        new()
        {
            Key = "system.volume",
            Returns =
            [
                Return("volume", "Volume", primary: true, type: ActionReturnType.Number,
                    hint: "The level the machine is left at, from 0 to 100. A step that only mutes "
                        + "still reports the level it is waiting at."),
            ],
            Category = ActionCategory.System,
            DisplayName = "Volume",
            Description = "Read or change the volume of the speakers Windows is using.",
            Parameters =
            [
                Choice("what", "Do what",
                    ["get", "set", "up", "down", "mute", "unmute", "toggleMute"], "get",
                    "Whether to read the volume, set it to a level, move it up or down by a few "
                    + "points, or switch the sound off and on; reading it by default. This is the "
                    + "machine's own volume, which is not the same as the volume keys, since those turn "
                    + "up whichever program has the focus.",
                    labels:
                    [
                        "Read it", "Set the level", "Turn it up", "Turn it down", "Mute",
                        "Unmute", "Mute or unmute",
                    ]),
                Number("percent", "Level %", 50,
                    "The level to set, from 0 to 100 and 50 by default. Used by \"Set the level\".",
                    max: 100,
                    onlyWith: ["set"], onlyField: "what"),
                Number("stepPercent", "By how much", 5,
                    "How far to move the volume, in points; from 1 to 100 and 5 by default. Used "
                    + "by \"Turn it up\" and \"Turn it down\", which never go past 0 or 100.",
                    min: 1, max: 100, onlyWith: ["up", "down"], onlyField: "what"),
                Returns(),
            ],
        },
        new()
        {
            Key = "system.sound",
            Category = ActionCategory.System,
            DisplayName = "Sound",
            Description = "Play one of the sounds this machine plays for an event.",
            Parameters =
            [
                Choice("what", "Play which sound",
                    ["default", "information", "warning", "error", "question"], "default",
                    "Which of the machine's own sounds to play; the default one by default. A macro "
                    + "that has finished, or gone wrong, can say so out loud without putting anything "
                    + "on the screen. Which sound each of these is follows the machine's own sound "
                    + "settings: a machine told to keep quiet plays nothing and the step is done either "
                    + "way, while one with no sound device at all fails the step and says why.",
                    labels:
                    ["The default one", "Notice", "Warning", "Error", "Question"]),
            ],
        },
        new()
        {
            Key = "system.notify",
            Category = ActionCategory.System,
            DisplayName = "Notification",
            Description = "Show a notification beside the notification area.",
            Parameters =
            [
                Text("heading", "Title", "Backup finished",
                    "The heading of the notification. Empty, which is the default, heads it with "
                    + "the program's own name.", required: false, acceptsFormula: true),
                Text("message", "Message", "All the files were copied.",
                    "What the notification says. Variables in it are filled in the way they are "
                    + "everywhere else, so a macro can report what it just did.",
                    required: true, acceptsFormula: true),
                Choice("what", "Kind", ["information", "warning", "error"], "information",
                    "How the notification is marked: an ordinary note, something worth looking at, "
                    + "or something that went wrong; an ordinary note by default. A machine with its "
                    + "notifications switched off shows nothing and the step is done either way.",
                    labels: ["Note", "Warning", "Error"]),
            ],
        },
        new()
        {
            Key = "system.ime",
            Returns =
            [
                Return("ime", "Input method", primary: true,
                    hint: "Which layout is in use, or the list of layouts that could be switched to."),
            ],
            Category = ActionCategory.System,
            DisplayName = "Input Method",
            Description = "Read or change the keyboard layout and input method of the window in "
                + "front.",
            Parameters =
            [
                Choice("what", "Do what",
                    ["get", "list", "switch"], "get",
                    "Whether to read which keyboard layout the window in front is typing in, list "
                    + "the installed ones, or give it another; reading it by default. A macro that types "
                    + "Latin keys while a Chinese layout is in use gets Chinese candidates instead of "
                    + "its shortcut, so switching to the English layout first is often what makes a "
                    + "macro work. Turning a Chinese input method's own Chinese/English switch off is a "
                    + "key press rather than a setting, since Windows keeps that state inside the "
                    + "program being typed into, so that part is done by sending Ctrl+Space or Shift.",
                    labels:
                    ["Which layout is in use", "List the installed layouts", "Switch to another layout"]),
                Text("layout", "Layout", "英语(美国)", required: false,
                    hint: "Which layout to switch to, used by \"Switch to another layout\". Part of "
                          + "the name is enough; \"List the installed layouts\" writes out the names "
                          + "to choose from. The step waits for the window to really change over, so "
                          + "the step after it can rely on typing in that language.",
                    acceptsFormula: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "system.brightness",
            Returns =
            [
                Return("brightness", "Brightness", primary: true, type: ActionReturnType.Number,
                    hint: "The brightness the screens are left at, from 0 to 100."),
            ],
            Category = ActionCategory.System,
            DisplayName = "Brightness",
            Description = "Read or change how bright the screens are.",
            Parameters =
            [
                Choice("what", "Do what", ["get", "set", "up", "down"], "get",
                    "Whether to read the brightness, set it to a level, or move it up or down a few "
                    + "points; reading it by default. Every screen that answers is changed together, "
                    + "because this talks to the screen itself over its video cable the way the "
                    + "brightness buttons on a monitor do, so a laptop's own panel or a screen that "
                    + "does not carry the setting may not answer at all.",
                    labels: ["Read it", "Set the level", "Turn it up", "Turn it down"]),
                Number("percent", "Level %", 50,
                    "The level to set, from 0 to 100 and 50 by default. Used by \"Set the level\".",
                    max: 100,
                    onlyWith: ["set"], onlyField: "what"),
                Number("stepPercent", "By how much", 10,
                    "How far to move the brightness, in points; from 1 to 100 and 10 by default. "
                    + "Used by \"Turn it up\" and \"Turn it down\", which never go past 0 or 100.",
                    min: 1, max: 100, onlyWith: ["up", "down"], onlyField: "what"),
                Returns(),
            ],
        },

        // ------------------------------------------------------------------ window
        new()
        {
            Key = "window.exists",
            Returns =
            [
                Return("found", "Found", primary: true, type: ActionReturnType.Flag,
                    hint: "True when a matching window is open."),
            ],
            Category = ActionCategory.Window,
            DisplayName = "Window Exists",
            Description = "Check whether a matching window is open.",
            Parameters =
            [
                ..WindowTarget(),
                Returns(),
            ],
        },
        new()
        {
            Key = "window.waitFor",
            Returns =
            [
                Return("title", "Title", primary: true, hint: "What the window's title bar says."),
            ],
            Category = ActionCategory.Window,
            DisplayName = "Wait for Window",
            Description = "Wait until a matching window appears.",
            Parameters =
            [
                ..WindowTarget(),
                Number("timeoutMs", "Timeout", 10000,
                    "How long to wait for the window before giving up, in milliseconds; 10000 by "
                    + "default."),
                Returns(),
            ],
        },
        new()
        {
            Key = "window.activate",
            Category = ActionCategory.Window,
            DisplayName = "Activate Window",
            Description = "Bring the window to the front, restoring it first if it is minimized.",
            Parameters =
            [
                ..WindowTarget(),
            ],
        },
        new()
        {
            Key = "window.minimize",
            Category = ActionCategory.Window,
            DisplayName = "Minimize Window",
            Description = "Minimize the window to the taskbar.",
            Parameters =
            [
                ..WindowTarget(),
            ],
        },
        new()
        {
            Key = "window.maximize",
            Category = ActionCategory.Window,
            DisplayName = "Maximize Window",
            Description = "Maximize the window so it fills the screen.",
            Parameters =
            [
                ..WindowTarget(),
            ],
        },
        new()
        {
            Key = "window.restore",
            Category = ActionCategory.Window,
            DisplayName = "Restore Window",
            Description = "Put a minimized or maximized window back to its normal size.",
            Parameters =
            [
                ..WindowTarget(),
            ],
        },
        new()
        {
            Key = "window.move",
            Category = ActionCategory.Window,
            DisplayName = "Move Window",
            Description = "Move the window to the given position and give it the given size, in "
                + "screen pixels.",
            Parameters =
            [
                ..WindowTarget(),
                Number("x", "X", 0,
                    "Left edge, in screen pixels, of the rectangle the window is seen at: the "
                    + "border Windows keeps round it for resizing is not drawn and does not count. "
                    + "The rectangle window.info hands over can be filled in here as it is; 0 by "
                    + "default, and anywhere from -100000 to 100000.",
                    min: -100_000m, max: 100_000m),
                Number("y", "Y", 0,
                    "Top edge, in screen pixels, counted the way X is; 0 by default, and anywhere "
                    + "from -100000 to 100000.", min: -100_000m, max: 100_000m),
                Number("width", "Width", 800,
                    "How wide the window is to become, in pixels, measured the same way: as the "
                    + "window is seen rather than as Windows stretches it; 800 by default, and "
                    + "anywhere from 1 to 100000.", min: 1m, max: 100_000m),
                Number("height", "Height", 600,
                    "How tall the window is to become, in pixels, counted the way the width is; 600 "
                    + "by default, and anywhere from 1 to 100000.", min: 1m, max: 100_000m),
            ],
        },
        new()
        {
            Key = "window.close",
            Category = ActionCategory.Window,
            DisplayName = "Close Window",
            Description = "Ask a window to close, the same as clicking its close button.",
            Parameters =
            [
                ..WindowTarget(),
            ],
        },
        new()
        {
            Key = "window.list",
            Returns =
            [
                Return("windows", "Windows", primary: true, type: ActionReturnType.List,
                    hint: "The windows that matched, each as a record of its own: the same shape "
                        + "window.info hands over for one window, without the part inside its "
                        + "border, with the fields title, handle, minimized, maximized, x and y "
                        + "(the middle of the window), left, top, width, height and box, plus "
                        + "process and class when the step is told to read them, in the order the "
                        + "system lists them. Two windows with the same title are two windows and "
                        + "get a record each. Read one field of one of them with "
                        + "field(get($name, 0), \"title\"), and walk them all with forEach."),
            ],
            Category = ActionCategory.Window,
            DisplayName = "List Windows",
            Description = "Collect the open windows into a list, one record each, or only the ones "
                + "the match text picks out.",
            Parameters =
            [
                ..WindowTarget("every window"),
                Toggle("details", "Read every program and class (slow)", false,
                    "Read the program name and the window class of every window found into the "
                    + "process and the class of its record. Telling windows apart by title does not "
                    + "need them, and reading them asks the system once per window, so this is off "
                    + "by default and belongs on when the macro goes by program or by class.",
                    advanced: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "window.info",
            Returns =
            [
                Return("window", "Window", primary: true, type: ActionReturnType.Record,
                    hint: "The window as one record, the shape a listing hands over for each window "
                        + "it finds, with the part inside its border added: title, process, class, "
                        + "handle, minimized, maximized, x and y (the middle of the window), left, "
                        + "top, width, height, box and clientBox. Read one part of it with "
                        + "field($name, \"left\"). box is the rectangle "
                        + "written as left,top,width,height, which a search region takes as it "
                        + "stands; it is the rectangle the window is seen at, so the border Windows "
                        + "keeps round it for resizing is not drawn and does not count and a "
                        + "maximized window is measured as it looks. clientBox is the part inside "
                        + "that border and under the title bar, which is where a search anchored to "
                        + "the window looks."),
            ],
            Category = ActionCategory.Window,
            DisplayName = "Window Info",
            Description = "Read where a window sits and how big it is into variables.",
            Parameters =
            [
                ..WindowTarget(),
                Returns(),
            ],
        },

        // ------------------------------------------------------------------ input
        new()
        {
            Key = "input.keyPress",
            Category = ActionCategory.Input,
            DisplayName = "Key Press",
            Description = "Press and release a key.",
            Parameters =
            [
                KeyBind("key", "Key",
                    "The key to press, named the way it is printed on the keyboard, for example F5, "
                    + "Enter or A. Take it off the keyboard drawn on screen when the spelling is not "
                    + "obvious."),
                Hold(50, "key"),
                ..Beat(),
                ..Delivery(),
            ],
        },
        new()
        {
            Key = "input.keyDown",
            Category = ActionCategory.Input,
            DisplayName = "Key Down",
            Description = "Hold a key down until a matching Key Up step releases it.",
            Parameters =
            [
                KeyBind("key", "Key",
                    "The key to hold down, named the same way as in a Key Press step. Letting it go "
                    + "is the matching Key Up step's business."),
                ..Delivery(),
            ],
        },
        new()
        {
            Key = "input.keyUp",
            Category = ActionCategory.Input,
            DisplayName = "Key Up",
            Description = "Release a key that is currently held down.",
            Parameters =
            [
                KeyBind("key", "Key",
                    "The key to let go of, named the same way as in a Key Press step; it is one a Key "
                    + "Down step above has held down."),
                ..Delivery(),
            ],
        },
        new()
        {
            Key = "input.hotkey",
            Category = ActionCategory.Input,
            DisplayName = "Hotkey",
            Description = "Press a key combination such as Ctrl+Shift+S.",
            Parameters =
            [
                KeyChord("keys", "Keys",
                    "The keys to press together, joined with +, for example Ctrl+Shift+S. Take them "
                    + "off the keyboard drawn on screen to be sure of the spelling: the two Ctrl keys "
                    + "and the two Shift keys are named apart, and the machine tells them apart."),
                Hold(50, "combination"),
                ..Beat(),
                ..Delivery(),
            ],
        },
        new()
        {
            Key = "input.keySequence",
            Category = ActionCategory.Input,
            DisplayName = "Key Sequence",
            Description = "Send a run of key combinations in order, the way a game plays out a "
                + "combo.",
            Parameters =
            [
                KeySequence(),
                ..Delivery(),
                Number("holdMs", "Hold each one for", 50,
                    "How long every combination of the run stays down, in milliseconds; 50 by "
                    + "default. A row may say its own."),
                Number("gapMs", "Gap between them", 120,
                    "How long the run waits between one combination and the next, in milliseconds; "
                    + "120 by default. This is the beat of the combo, and it is the part a game "
                    + "notices: too short and the presses run into one another, too long and they "
                    + "stop being a combo. A row may say its own, and the give beside this box is "
                    + "what keeps the beat from being the same number every time a macro plays it."),
                Number("afterMs", "Pause afterwards", 0,
                    "How long to wait after the last combination before the next step runs, in "
                    + "milliseconds; 0 by default.", advanced: true),
                Number("repeat", "Play the run again", 1,
                    "How many times the whole run is played, from 1 up; 1 by default.", min: 1),
                Number("intervalMs", "Between runs", 0,
                    "How long to wait between one playing of the run and the next, in milliseconds; "
                    + "0 by default."),
            ],
        },
        new()
        {
            Key = "input.typeText",
            Category = ActionCategory.Input,
            DisplayName = "Type Text",
            Description = "Type a string of text.",
            Parameters =
            [
                Multiline("text", "Text",
                    "The text to type; it may run over several lines, and it may be a variable or an "
                    + "expression.", "Hello world", acceptsFormula: true),
                Number("intervalMs", "Interval", 30,
                    "How long to wait between characters, in milliseconds; 30 by default. Raise it "
                    + "for an application that misses characters typed too fast."),
                ..Delivery(),
            ],
        },
        new()
        {
            Key = "input.mouseMove",
            Category = ActionCategory.Input,
            DisplayName = "Mouse Move",
            Description = "Move the cursor to a screen position.",
            Parameters =
            [
                Number("x", "X", 0,
                    "The column of the place to move to, counted from the coordinate origin above, "
                    + "in pixels; 0 by default. Type a number for a place that does not move, or "
                    + "change the coordinate origin, or put a variable here, for one that does."),
                Number("y", "Y", 0,
                    "The row of the place to move to, counted from the coordinate origin above, in "
                    + "pixels; 0 by default."),
                ..Anchor(withElement: true),
                Number("durationMs", "Duration", 0,
                    "How long the pointer takes to travel from where it is to the target, in "
                    + "milliseconds; 0, the default, arrives at once."),
                Movement(),
                ..Delivery(),
            ],
        },
        new()
        {
            Key = "input.mouseMoveRelative",
            Category = ActionCategory.Input,
            DisplayName = "Mouse Move Relative",
            Description = "Move the cursor by an offset from its current position.",
            Parameters =
            [
                Number("dx", "Offset X", 0,
                    "How far to move sideways, in pixels; positive goes right and negative left, 0 by "
                    + "default, and no less than -100000.", min: -100000m),
                Number("dy", "Offset Y", 0,
                    "How far to move up or down, in pixels; positive goes down and negative up, 0 by "
                    + "default, and no less than -100000.", min: -100000m),
                Number("durationMs", "Duration", 0,
                    "How long the pointer takes to travel to the offset place, in milliseconds; 0, "
                    + "the default, arrives at once."),
                Movement(),
                ..Delivery(
                    "This step cannot use the background mode: a move by an offset asks where the "
                    + "pointer is now, and that mode never moves it. Use in-front or driver input, "
                    + "or write the whole place out with Mouse Move."),
            ],
        },
        new()
        {
            Key = "input.mouseClick",
            Category = ActionCategory.Input,
            DisplayName = "Mouse Click",
            Description = "Click a mouse button at a screen position.",
            Parameters =
            [
                Button(),
                AtPointer(),
                ..PointerPosition(
                    "The column of the place to click, counted from the coordinate origin above, in "
                    + "pixels; 0 by default. Nothing is written here while the place comes from the "
                    + "pointer.",
                    "The row of the place to click, counted from the same origin and in the same "
                    + "pixels; 0 by default."),
                ..Anchor(withElement: true),
                Aim(),
                PutBack(),
                Hold(0, "button",
                    "0 sends a normal quick click; a longer hold is for buttons that only answer a "
                    + "press-and-hold."),
                ..Beat(),
                ..Delivery(),
            ],
        },
        new()
        {
            Key = "input.mouseDoubleClick",
            Category = ActionCategory.Input,
            DisplayName = "Mouse Double Click",
            Description = "Double click a mouse button at a screen position.",
            Parameters =
            [
                Button(),
                AtPointer(),
                ..PointerPosition(
                    "The column of the place to double click, counted from the coordinate origin "
                    + "above, in pixels; 0 by default. Nothing is written here while the place comes "
                    + "from the pointer.",
                    "The row of the place to double click, counted from the same origin and in the "
                    + "same pixels; 0 by default."),
                ..Anchor(withElement: true),
                Aim(),
                PutBack(),
                ..Beat(),
                ..Delivery(),
            ],
        },
        new()
        {
            Key = "input.mouseDown",
            Category = ActionCategory.Input,
            DisplayName = "Mouse Down",
            Description = "Hold a mouse button down until a Mouse Up step releases it.",
            Parameters =
            [
                Button(),
                AtPointer(),
                ..PointerPosition(
                    "The column of the place to press, counted from the coordinate origin above, in "
                    + "pixels; 0 by default. Nothing is written here while the place comes from the "
                    + "pointer.",
                    "The row of the place to press, counted from the same origin and in the same "
                    + "pixels; 0 by default."),
                ..Anchor(withElement: true),
                ..Delivery(),
            ],
        },
        new()
        {
            Key = "input.mouseUp",
            Category = ActionCategory.Input,
            DisplayName = "Mouse Up",
            Description = "Release a mouse button that is currently held down.",
            Parameters =
            [
                Button(),
                AtPointer(),
                ..PointerPosition(
                    "The column of the place to let go at, counted from the coordinate origin above, "
                    + "in pixels; 0 by default. Nothing is written here while the place comes from "
                    + "the pointer.",
                    "The row of the place to let go at, counted from the same origin and in the same "
                    + "pixels; 0 by default."),
                ..Anchor(withElement: true),
                ..Delivery(),
            ],
        },
        new()
        {
            Key = "input.mouseScroll",
            Category = ActionCategory.Input,
            DisplayName = "Mouse Scroll",
            Description = "Scroll the mouse wheel.",
            Parameters =
            [
                Choice("direction", "Direction", ["up", "down", "left", "right"], "down",
                    "Which way the wheel turns; down by default."),
                Choice("unit", "Unit", ["notches", "pixels"], "notches",
                    "What the amount below is counted in; whole notches by default. Notches are the "
                    + "steps a wheel normally turns in. Pixels measure the same turn more finely, with "
                    + "120 pixels to a notch; an application that only looks at whole notches may "
                    + "ignore the smaller amounts."),
                Number("amount", "Amount", 3,
                    "How far to scroll, counted in the unit above, from 1 up; 3 by default.", min: 1),
                Number("smoothMs", "Smooth", 0,
                    "Spread the scroll over this many milliseconds instead of sending it in one "
                    + "jump, so an application that animates its scrolling can follow; 0, the "
                    + "default, sends it all at once.", advanced: true),
                ..PointerPosition(
                    "The column of the place to scroll at, counted from the coordinate origin above, "
                    + "in pixels; 0 by default. Nothing is written here while the place comes from "
                    + "the pointer.",
                    "The row of the place to scroll at, counted from the same origin and in the same "
                    + "pixels; 0 by default."),
                AtPointer(),
                ..Anchor(withElement: true),
                ..Beat(),
                ..Delivery(),
            ],
        },
        new()
        {
            Key = "input.mouseDrag",
            Category = ActionCategory.Input,
            DisplayName = "Mouse Drag",
            Description = "Press at one point, move to another and release.",
            Parameters =
            [
                Number("startX", "Start X", 0,
                    "The column of the place the drag begins at, counted from the coordinate origin "
                    + "above, in pixels; 0 by default. Type a number for a place that does not move, "
                    + "or put a variable here, for one that does."),
                Number("startY", "Start Y", 0,
                    "The row of the place the drag begins at, counted from the same origin and in the "
                    + "same pixels; 0 by default."),
                Number("endX", "End X", 0,
                    "The column of the place the drag ends at, counted from the same origin and in "
                    + "the same pixels; 0 by default."),
                Number("endY", "End Y", 0,
                    "The row of the place the drag ends at, counted from the same origin and in the "
                    + "same pixels; 0 by default."),
                ..Anchor(withElement: true),
                Button(),
                Number("durationMs", "Duration", 300,
                    "How long the drag takes, in milliseconds; 300 by default. The pointer travels "
                    + "from one end to the other over that time."),
                Number("steps", "Move steps", 20,
                    "Intermediate move events sent while dragging, from 1 up; 20 by default. The more "
                    + "of them, the more finely the pointer's path is drawn.", min: 1, advanced: true),
                Movement(),
                Settle(),
                ..Delivery(),
            ],
        },

        // ----------------------------------------------------------------- gamepad
        new()
        {
            Key = "gamepad.connect",
            Category = ActionCategory.Input,
            DisplayName = "Connect Controller",
            Description = "Put the virtual controller (an Xbox 360 pad) on the machine, which the "
                + "steps after this one press the buttons of. The steps that drive a controller "
                + "put it there by themselves, so this is for a game that only looks at what is "
                + "connected when it starts. It is the same driver-level input as a virtual "
                + "keyboard: the usbip-win2 driver and the VIIPER server have to be there.",
            Parameters = [],
        },
        new()
        {
            Key = "gamepad.button",
            Category = ActionCategory.Input,
            DisplayName = "Controller Button",
            Description = "Press a button of the virtual controller, hold it down, or let it up. "
                + "The buttons are named the Xbox way whatever controller is on the machine.",
            Parameters =
            [
                GamepadButton(),
                Choice("mode", "Action", ["tap", "down", "up"], "tap",
                    "What this step does to the button; a tap by default. A tap presses and lets go; "
                    + "hold and let up are for a button that has to stay down across other steps.",
                    labels: ["Tap", "Hold down", "Let up"]),
                Hold(50, "button", "It only applies to a tap."),
                ..Beat(),
            ],
        },
        new()
        {
            Key = "gamepad.stick",
            Category = ActionCategory.Input,
            DisplayName = "Controller Stick",
            Description = "Move a stick of the virtual controller, in whole percent from its centre.",
            Parameters =
            [
                Choice("stick", "Stick", ["left", "right"], "left",
                    "Which of the two sticks is pushed; the left one by default.",
                    labels: ["Left stick", "Right stick"]),
                Number("x", "X", 0,
                    "How far sideways the stick is pushed, as whole percent away from its centre, "
                    + "from -100 to 100; 0 by default, 100 all the way right and -100 all the way "
                    + "left.",
                    min: -100, max: 100),
                Number("y", "Y", 0,
                    "How far up or down the stick is pushed, as whole percent away from its centre, "
                    + "from -100 to 100; 0 by default, 100 all the way up and -100 all the way down.",
                    min: -100, max: 100),
            ],
        },
        new()
        {
            Key = "gamepad.trigger",
            Category = ActionCategory.Input,
            DisplayName = "Controller Trigger",
            Description = "Pull a trigger of the virtual controller, or let it back off.",
            Parameters =
            [
                Choice("trigger", "Trigger", ["left", "right"], "left",
                    "Which of the two triggers is pulled; the left one by default.",
                    labels: ["Left trigger", "Right trigger"]),
                Number("amount", "Amount", 100,
                    "How far the trigger is pulled, as whole percent, from 0 to 100; 100 by default, "
                    + "which is all the way down. 0 lets it back off.", min: 0, max: 100),
            ],
        },
        new()
        {
            Key = "gamepad.release",
            Category = ActionCategory.Input,
            DisplayName = "Release Controller",
            Description = "Let go of every button, stick and trigger of the virtual controller. "
                + "A run does this by itself when it finishes; this is for a macro that wants to "
                + "hand the controller back in the middle of what it is doing.",
            Parameters = [],
        },

        // ----------------------------------------------------------------- vision
        new()
        {
            Key = "vision.capture",
            Returns =
            [
                Return("image", "Picture", primary: true, type: ActionReturnType.Image,
                    hint: "The picture that was captured, ready to be searched for or written out."),
                ..Place(),
            ],
            Category = ActionCategory.Vision,
            DisplayName = "Capture",
            Description = "Capture a screen region into an image variable.",
            Parameters =
            [
                ..RegionNumbers(),
                ..Anchor(withElement: true),
                ..Capture(),
                CaptureFile(),
                LookGap(0),
                Returns(),
            ],
        },
        new()
        {
            Key = "vision.captureWindow",
            Returns =
            [
                Return("image", "Picture", primary: true, type: ActionReturnType.Image,
                    hint: "The picture that was captured, title bar and all."),
                ..Place(),
            ],
            Category = ActionCategory.Vision,
            DisplayName = "Capture Window",
            Description = "Capture a whole window, title bar and all, into an image variable.",
            Parameters =
            [
                ..WindowTarget(),
                ..Capture(),
                CaptureFile(),
                LookGap(0),
                Returns(),
            ],
        },
        new()
        {
            Key = "vision.findImage",
            Returns = Hits(),
            Category = ActionCategory.Vision,
            DisplayName = "Find Image",
            Description = "Look for a reference image on screen.",
            Parameters =
            [
                Images("image", "Reference pictures", @"C:\images\ok.png",
                   "The pictures to look for, one row each, tried in the order they are listed; the "
                   + "first one that turns up is the one the step goes with. A row is a file path or "
                   + "the variable a Capture step saved ($shot)."),
                Region(),
                ..Capture(),
                LookGap(0),
                Number("confidence", "Similarity %", 90,
                    "How alike the picture on screen has to be, from 0 to 100 per cent; the default "
                    + "is 90. Raise it to accept fewer false matches, lower it when the screen is "
                    + "drawn a little differently.", max: 100),
                ImageSet(),
                MatchAlgorithm(),
                SkipColour(),
                MinFeatures(),
                MatchOrder(pixelsOnly: true),
                ..Anchor(),
                MatchIndex(pixelsOnly: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "vision.waitImage",
            Returns = Hits(),
            Category = ActionCategory.Vision,
            DisplayName = "Wait Image",
            Description = "Wait until a reference image appears on screen, for as long as the "
                + "step's own timeout allows; running out of it fails the step.",
            StepTimeoutMs = 5000,
            Parameters =
            [
                Images("image", "Reference pictures", @"C:\images\ok.png",
                    "The pictures to wait for, one row each, tried in the order they are listed. A "
                    + "row is a file path or the variable a Capture step saved ($shot)."),
                Region(),
                ..Capture(),
                LookPoll(),
                Number("confidence", "Similarity %", 90,
                    "How alike the picture on screen has to be, from 0 to 100 per cent; the default "
                    + "is 90.", max: 100),
                ImageSet(),
                MatchAlgorithm(),
                SkipColour(),
                MinFeatures(),
                MatchOrder(pixelsOnly: true),
                ..Anchor(),
                MatchIndex(pixelsOnly: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "vision.clickImage",
            Returns = Hits(),
            Category = ActionCategory.Vision,
            DisplayName = "Click Image",
            Description = "Find a reference image on screen and click it, waiting for as long as "
                + "the step's own timeout allows; running out of it fails the step.",
            StepTimeoutMs = 5000,
            Parameters =
            [
                Images("image", "Reference pictures", @"C:\images\ok.png",
                    "The pictures to look for, one row each, tried in the order they are listed; the "
                    + "first one that turns up is the one that gets clicked."),
                Region(),
                ..Capture(),
                Number("confidence", "Similarity %", 90,
                    "How alike the picture on screen has to be, from 0 to 100 per cent; the default "
                    + "is 90.", max: 100),
                ImageSet(),
                Button(),
                Number("offsetX", "Offset X", 0,
                    "Pixels added to the middle of the match before clicking. 0, the default, clicks "
                    + "the middle.", min: -100000m),
                Number("offsetY", "Offset Y", 0,
                    "Pixels added to the middle of the match before clicking, downwards for a "
                    + "positive number; 0, the default, clicks the middle.", min: -100000m),
                MatchAlgorithm(),
                SkipColour(),
                MinFeatures(),
                MatchOrder(pixelsOnly: true),
                ..Anchor(),
                MatchIndex(pixelsOnly: true),
                LookPoll(),
                Aim(),
                Recheck("picture"),
                Returns(),
            ],
        },
        new()
        {
            Key = "vision.getPixel",
            Returns =
            [
                Return("color", "Colour", primary: true,
                    hint: "The colour of the pixel, as hex or as raw channels, whichever the step "
                        + "was asked for."),
            ],
            Category = ActionCategory.Vision,
            DisplayName = "Get Pixel",
            Description = "Read the colour of a pixel.",
            Parameters =
            [
                Number("x", "X", 0,
                    "The column to sample, counted from the coordinate origin above, in pixels; 0 by "
                    + "default."),
                Number("y", "Y", 0,
                    "The row to sample, counted from the same origin and in the same pixels; 0 by "
                    + "default."),
                ..Anchor(),
                ..Capture(),
                LookGap(0),
                Returns(),
                Toggle("asHex", "Store as hex", true,
                    "Store #RRGGBB instead of raw colour channels. On by default."),
            ],
        },
        new()
        {
            Key = "vision.waitColor",
            Category = ActionCategory.Vision,
            DisplayName = "Wait Color",
            Description = "Wait until a pixel shows a specific colour, for as long as the step's "
                + "own timeout allows; running out of it fails the step.",
            StepTimeoutMs = 5000,
            Parameters =
            [
                Number("x", "X", 0,
                    "The column to watch, counted from the coordinate origin above, in pixels; 0 by "
                    + "default."),
                Number("y", "Y", 0,
                    "The row to watch, counted from the same origin and in the same pixels; 0 by "
                    + "default."),
                ColorPick("color", "Colour", "#000000",
                    "Colour the pixel has to show, written as #RRGGBB; #000000 by default."),
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference, 0 to 100 per cent; the default is 5.", max: 100),
                ..Anchor(),
                ..Capture(),
                LookPoll(),
            ],
        },
        new()
        {
            Key = "vision.findColor",
            Returns = [.. Hits(), .. Coverage()],
            Category = ActionCategory.Vision,
            DisplayName = "Find Color",
            Description = "Look for a colour inside a screen region.",
            Parameters =
            [
                ColorPick("color", "Colour", "#000000", "Colour to look for."),
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference, 0 to 100 per cent; the default is 5.", max: 100),
                Number("minCoverage", "At least this much", 0,
                    "The least share of the search region that has to show the colour for the step "
                    + "to count it as found, from 0 to 100 per cent; the default is 0, which does "
                    + "not look at the share at all. A bar half filled, a button lit up, a gauge "
                    + "past its mark are read with this rather than by where the colour is: the "
                    + "share is measured over the whole search region, several rows counted as "
                    + "one. Measuring it costs a walk of the whole region, so a step that does "
                    + "not ask for it is not slowed down.",
                    min: 0, max: 100, advanced: true),
                Region(),
                ..Capture(),
                MatchOrder(bySize: false),
                MatchIndex(),
                ..Anchor(),
                LookGap(0),
                Returns(),
            ],
        },
        new()
        {
            Key = "vision.waitStable",
            Category = ActionCategory.Vision,
            DisplayName = "Wait for Screen to Settle",
            Description = "Wait until the screen stops changing, so the step after it works on a "
                + "picture that is not still being drawn. It waits for as long as the step's own "
                + "timeout allows; running out of it fails the step.",
            StepTimeoutMs = 5000,
            Parameters =
            [
                Region(),
                ..Anchor(),
                ..Capture(),
                Number("quietMs", "Hold still for", 500,
                    "How long the area has to stay unchanged before the step goes on. A page that "
                    + "is still filling in, or an animation still running, keeps this from being "
                    + "reached. In milliseconds; the default is 500."),
                Number("tolerance", "Tolerance %", 5,
                    "How far a pixel may drift and still count as the same pixel. Raise it for a "
                    + "picture with noise or a slow fade in it. From 0 to 100 per cent, 5 by "
                    + "default.", max: 100, advanced: true),
                Number("changedPercent", "Allowed change %", 0.1m,
                    "How much of the area may move and still count as still. A spinner turning, a "
                    + "list scrolling or a progress bar filling moves more than this. From 0 to 100 "
                    + "per cent, 0.1 by default.",
                    max: 100, advanced: true),
                LookPoll(),
            ],
        },
        new()
        {
            Key = "vision.findColors",
            Returns = Hits(),
            Category = ActionCategory.Vision,
            DisplayName = "Find Colour Pattern",
            Description = "Look for a colour that has the colours this step names around it, which "
                + "is how two things drawn alike are told apart.",
            Parameters =
            [
                ColorPick("color", "Base colour", "#000000",
                    "The colour in the middle of the pattern: the one whose place is handed over, "
                    + "and the pixel the colours around it are counted from."),
                Multiline("points", "Colours around it",
                    "The colours that have to be there, one per line, written as the offset from "
                    + "the base colour and the colour itself: 12,-4,#FF0000. Every one of them has "
                    + "to be where it says, and the whole pattern has to fit inside the search "
                    + "region; a pattern that runs off the edge of the region is not found.",
                    "12,-4,#FF0000; -8,3,#00FF00"),
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference, 0 to 100 per cent; the default is 5.", max: 100),
                Region(),
                ..Capture(),
                LookGap(0),
                MatchOrder(bySize: false),
                MatchIndex(),
                ..Anchor(),
                Returns(),
            ],
        },

        // -------------------------------------------------------------------- ocr
        new()
        {
            Key = "ocr.recognize",
            Returns =
            [
                Return("text", "Text", primary: true, hint: "The writing that was read."),
                Return("table", "Table", type: ActionReturnType.List,
                    hint: "The writing kept as rows, each one a record keyed by where its cells sit "
                        + "(A, B, C, the shape a file with no header gets). Only filled in when the "
                        + "step is told to read a table."),
            ],
            Category = ActionCategory.Ocr,
            DisplayName = "Recognize",
            Description = "Read text from a screen region.",
            Parameters =
            [
                ..RegionNumbers(),
                Content(),
                ContentPattern(),
                Toggle("table", "Read as a table", false,
                    "Keep the lines and columns the writing was read in: the table result holds one "
                        + "record per row, keyed by where each cell sits (A, B, C, the way a table "
                        + "through UI Automation is keyed by its column names), and the text result "
                        + "holds the whole lot as text, one line per row."),
                ..Anchor(),
                ..Capture(),
                LookGap(0),
                Preprocess(advanced: true),
                ColourFilter(advanced: true),
                ColourTolerance(advanced: true),
                FixText(advanced: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "ocr.findText",
            Returns = Hits(),
            Category = ActionCategory.Ocr,
            DisplayName = "Find Text",
            Description = "Search the screen for text.",
            Parameters =
            [
                Text("text", "Text to find", "Save",
                    "Text to look for, read as one value, so a variable or an expression may stand "
                    + "for it; the match mode below says how it is compared.",
                    acceptsFormula: true),
                Region(),
                ..Capture(),
                LookGap(0),
                TextMatch(),
                Content(),
                ContentPattern(),
                ..Anchor(),
                Preprocess(advanced: true),
                ColourFilter(advanced: true),
                ColourTolerance(advanced: true),
                FixText(advanced: true),
                Expected(),
                MinScore(),
                MatchOrder(),
                Returns(),
            ],
        },
        new()
        {
            Key = "ocr.clickText",
            Returns = Hits(),
            Category = ActionCategory.Ocr,
            DisplayName = "Click Text",
            Description = "Find text on screen and click it, waiting for as long as the step's own "
                + "timeout allows; running out of it fails the step.",
            StepTimeoutMs = 5000,
            Parameters =
            [
                Text("text", "Text to find", "Save",
                    "Text to look for, read as one value, so a variable or an expression may stand "
                    + "for it.", acceptsFormula: true),
                Region(),
                ..Capture(),
                TextMatch(),
                Content(),
                ContentPattern(),
                Button(),
                Number("offsetX", "Offset X", 0,
                    "Pixels added to the match centre; 0, the default, clicks the centre.",
                    min: -100000m),
                Number("offsetY", "Offset Y", 0,
                    "Pixels added to the middle of the match before clicking, downwards for a "
                    + "positive number; 0, the default, clicks the middle.", min: -100000m),
                ..Anchor(),
                LookPoll(),
                Aim(),
                Recheck("text"),
                Preprocess(advanced: true),
                ColourFilter(advanced: true),
                ColourTolerance(advanced: true),
                FixText(advanced: true),
                Expected(),
                MinScore(),
                MatchOrder(),
                Returns(),
            ],
        },

        // -------------------------------------------------------------------- uia
        new()
        {
            Key = "uia.exists",
            Returns =
            [
                Return("exists", "Exists", primary: true, type: ActionReturnType.Flag,
                    hint: "True when the element was there."),
            ],
            Category = ActionCategory.Uia,
            DisplayName = "Exists",
            Description = "Check whether a UI Automation element exists.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
                Number("timeoutMs", "Timeout", 0,
                    "How long to wait for the element to turn up, in milliseconds; the default is "
                    + "0, which checks once and returns immediately."),
                Returns(),
            ],
        },
        new()
        {
            Key = "uia.find",
            Returns = Hits(),
            Category = ActionCategory.Uia,
            DisplayName = "Find Element",
            Description = "Find a UI Automation element and remember where it is.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
                Returns(),
            ],
        },
        new()
        {
            Key = "uia.waitElement",
            Category = ActionCategory.Uia,
            DisplayName = "Wait Element",
            Description = "Wait until a UI Automation element exists.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
                Number("timeoutMs", "Timeout", 10000,
                    "How long to wait for the element before giving up, in milliseconds; 10000 by "
                    + "default."),
                Number("pollMs", "Check every", 200,
                    "How long to leave between two checks, in milliseconds; 200 by default.",
                    advanced: true),
            ],
        },
        new()
        {
            Key = "uia.click",
            Category = ActionCategory.Uia,
            DisplayName = "Click Element",
            Description = "Click a UI Automation element.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
                Number("timeoutMs", "Timeout", 5000,
                    "How long to wait for the element before clicking, in milliseconds; 5000 by "
                    + "default."),
                Button(),
            ],
        },
        new()
        {
            Key = "uia.invoke",
            Category = ActionCategory.Uia,
            DisplayName = "Press Element",
            Description = "Work a UI Automation element's own action, without moving the mouse.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
                Number("timeoutMs", "Timeout", 5000,
                    "How long to wait for the element before pressing it, in milliseconds; 5000 by "
                    + "default."),
            ],
        },
        new()
        {
            Key = "uia.setText",
            Category = ActionCategory.Uia,
            DisplayName = "Set Text",
            Description = "Type text into a UI Automation element.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
                Text("text", "Text", "Hello", "Text written into the element.",
                    acceptsFormula: true),
                Toggle("clearFirst", "Clear first", true, "Clear the field before typing."),
            ],
        },
        new()
        {
            Key = "uia.read",
            Returns =
            [
                Return("element", "Element", primary: true, type: ActionReturnType.Record,
                    hint: "What the element says about itself, as one record: enabled and visible, "
                        + "value, name, automationId and controlType, then x and y, the middle of "
                        + "it, then left, top, width, height and box. Read one part of it with "
                        + "field($name, \"value\"). value is what "
                        + "the element holds (the text in a box, the caption of a button), and is "
                        + "empty for one that keeps nothing."),
            ],
            Category = ActionCategory.Uia,
            DisplayName = "Read Element",
            Description = "Read everything one UI Automation element says about itself.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
                Returns(),
            ],
        },
        new()
        {
            Key = "uia.select",
            Category = ActionCategory.Uia,
            DisplayName = "Select Item",
            Description = "Pick an entry of a drop-down, a list or a set of tabs.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
                Text("item", "Entry", "Ready", required: false,
                    hint: "The entry to pick, by the text it shows; a $variable holding the text "
                          + "works too. Leave it empty and fill in the number below instead.",
                    acceptsFormula: true,
                    onlyWith: ["", "0"], onlyField: "itemIndex"),
                Number("itemIndex", "Entry number", 0,
                    "Which entry to pick, counted from 1; the default is 0, which picks the one "
                    + "named above.",
                    max: 10000, onlyWith: [""], onlyField: "item"),
            ],
        },
        new()
        {
            Key = "uia.check",
            Category = ActionCategory.Uia,
            DisplayName = "Check Box",
            Description = "Turn a check box, a switch or a radio button on or off.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
                Choice("state", "State", ["on", "off", "toggle"], "on",
                    "What to do with it. \"The other way round\" flips whatever it is now, which is "
                    + "what a macro wants when it does not know the state it starts from.",
                    labels: ["Turn on", "Turn off", "The other way round"]),
            ],
        },
        new()
        {
            Key = "uia.expand",
            Category = ActionCategory.Uia,
            DisplayName = "Expand or Collapse",
            Description = "Open or close a tree branch, a menu or a collapsed panel.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
                Choice("state", "State", ["expand", "collapse", "toggle"], "expand",
                    "Whether to open it, close it, or do whichever it is not right now.",
                    labels: ["Open", "Close", "The other way round"]),
            ],
        },
        new()
        {
            Key = "uia.scrollIntoView",
            Category = ActionCategory.Uia,
            DisplayName = "Scroll Into View",
            Description = "Scroll an element into view inside the list or panel holding it.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
            ],
        },
        new()
        {
            Key = "uia.readTable",
            Returns =
            [
                Return("table", "Table", primary: true, type: ActionReturnType.List,
                    hint: "The rows that were read, each a record whose keys are the names written "
                        + "above its columns, which is the shape the CSV reader gives back, so "
                        + "field(get($name, 0), \"Name\") reads one cell by name. A table with no "
                        + "strip of names gets A, B, C instead, and a column whose name is blank or "
                        + "repeats one already taken keeps the letter of its place."),
            ],
            Category = ActionCategory.Uia,
            DisplayName = "Read Table",
            Description = "Read a table or a grid into a list of rows.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
                MatchIndex(),
                Number("maxRows", "Rows to read", 100,
                    "Stop after this many rows of data, counted from the top, from 1 to 10000 and "
                    + "100 by default. Rows that hold nothing at all, the strip the column titles live "
                    + "in and the empty row some grids keep at the bottom for typing a new one in, are "
                    + "left out.",
                    min: 1, max: 10000, advanced: true),
                Returns(),
            ],
        },
        new()
        {
            Key = "uia.focusWindow",
            Category = ActionCategory.Uia,
            DisplayName = "Focus Window",
            Description = "Bring a window to the front.",
            Parameters =
            [
                ..WindowFilter(
                    hint: "The window to bring to the front, recognised the way every window rule "
                        + "is. The picker fills in one of the windows that are open.",
                    required: true),
            ],
        },

        // ---------------------------------------------------------------- browser
        new()
        {
            Key = "browser.open",
            Category = ActionCategory.Browser,
            DisplayName = "Open Browser",
            Description = "Start a browser and open a page at an address.",
            Parameters =
            [
                Choice("browser", "Browser",
                    ["edge", "chrome", "chromium", "firefox", "webkit"], "edge",
                    "Which browser to drive. Edge is already on every Windows, so nothing has to be "
                    + "installed for it; a machine without Chrome or the engines Playwright fetches "
                    + "gets a message naming the command that puts them there. Edge is the default.",
                    labels: ["Edge (already on Windows)", "Chrome", "Chromium", "Firefox", "WebKit"]),
                Text("url", "Address", "https://example.com",
                    "The page to open. Empty, which is the default, opens the browser without a "
                    + "page yet; read as one value, so a variable or an expression may stand for it.",
                    required: false, acceptsFormula: true),
                Toggle("headless", "Hidden window", false,
                    "Open the browser without showing it. A page kept off the screen is quicker, "
                    + "but nothing can be watched while the macro runs. Off by default.",
                    advanced: true),
            ],
        },
        new()
        {
            Key = "browser.goTo",
            Category = ActionCategory.Browser,
            DisplayName = "Go To Address",
            Description = "Sends the page the browser has open to another address.",
            Parameters =
            [
                Text("url", "Address", "https://example.com", "The page to open, read as one "
                    + "value, so a variable or an expression may stand for it.", acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "browser.click",
            Category = ActionCategory.Browser,
            DisplayName = "Click Element",
            Description = "Clicks the first element a selector names.",
            Parameters =
            [
                Text("target", "Selector", "#submit",
                    "Where the element is. Playwright selectors read like CSS, and also like text: "
                    + "\"text=Sign in\" clicks the first thing showing that text."),
            ],
        },
        new()
        {
            Key = "browser.fill",
            Category = ActionCategory.Browser,
            DisplayName = "Fill In",
            Description = "Types text into the element a selector names.",
            Parameters =
            [
                Text("target", "Selector", "#search",
                    "Which element to type into, named the way the click action names one."),
                Text("text", "Text", "$query",
                    "What to type. Read as one value, so a macro can type what it just read "
                    + "somewhere else; a variable or an expression may stand for it.",
                    acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "browser.readText",
            Returns =
            [
                Return("text", "Text", primary: true, hint: "The text that was read."),
            ],
            Category = ActionCategory.Browser,
            DisplayName = "Read Text",
            Description = "Reads the text of an element, or of the whole page.",
            Parameters =
            [
                Text("target", "Selector", "",
                    "Which element to read, named the way the click action names one. Leave it "
                    + "empty, which is the default, reads the whole page.", required: false),
                Returns(),
            ],
        },
        new()
        {
            Key = "browser.select",
            Category = ActionCategory.Browser,
            DisplayName = "Select Option",
            Description = "Picks an option of a dropdown.",
            Parameters =
            [
                Text("target", "Selector", "#country",
                    "Which dropdown to pick from, named the way the click action names one."),
                Text("value", "Option", "China",
                    "Which option to pick: the one whose value or visible text is this, or, with "
                    + "\"By number\" below, the number of the option counted from the top starting "
                    + "at 1. Read as one value, so a variable or an expression may stand for it.",
                    acceptsFormula: true),
                Choice("by", "Pick by", ["value", "index"], "value",
                    "How the option above is matched. \"By value or text\" takes the first option "
                    + "whose value or visible text is exactly what is written; \"By number\" takes "
                    + "the option at that position, counted from the top starting at 1. A dropdown "
                    + "the page draws out of other elements is not this kind and has to be clicked "
                    + "open and picked from.",
                    labels: ["By value or text", "By number"]),
            ],
        },
        new()
        {
            Key = "browser.check",
            Category = ActionCategory.Browser,
            DisplayName = "Tick or Untick",
            Description = "Leaves a checkbox or a radio button ticked, or unticked.",
            Parameters =
            [
                Text("target", "Selector", "#agree",
                    "Which box to leave in that state, named the way the click action names one."),
                Toggle("checked", "Ticked", true,
                    "Whether the box should end up ticked. Ticked by default; turn it off to untick "
                    + "a box that is already ticked. Asking for the state it is already in changes "
                    + "nothing rather than unticking it, which a click on an already-ticked box "
                    + "would."),
            ],
        },
        new()
        {
            Key = "browser.hover",
            Category = ActionCategory.Browser,
            DisplayName = "Move Over Element",
            Description = "Moves the pointer over an element and leaves it there.",
            Parameters =
            [
                Text("target", "Selector", "#menu",
                    "Which element to move over, named the way the click action names one. This is "
                    + "what makes a menu, a tooltip or a row of buttons appear; nothing is clicked."),
            ],
        },
        new()
        {
            Key = "browser.press",
            Category = ActionCategory.Browser,
            DisplayName = "Press Key",
            Description = "Sends one key, or one chord, into the page.",
            Parameters =
            [
                Text("target", "Selector", "",
                    "Which element takes the focus before the key is sent, named the way the click "
                    + "action names one. Leave it empty, which is the default, to send the key to "
                    + "the page itself.", required: false),
                KeyChord("key", "Key",
                    "Which key to send: one of the names the keyboard picker offers (Enter, Esc, "
                    + "F5, Down), or several keys joined with \"+\" for one chord (Ctrl+S). A name "
                    + "the keyboard does not have is refused before the step runs."),
            ],
        },
        new()
        {
            Key = "browser.waitFor",
            Category = ActionCategory.Browser,
            DisplayName = "Wait For Element",
            Description = "Waits for an element of the page to appear or to go away.",
            StepTimeoutMs = 15000,
            Parameters =
            [
                Text("target", "Selector", "#results",
                    "Which element to wait for, named the way the click action names one. A page "
                    + "that fetches what it shows needs this rather than a fixed pause: the wait "
                    + "ends the moment the element is in the state asked for."),
                Choice("state", "Wait for it to", ["visible", "hidden", "attached", "detached"],
                    "visible",
                    "Which state to wait for. Visible is what a step about to click something "
                    + "wants; \"Not visible\" is for a spinner that has to be gone; \"On the page\" "
                    + "waits for an element that is there whether or not it takes up room; \"Off "
                    + "the page\" waits for one that has been taken away.",
                    labels: ["Be visible", "Not be visible", "Be on the page", "Be off the page"]),
            ],
        },
        new()
        {
            Key = "browser.upload",
            Category = ActionCategory.Browser,
            DisplayName = "Upload Files",
            Description = "Hands files to a file input on the page.",
            Parameters =
            [
                Text("target", "Selector", "input[type=file]",
                    "Which file input to hand them to, named the way the click action names one."),
                Multiline("files", "Files",
                    "One path to a file, one to a line; every line is a file, so a blank line is "
                    + "skipped. A relative path is read from the folder the macro file is in, the "
                    + "same as every other path a step is given. This is the only way round a "
                    + "browser's rule that a page may not be told a path by a script.",
                    "C:\\Users\\me\\report.pdf"),
            ],
        },
        new()
        {
            Key = "browser.switchTab",
            Category = ActionCategory.Browser,
            DisplayName = "Switch Tab",
            Description = "Moves to another tab of the browser that is open.",
            Parameters =
            [
                Choice("how", "Which tab", ["newest", "index", "title", "url"], "newest",
                    "Which tab the steps after this one work on; the newest one by default. \"The "
                    + "newest one\" is the tab that appeared last, the one a click leaves behind when "
                    + "it opens a new tab, and this step waits a few seconds for it, because a tab "
                    + "reaches the browser a moment after the click that opened it; none turning up "
                    + "is an error rather than staying where the macro was, which on a site whose "
                    + "pages look alike would go unnoticed.",
                    labels: ["The newest one", "By number", "By title", "By address"]),
                Number("index", "Tab number", 1,
                    "Which tab, counted from the left of the tab strip starting at 1. Used with "
                    + "\"by number\" above. From 1 to 50; 1 by default.",
                    min: 1, max: 50,
                    onlyWith: ["index"], onlyField: "how"),
                Text("match", "Title or address holds", "",
                    "The text the tab's title (or address) has to contain. Used with \"by title\" "
                    + "and \"by address\" above.", required: false,
                    acceptsFormula: true, onlyWith: ["title", "url"], onlyField: "how"),
            ],
        },
        new()
        {
            Key = "browser.closeTab",
            Category = ActionCategory.Browser,
            DisplayName = "Close Tab",
            Description = "Closes the tab the browser is on, and moves to another one.",
            Parameters = [],
        },
        new()
        {
            Key = "browser.close",
            Category = ActionCategory.Browser,
            DisplayName = "Close Browser",
            Description = "Closes the browser the macro opened.",
            Parameters = [],
        },

        // ----------------------------------------------------------------- script
        new()
        {
            Key = "script.run",
            Returns =
            [
                Return("output", "Output", primary: true,
                    hint: "What the script printed, without the blank lines around it."),
            ],
            Category = ActionCategory.Script,
            DisplayName = "Run Script",
            Description = "Run a short script with an interpreter of your choice.",
            Parameters =
            [
                Choice("language", "Interpreter",
                    ["powershell", "cmd", "node", "python", "custom"], "powershell",
                    "Which interpreter runs the script. PowerShell and Command Prompt are on every "
                    + "Windows machine; Node and Python have to be installed first; another "
                    + "program is for anything else the machine has, such as dotnet-script. "
                    + "PowerShell by default.",
                    labels: ["PowerShell", "Command Prompt", "Node.js", "Python", "Another program"],
                    advanced: true),
                Text("interpreter", "Interpreter command", "dotnet-script", required: false,
                    hint: "The program that runs the script, with any flags of its own in front, "
                          + "used with \"another program\" above. The script's path goes after it, "
                          + "the way it would be typed on a command line. A path with spaces in it "
                          + "goes in quotes.",
                    advanced: true),
                Text("extension", "Script file ending", ".csx", required: false,
                    hint: "The file name ending that program reads, such as .csx for dotnet-script "
                          + "or .vbs for cscript. Only used with \"another program\" above.",
                    advanced: true),
                Choice("encoding", "Script file encoding", ["auto", .. TextEncoding.Names], "auto",
                    "How the interpreter's copy of the script is written. Left to decide for "
                    + "itself, PowerShell gets a byte-order mark and the rest do not, which is what "
                    + "each of them reads correctly; a program that reads the system code page "
                    + "instead, cscript for one, needs GBK to read Chinese text. Reading it as "
                    + "the interpreter expects is the default.",
                    labels: ["As the interpreter expects", "UTF-8", "UTF-8 with BOM",
                        "GBK (Chinese)", "UTF-16"], advanced: true),
                Multiline("script", "Script",
                    "The script itself. {{name}} is replaced by what the variable name holds "
                    + "before the script runs, which is how a macro value gets in; everything the "
                    + "script prints comes back in the output result.",
                    "Write-Host \"Hello\"", foreign: true),
                Text("arguments", "Arguments", "first second", required: false,
                    hint: "Extra arguments for the script's command line. {{name}} is filled in "
                          + "here as well. Empty, which is the default, passes nothing.",
                    foreign: true),
                PickablePath("folder", "Working folder", string.Empty,
                    "The folder the script runs in. Empty, which is the default, runs it in the "
                    + "macros folder, or wherever WhaleGenie is when that folder is not there yet.",
                    PathIntent.Folder, required: false, advanced: true),
                Number("timeoutMs", "Timeout", 60000,
                    "Stop the script and fail the step after this long, in milliseconds; 60000 by "
                    + "default."),
                Returns(),
            ],
        },

        // -------------------------------------------------------------- condition
        new()
        {
            Key = "condition.imageExists",
            Category = ActionCategory.Condition,
            DisplayName = "Image Exists",
            Description = "True when a reference image is on screen.",
            Parameters =
            [
                Images("image", "Reference pictures", @"C:\images\ok.png",
                    "The pictures to look for, one row each, tried in the order they are listed; the "
                    + "condition is true as soon as one of them turns up."),
                ImageSet(),
                Number("confidence", "Confidence %", 90,
                    "The lowest match confidence that still counts as found, as a whole percent from "
                    + "0 to 100; 90 by default. Lower it for a reference picture that is drawn a "
                    + "little differently each time.", max: 100, advanced: true),
                Region(),
                ..Capture(),
                LookGap(0),
                ..Anchor(),
            ],
        },
        new()
        {
            Key = "condition.imageNotExists",
            Category = ActionCategory.Condition,
            DisplayName = "Image Missing",
            Description = "True when a reference image is not on screen.",
            Parameters =
            [
                Images("image", "Reference pictures", @"C:\images\ok.png",
                    "The pictures to look for, one row each, tried in the order they are listed; the "
                    + "condition is true only when none of them turns up."),
                ImageSet(),
                Number("confidence", "Confidence %", 90,
                    "The lowest match confidence that still counts as found, as a whole percent from "
                    + "0 to 100; 90 by default. Lower it for a reference picture that is drawn a "
                    + "little differently each time.", max: 100, advanced: true),
                Region(),
                ..Capture(),
                LookGap(0),
                ..Anchor(),
            ],
        },
        new()
        {
            Key = "condition.textExists",
            Category = ActionCategory.Condition,
            DisplayName = "Text Exists",
            Description = "True when the screen shows a piece of text.",
            Parameters =
            [
                Text("text", "Text", "Ready",
                    "The text to look for on screen, for example Ready. It is read as one value, so "
                    + "a variable or an expression may stand for it; nothing is filled in by "
                    + "default.",
                    acceptsFormula: true),
                Region(),
                ..Capture(),
                LookGap(0),
                ..Anchor(),
                TextMatch(),
            ],
        },
        new()
        {
            Key = "condition.textNotExists",
            Category = ActionCategory.Condition,
            DisplayName = "Text Missing",
            Description = "True when the screen does not show a piece of text.",
            Parameters =
            [
                Text("text", "Text", "Ready",
                    "The text the screen must not be showing, for example Ready. It is read as one "
                    + "value, so a variable or an expression may stand for it; nothing is filled in "
                    + "by default.",
                    acceptsFormula: true),
                Region(),
                ..Capture(),
                LookGap(0),
                ..Anchor(),
                TextMatch(),
            ],
        },
        new()
        {
            Key = "condition.uiaExists",
            Category = ActionCategory.Condition,
            DisplayName = "UI Element Exists",
            Description = "True when a UI Automation element is present.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
            ],
        },
        new()
        {
            Key = "condition.uiaNotExists",
            Category = ActionCategory.Condition,
            DisplayName = "UI Element Missing",
            Description = "True when a UI Automation element is not present.",
            Parameters =
            [
                ..WindowFilter(),
                Selector(),
            ],
        },
        new()
        {
            Key = "condition.colorEquals",
            Category = ActionCategory.Condition,
            DisplayName = "Color Equals",
            Description = "True when a pixel matches a colour.",
            Parameters =
            [
                Number("x", "X", 0,
                    "The column of the pixel to compare, counted from the coordinate origin above, "
                    + "in pixels; 0 by default."),
                Number("y", "Y", 0,
                    "The row of the pixel to compare, counted from the coordinate origin above, in "
                    + "pixels; 0 by default."),
                ..Anchor(),
                ..Capture(),
                LookGap(0),
                ColorPick("color", "Colour", "#000000",
                    "The colour the pixel is compared with, written as #RRGGBB; black by default."),
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference, 0 to 100 per cent; the default is 5.", max: 100, advanced: true),
            ],
        },
        new()
        {
            Key = "condition.colorsMatch",
            Category = ActionCategory.Condition,
            DisplayName = "Colors Match",
            Description = "True when several points show the colours they are meant to.",
            Parameters =
            [
                Multiline("points", "Points",
                    "One x,y,#RRGGBB per point, separated by a semicolon or a new line.",
                    "100,200,#FF0000; 300,400,#00FF00"),
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference, 0 to 100 per cent; the default is 5.", max: 100, advanced: true),
                Choice("mode", "Mode", ["all", "any"], "all",
                    "Whether every point has to match or one is enough; every point by default.",
                    labels: ["Every point", "Any point"]),
                ..Anchor(),
                ..Capture(),
                LookGap(0),
            ],
        },
        new()
        {
            Key = "condition.colorsExist",
            Category = ActionCategory.Condition,
            DisplayName = "Colour Pattern",
            Description = "True when a colour is on screen with the colours this step names around "
                + "it.",
            Parameters =
            [
                ColorPick("color", "Base colour", "#000000",
                    "The colour in the middle of the pattern: the pixel the colours around it are "
                    + "counted from."),
                Multiline("points", "Colours around it",
                    "The colours that have to be there, one per line, written as the offset from "
                    + "the base colour and the colour itself: 12,-4,#FF0000. Every one of them has "
                    + "to be where it says, and the whole pattern has to fit inside the search "
                    + "region; a pattern that runs off the edge of the region is not found.",
                    "12,-4,#FF0000; -8,3,#00FF00"),
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference, 0 to 100 per cent; the default is 5.", max: 100),
                Region(),
                ..Capture(),
                LookGap(0),
                ..Anchor(),
            ],
        },
        new()
        {
            Key = "condition.colorCoverage",
            Category = ActionCategory.Condition,
            DisplayName = "Colour Share",
            Description = "True when enough of a screen region shows a colour.",
            Parameters =
            [
                ColorPick("color", "Colour", "#000000", "The colour to count."),
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference, 0 to 100 per cent; the default is 5.", max: 100),
                Number("minCoverage", "At least this much", 0,
                    "The least share of the search region that has to show the colour for this to "
                    + "be true, from 0 to 100 per cent; the default is 0, which asks only whether "
                    + "the colour is anywhere in the region. A bar half filled, a button lit up and "
                    + "a gauge past its mark are waited on with a share of their own: the share is "
                    + "measured over the whole region, several rows counted as one, and measuring it "
                    + "walks the whole of it.",
                    min: 0, max: 100),
                Region(),
                ..Capture(),
                LookGap(0),
                ..Anchor(),
            ],
        },
        new()
        {
            Key = "condition.expression",
            Category = ActionCategory.Condition,
            DisplayName = "Boolean Expression",
            Description = "True when what you write reads as true.",
            Parameters =
            [
                Expression("expression", "Expression", "$count > 3 and $name != \"\"",
                    "A boolean expression built from $variables, the usual operators and the "
                    + "built-in functions, for example "
                    + "\"$count > 3 and contains($name, \\\"ok\\\")\". It is read every time the "
                    + "condition is asked, so a wait follows it as the variables change."),
            ],
        },
        new()
        {
            Key = "condition.compare",
            Category = ActionCategory.Condition,
            DisplayName = "Compare",
            Description = "Compares a variable against a value or another variable.",
            Parameters =
            [
                Variable("variable", "Variable", "count",
                    "Variable to test. Suggestions come from the variables earlier steps create.",
                    namesVariable: true),
                Choice("operator", "Operator", ComparisonOperators, "equals",
                    "How the variable is compared against the value; equal to by default.",
                    labels: ComparisonOperatorLabels),
                Variable("value", "Variable or value", "3",
                    "Pick another variable from the list, or type a number or text. "
                    + "Not needed for \"exists\".",
                    required: false, acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "condition.stepResult",
            Category = ActionCategory.Condition,
            DisplayName = "Step Result",
            Description = "Asks how one step of this macro ended up.",
            Parameters =
            [
                PickStep("step", "Step",
                    "The step to ask about. Every step of this macro is offered by its note, its "
                    + "action and its name; a step the run has not reached yet answers \"not "
                    + "reached\" rather than \"no\"."),
                Choice("expected", "Should have", ["ok", "failed", "skipped"], "ok",
                    "How that step should have ended, succeeded by default: it did what it was asked "
                    + "to, it failed, or the run never got to it.",
                    labels: ["Succeeded", "Failed", "Not reached"]),
            ],
        },
        new()
        {
            Key = "condition.listContains",
            Category = ActionCategory.Condition,
            DisplayName = "List Contains",
            Description = "True when a list holds a value.",
            Parameters =
            [
                Variable("list", "List", "$items",
                    "The list to look in. Write it out as a;b;c, or name a variable holding one "
                    + "with $items.", acceptsFormula: true),
                Variable("value", "Value", "1",
                    "The value that has to be in the list. A number is compared as a number, so "
                    + "1 finds 1.0, and text is compared without caring about case.",
                    acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "condition.pathExists",
            Category = ActionCategory.Condition,
            DisplayName = "File or Folder Exists",
            Description = "True when there is a file or a folder at a path.",
            Parameters =
            [
                FilePath("path", "Path", @"C:\data\report.csv",
                    "The file or the folder that has to be there. What is at that path is not "
                    + "looked at, only whether anything is.", PathIntent.Read),
            ],
        },
        new()
        {
            Key = "condition.processRunning",
            Category = ActionCategory.Condition,
            DisplayName = "Program Running",
            Description = "True when a program is running.",
            Parameters =
            [
                Text("name", "Program", "notepad",
                    "The program's name, as it is written in Task Manager and without the "
                    + "\".exe\": notepad, chrome, excel.",
                    acceptsVariables: true, acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "condition.windowExists",
            Category = ActionCategory.Condition,
            DisplayName = "Window Exists",
            Description = "True when a window is open.",
            Parameters =
            [
                MatchText("window", "Match text",
                    "The text the window is recognised by, held up against its title, the program "
                    + "that owns it or the class it registered, the way \"Match by\" says, ignoring "
                    + "case. Leave it empty to ask whether any window at all is open."),
                MatchByChoice(extra: "A rule with no match text asks about every window there is."),
            ],
        },
        new()
        {
            Key = "condition.valueInRange",
            Category = ActionCategory.Condition,
            DisplayName = "Value In Range",
            Description = "True when a value sits between two other values.",
            Parameters =
            [
                Variable("value", "Value", "$count",
                    "The value to place on the number line.", acceptsFormula: true),
                Variable("min", "Lowest", "1",
                    "One end of the range, for example 1; either end may be written first.",
                    acceptsFormula: true),
                Variable("max", "Highest", "10",
                    "The other end of the range, for example 10. Both ends count as inside, so a "
                    + "range of 1 to 10 holds 1 and 10.", acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "condition.dateCompare",
            Category = ActionCategory.Condition,
            DisplayName = "Date Comparison",
            Description = "Compares two dates, times or both.",
            Parameters =
            [
                Variable("left", "Date", "$sys.dateTime",
                    "The date or time to place. Name a variable such as $sys.dateTime, write a date "
                    + "such as 2026-01-31 or 09:30, or work one out with today().",
                    acceptsFormula: true),
                Choice("operator", "Compare", ["before", "after", "same", "sameDay"], "after",
                    "How the first date stands against the second; is after by default. \"The same "
                    + "day\" ignores the time of day, which is what asking whether something has "
                    + "happened today needs.",
                    labels: ["Is before", "Is after", "Is exactly", "Is the same day"]),
                Variable("right", "Against", "2026-01-01",
                    "The date it is compared against, written the same ways, for example 2026-01-01.",
                    acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "condition.group",
            Category = ActionCategory.Condition,
            DisplayName = "Logic Group",
            Description = "Combines several conditions with AND, OR or NOT.",
            Parameters =
            [
                Choice("op", "Logic", ["and", "or", "none"], "and",
                    "How the child conditions are combined, AND by default. Available once the "
                    + "group has two or more.",
                    labels: ["AND (all match)", "OR (any matches)", "NONE (none match)"],
                    enabledBySibling: "conditions"),
                ConditionList("conditions", "Conditions",
                    "Child conditions, combined by the logic above. Groups can be nested."),
            ],
        },
        new()
        {
            Key = "condition.randomChance",
            Category = ActionCategory.Condition,
            DisplayName = "Random Chance",
            Description = "True at random, with a fixed probability.",
            Parameters =
            [
                Number("percent", "Chance %", 50,
                    "Probability that the condition is true, as a whole percent from 0 to 100; 50 by "
                    + "default.", max: 100),
            ],
        },
    ];

    private static readonly Dictionary<string, ActionDefinition> ByKey =
        Definitions.ToDictionary(definition => definition.Key);

    /// <summary>Older keys kept working so JSON written by an earlier build still loads.</summary>
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["condition.variableEquals"] = "condition.compare",
    };

    /// <summary>Catalogue subset used by the condition pickers.</summary>
    public static IReadOnlyList<ActionDefinition> Conditions { get; } =
        [.. Definitions.Where(definition => definition.Category == ActionCategory.Condition)];

    /// <summary>
    /// Everything that can be a step of its own. The conditions are left out on purpose: they say
    /// what has to be true rather than doing something, so they are only ever offered inside an if,
    /// a while or a wait, where the engine knows how to ask them. A hidden action belongs to a
    /// container — a switch case — and is added through that container, so it stays out too.
    /// </summary>
    public static IReadOnlyList<ActionDefinition> RunnableActions { get; } =
        [.. Definitions.Where(definition =>
            definition.Category != ActionCategory.Condition && !definition.Hidden)];

    /// <summary>Finds a definition by its fully qualified key.</summary>
    public static ActionDefinition? Find(string key)
    {
        if (ByKey.TryGetValue(key, out var definition))
        {
            return definition;
        }

        return Aliases.TryGetValue(key, out var current) && ByKey.TryGetValue(current, out var aliased)
            ? aliased
            : null;
    }

    /// <summary>
    /// The catalogue entries for a fixed list of keys, in the order given. Used where a nested
    /// editor accepts one particular kind of child, such as the cases of a switch.
    /// </summary>
    public static IReadOnlyList<ActionDefinition> ForKeys(IReadOnlyList<string> keys)
        => [.. keys.Select(Find).OfType<ActionDefinition>()];

    /// <summary>Line-art icon shared by every action of a category.</summary>
    public static Geometry? IconFor(ActionCategory category) => category switch
    {
        ActionCategory.Control => ControlIcon,
        ActionCategory.File => FileIcon,
        ActionCategory.Data => DataIcon,
        ActionCategory.Clipboard => ClipboardIcon,
        ActionCategory.Process => ProcessIcon,
        ActionCategory.System => SystemIcon,
        ActionCategory.Window => WindowIcon,
        ActionCategory.Input => InputIcon,
        ActionCategory.Vision => VisionIcon,
        ActionCategory.Ocr => OcrIcon,
        ActionCategory.Uia => UiaIcon,
        ActionCategory.Browser => BrowserIcon,
        ActionCategory.Script => ScriptIcon,
        ActionCategory.Condition => ConditionIcon,
        _ => null,
    };

    // ------------------------------------------------------------------ builders

    private static ActionParameter Text(string name, string label, string placeholder = "",
        string hint = "", bool required = true, string defaultValue = "", bool advanced = false,
        bool foreign = false, bool acceptsVariables = false, bool acceptsFormula = false,
        string[]? onlyWith = null, string onlyField = "algorithm")
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Text,
            Placeholder = placeholder,
            Hint = hint,
            Required = required,
            DefaultValue = defaultValue,
            Advanced = advanced,
            ForeignText = foreign,
            AcceptsVariables = acceptsVariables,
            AcceptsFormula = acceptsFormula,
            AppliesWhen = onlyWith is null ? string.Empty : onlyField,
            AppliesWhenValues = onlyWith ?? [],
        };

    private static ActionParameter Multiline(string name, string label, string hint = "",
        string placeholder = "", bool required = true, bool foreign = false, bool advanced = false,
        bool acceptsFormula = false)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.MultilineText,
            Hint = hint,
            Placeholder = placeholder,
            Required = required,
            ForeignText = foreign,
            Advanced = advanced,
            AcceptsFormula = acceptsFormula,
        };

    /// <summary>
    /// A value written as an expression. The editor offers the variables in scope and the
    /// built-in function names while the user types, and checks the result as it changes.
    /// </summary>
    private static ActionParameter Expression(string name, string label, string placeholder = "",
        string hint = "", bool required = true, string defaultValue = "")
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Expression,
            Placeholder = placeholder,
            Hint = hint,
            Required = required,
            DefaultValue = defaultValue,
        };

    /// <summary>A nested list of child steps.</summary>
    private static ActionParameter Steps(string name, string label, string hint)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Steps,
            Hint = hint,
            Required = false,
        };

    /// <summary>A single condition picked from the <c>condition.*</c> actions.</summary>
    private static ActionParameter Condition(string name, string label, string hint)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Condition,
            Hint = hint,
            Required = true,
        };

    /// <summary>A list of conditions, used by the AND / OR / NOT logic group.</summary>
    private static ActionParameter ConditionList(string name, string label, string hint)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Steps,
            Hint = hint,
            Required = true,
            ConditionsOnly = true,
        };

    /// <summary>A list of switch cases, so the nested editor only offers <c>control.case</c>.</summary>
    private static ActionParameter CaseList(string name, string label, string hint)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Steps,
            Hint = hint,
            Required = false,
            AddLabelKey = "Add.Case",
            ChildKeys = ["control.case"],
        };

    /// <summary>Name of a variable, edited with a suggestion list.</summary>
    private static ActionParameter Variable(string name, string label, string placeholder, string hint,
        bool required = true, bool namesVariable = false, string defaultValue = "",
        bool acceptsFormula = false, bool advanced = false)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Variable,
            Placeholder = placeholder,
            Hint = hint,
            Required = required,
            NamesVariable = namesVariable,
            DefaultValue = defaultValue,
            AcceptsFormula = acceptsFormula,
            Advanced = advanced,
        };

    /// <summary>
    /// A step of the macro being written, chosen from the steps that are in it. The choices are not
    /// the catalogue's — only the editor knows what is in the macro — so the dialog fills them in
    /// from the editor's own list, by the note, the action and the step's name.
    /// </summary>
    private static ActionParameter PickStep(string name, string label, string hint)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Step,
            Hint = hint,
        };

    /// <summary>
    /// A program or a folder the machine can hand over from a dialog of its own. It is written in
    /// a plain box rather than a variable field because what goes there is an ordinary path, and
    /// the macros-folder wording of <see cref="FilePath"/> would be wrong for a program.
    /// </summary>
    private static ActionParameter PickablePath(string name, string label, string placeholder,
        string hint, PathIntent intent, string filter = "", bool required = true,
        bool advanced = false)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Text,
            Placeholder = placeholder,
            Hint = hint,
            Required = required,
            PathIntent = intent,
            PathFilter = filter,
            Advanced = advanced,
        };

    private static ActionParameter Number(string name, string label, decimal? defaultValue = null,
        string hint = "", decimal min = 0m, decimal max = StepMeta.LongestPauseMs,
        bool advanced = false, string[]? onlyWith = null, string onlyField = "algorithm",
        bool required = true, bool emptiesToEdge = false)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Number,
            Hint = hint,
            // A number with nothing written in it is a field nobody has answered rather than a
            // zero, which is what the few fields that start empty are for.
            DefaultValue = defaultValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Required = required,
            Minimum = min,
            Maximum = max,
            // Every pause in the catalogue ends its name in "Ms", so the editor can offer the
            // unit dropdown without each call site having to ask for it. A number that is not a
            // length of time is simply never named that way.
            IsDuration = name == "ms" || name.EndsWith("Ms", StringComparison.Ordinal),
            EmptiesToEdge = emptiesToEdge,
            Advanced = advanced,
            AppliesWhen = onlyWith is null ? string.Empty : onlyField,
            AppliesWhenValues = onlyWith ?? [],
        };

    private static ActionParameter Toggle(string name, string label, bool defaultValue = false,
        string hint = "", bool advanced = false, string[]? onlyWith = null,
        string onlyField = "algorithm")
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Bool,
            Hint = hint,
            DefaultValue = defaultValue ? "true" : "false",
            Advanced = advanced,
            AppliesWhen = onlyWith is null ? string.Empty : onlyField,
            AppliesWhenValues = onlyWith ?? [],
        };

    private static ActionParameter Choice(string name, string label, string[] options,
        string defaultValue, string hint = "", string[]? labels = null,
        string enabledBySibling = "", bool advanced = false, string[]? onlyWith = null,
        string onlyField = "algorithm")
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Choice,
            Options = options,
            OptionLabels = labels ?? [],
            DefaultValue = defaultValue,
            Hint = hint,
            EnabledBySibling = enabledBySibling,
            Advanced = advanced,
            AppliesWhen = onlyWith is null ? string.Empty : onlyField,
            AppliesWhenValues = onlyWith ?? [],
        };

    private static ActionParameter KeyBind(string name, string label, string hint = "",
        bool required = true)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Key,
            Placeholder = "e.g. F5",
            Hint = hint,
            Required = required,
        };

    /// <summary>
    /// The keys of a combination, such as Ctrl+Shift+S. One box holds them all, and the keyboard
    /// drawn on screen adds to it a key at a time — a combination written from memory is a
    /// combination spelled wrong, and this is the spelling the step has to get right.
    /// </summary>
    private static ActionParameter KeyChord(string name, string label, string hint = "")
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Keys,
            Placeholder = "Ctrl+Shift+S",
            Hint = hint,
            Required = true,
        };

    private static ActionParameter ColorPick(string name, string label, string defaultValue = "#000000",
        string hint = "", bool required = true, bool advanced = false)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Color,
            DefaultValue = defaultValue,
            Hint = hint,
            Required = required,
            Advanced = advanced,
            // A colour is read as a value like any other: it may be written out, or held in a
            // variable the macro filled in earlier.
            AcceptsFormula = true,
        };

    /// <summary>
    /// A picture the action looks for on screen. It is picked from the file system or taken
    /// straight off the screen, and the editor shows what it points at.
    /// </summary>
    private static ActionParameter Image(string name, string label, string placeholder = "",
        string hint = "")
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Image,
            Placeholder = placeholder,
            Hint = hint,
            Required = true,
        };

    /// <summary>
    /// The pictures an action looks for on screen, one row each. A search tries them in the order
    /// they are listed and goes with the first one that turns up, so one step covers a thing that
    /// is drawn differently from one screen to the next instead of needing a branch for each.
    /// </summary>
    private static ActionParameter Images(string name, string label, string placeholder = "",
        string hint = "")
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Images,
            Placeholder = placeholder,
            Hint = hint,
            Required = true,
        };

    /// <summary>
    /// Which of the pictures a step listed it goes with. A step that lists several pictures is a
    /// step that covers a thing drawn differently from one screen to the next, and what it wants
    /// of them is not always "whichever turns up first": a screen may count as reached only when
    /// the whole set is there, or the closest of two similar icons may be the one to click.
    /// </summary>
    private static ActionParameter ImageSet()
        => Choice("imageSet", "Which of the pictures", ["first", "best", "every"], "first",
            "Which of the pictures above this step goes with. The first that turns up, the "
            + "default, is what a button drawn two ways asks for. The surest of them looks at "
            + "every picture and takes the one whose closest match scored highest, which is how "
            + "the closer of two similar icons is picked. Every one of them asks for the whole "
            + "set at once, so a step acts only on a screen showing all of them and finds nothing "
            + "when one is missing; where a match is handed over it is the first picture's, and "
            + "the hit that is handed over is counted inside that picture as usual.",
            labels: ["The first that turns up", "The surest of them", "Every one of them"],
            advanced: true);

    /// <summary>
    /// The places on the screen an action looks at. It is a list rather than a line of text so that
    /// a rectangle can be dragged out on the screen instead of spelled out in numbers, and so that
    /// looking at two halves of a screen reads as two things rather than as punctuation.
    /// </summary>
    private static ActionParameter Region()
        => new()
        {
            Name = "region",
            Label = "Search region",
            Kind = ActionParameterKind.Region,
            Hint = RegionHint,
            Required = false,
        };

    /// <summary>
    /// The one part of the screen a step reads or copies, written as four numbers it works on
    /// directly rather than as a rectangle of its own: the corners are ordinary number fields, each
    /// of which may be written as an expression, and the region picker fills all four at once. They
    /// start empty and a number left unsaid is not read as a zero: the rectangle goes to the edge of
    /// the screen instead, so a step that says nothing about where to look reads all of it. A number
    /// guessed at the corner of the screen would be a step that quietly reads the wrong thing.
    /// </summary>
    private static ActionParameter[] RegionNumbers()
    =>
    [
        Number("x", "Top-left X",
            hint: "How far the left edge of the region is from the left of the screen, in pixels. Under "
            + "a window anchor it is counted from that window's own left edge. Left out, counting "
            + "starts at that edge, and with the other three left out too the whole screen is read. "
            + "“Pick region” fills all four in at once.", required: false, emptiesToEdge: true),
        Number("y", "Top-left Y",
            hint: "How far the top edge of the region is from the top of the screen, in pixels. Under a "
            + "window anchor it is counted from that window's own top edge. Left out, counting "
            + "starts at that edge.", required: false, emptiesToEdge: true),
        Number("width", "Width to the right",
            hint: "How wide the region is, in pixels: the distance from the left edge to the right one. "
            + "Left out, it reaches the right edge of the screen. At least 1.", min: 1, required: false,
            emptiesToEdge: true),
        Number("height", "Height downward",
            hint: "How tall the region is, in pixels: the distance from the top edge to the bottom one. "
            + "Left out, it reaches the bottom edge of the screen. At least 1.", min: 1, required: false,
            emptiesToEdge: true),
    ];

    /// <summary>
    /// A run of key combinations: one row per combination, in the order they are pressed. It is a
    /// list rather than one long line of keys because a combo is a sequence of presses, and the
    /// gap between them is part of what it is.
    /// </summary>
    private static ActionParameter KeySequence()
        => new()
        {
            Name = "keys",
            Label = "The run",
            Kind = ActionParameterKind.KeySequence,
            Required = true,
            Hint = "One row per combination, pressed in the order they are listed. Take the keys off "
                + "the keyboard drawn on screen to be sure of the spelling: the two Ctrl keys and "
                + "the two Shift keys are named apart, and the machine tells them apart. A row may "
                + "say how long its own combination is held and how long to wait after it, for the "
                + "one place where the beat is not the same as everywhere else; leave those empty "
                + "to use the run's own.",
        };

    /// <summary>
    /// The results a step may hand over, edited as one line each: what the engine works out while
    /// it runs, with a box beside the ones the macro should keep. Only what is ticked is written,
    /// each under a name of its own, because a step that reports eight things should not put eight
    /// variables in front of somebody who wanted one.
    /// </summary>
    private static ActionParameter Returns()
        => new()
        {
            Name = "returns",
            Label = "Returns",
            Kind = ActionParameterKind.Returns,
            Required = false,
            Hint = "Tick the results this step should hand over. Each ticked one becomes a variable "
                + "of its own, named after the note on the step and the name of the result, which "
                + "the row itself may change, and can then be read by later steps. Nothing "
                + "ticked means the step leaves no result behind.",
        };

    /// <summary>One result an action can hand over.</summary>
    private static ActionReturn Return(string key, string label, bool primary = false,
        string hint = "", ActionReturnType type = ActionReturnType.Text)
        => new()
        {
            Key = key,
            Label = label,
            Hint = hint,
            Type = type,
            Primary = primary,
        };

    /// <summary>
    /// What a search hands over: the one hit it went with, and — when the step asks for them — every
    /// hit it found. Both are records, because the middle of a hit, the rectangle it was found in,
    /// how sure the reading was and the writing that was read are several answers about one thing:
    /// they travel together under names rather than as a variable each, and a macro reads one part
    /// of it with <c>field(变量, "x")</c>.
    ///
    /// The middle of a hit and its top-left corner are two different things and both are in the
    /// record: the middle is what a click aims at, and the corner is where a search region starts
    /// counting, so a macro that looks again at what it just found does not have to work out which
    /// of the two it is reading.
    /// </summary>
    private static ActionReturn[] Hits() =>
    [
        Return("match", "Hit", primary: true, type: ActionReturnType.Record,
            hint: "Where it was found and everything else about it, as one record; empty when it "
                + "was not there. x and y are the middle (the point this action clicks or moves "
                + "to), left and top are the corner a search region counts from, width and height "
                + "are its size, score is how sure the reading was, box is the whole hit written "
                + "as left,top,width,height, and text is what was read where the action reads "
                + "writing. Read one part of it with field($name, \"x\")."),
        Return("every", "Every match", type: ActionReturnType.List,
            hint: "Every hit that was found, each shaped like the record above, in the order they "
                + "are counted in. Walk them with forEach or count them with count(); name one of "
                + "them in a search region to look at that place next. Only written when this box "
                + "is ticked."),
    ];

    /// <summary>
    /// Where the picture sits, handed over beside the picture itself so a later step can search the
    /// same place without working the position out again: one record, so the rectangle goes into a
    /// search region as it stands.
    /// </summary>
    private static ActionReturn[] Place() =>
    [
        Return("place", "Place", type: ActionReturnType.Record,
            hint: "Where the picture was taken, as one record. x and y are the middle of it, which "
                + "is the point a step aims at; left and top are the corner it starts at and width "
                + "and height are its size in screen pixels: the picture's own size, so a capture "
                + "that ran off the edge of the screen comes back smaller than it asked for; box "
                + "is the whole rectangle written as left,top,width,height, ready to be used as a "
                + "search region. Read one part of it with field($name, \"left\")."),
    ];

    /// <summary>
    /// How much of the search region shows the colour: a bar rather than a place, which is what a
    /// health bar, a progress bar and a gauge are read with. It costs a walk of the whole region to
    /// measure, so it is only measured by the steps that ask for it.
    /// </summary>
    private static ActionReturn[] Coverage() =>
    [
        Return("coverage", "Colour coverage %", type: ActionReturnType.Number,
            hint: "How much of the search region shows the colour, from 0 to 100 per cent in "
                + "tenths, with several search rows counted as one region. Written whether or not "
                + "the colour was found, so a share below the least the step accepts can be read."),
    ];

    /// <summary>Mouse button picker shared by every input action that clicks.</summary>
    private static ActionParameter Button()
        => Choice("button", "Button", ["left", "right", "middle", "back", "forward"], "left",
                    "Which mouse button; the left one by default.");

    /// <summary>
    /// The button of the virtual controller, listed with the names the macro writes, which are the
    /// ones printed on the pad.
    /// </summary>
    private static ActionParameter GamepadButton()
        => Choice("button", "Button", [.. GamepadNames.Buttons], "a",
            "The button to press, named the way it is printed on the pad; A by default.",
            labels:
            [
                "A", "B", "X", "Y",
                "LB", "RB", "LT", "RT",
                "LS", "RS",
                "D-pad up", "D-pad down", "D-pad left", "D-pad right",
                "Start", "Back", "Guide",
            ]);

    /// <summary>
    /// The hint every search-region field shares, so the shape of a region is written once. A
    /// region may hold several rectangles, which is how one step looks in two windows at once.
    /// </summary>
    private const string RegionHint =
        "Where to look: one row per place, and there may be several. A row is x, y, width and "
        + "height, dragged out with “pick region” or typed in; a row may also name a variable, "
        + "either one holding a picture an earlier Capture took, which is searched inside, or one "
        + "holding a rectangle written as x,y,width,height. Empty searches the whole screen, which "
        + "is the default.";

    /// <summary>
    /// The three ways that compare the two pictures pixel for pixel. They are the ones a search can
    /// come back with several hits from, so the questions about a second or third hit belong to
    /// them and to nothing else.
    /// </summary>
    /// <remarks>
    /// A method rather than a field on purpose: the catalogue is a static field initializer, and
    /// static fields are built in the order they are written, so a field declared below it would
    /// still be null by the time these entries are built — which is a gate that silently does
    /// nothing.
    /// </remarks>
    private static string[] PixelWays() => ["normed", "correlated", "difference"];

    /// <summary>
    /// Which of several hits a step means, shared by the actions that can find more than one. Hits
    /// are counted from the top left, the order a person counts them in on a screenshot.
    /// </summary>
    private static ActionParameter MatchIndex(bool pixelsOnly = false)
        => Number("matchIndex", "Match number", 1,
            "Which of the hits to use, numbered the way “count them in” says, from 1. The default "
            + "is 1. More hits than were found is treated as finding nothing.",
            min: 1, max: 200, advanced: true, onlyWith: pixelsOnly ? PixelWays() : null);

    /// <summary>
    /// How a reference picture is looked for, shared by the picture finders. It is one list because
    /// the three ways of comparing two pictures pixel for pixel and the one way of pairing up their
    /// features answer the same question: a step that pairs up features never compares pixels, so
    /// two pickers would leave one of them doing nothing.
    /// </summary>
    private static ActionParameter MatchAlgorithm()
        => Choice("algorithm", "Recognition algorithm",
            ["normed", "correlated", "difference", "feature"], "normed",
            "How the reference picture is recognised. Normalised, the default, takes the brightness "
            + "out first, so an area that has gone brighter or darker still matches. Correlated "
            + "does not, which is steadier for a very small picture. Pixel for pixel wants a copy, "
            + "which a flat icon or a screenshot of a dialog is. Features pairs up what stands out "
            + "in both pictures, so it still finds the thing at another size: slower, and it reports "
            + "a single hit.",
            labels: ["Normalised", "Correlated", "Pixel for pixel", "Features"], advanced: true);

    /// <summary>
    /// How fresh the picture a step looks at has to be, shared by every step that looks at the
    /// screen. How the pixels come off this machine is a setting rather than a step's business: the
    /// machine answers the same way for every step of every macro that runs on it.
    /// </summary>
    /// <remarks>
    /// Spread into the parameter list rather than written out in each of the steps that read the
    /// screen, so that the field that makes a macro read the same place once instead of twenty
    /// times is offered everywhere that reading happens.
    /// </remarks>
    private static ActionParameter[] Capture() => [FrameMaxAge()];

    /// <summary>
    /// Where a picture that was just taken is written out, for a macro that wants it to outlive the
    /// run. Left empty the picture only goes to the variable, which is what a step that captures
    /// something to look at it right away wants; filled in it is written as a PNG beside it, so one
    /// step takes the picture and keeps it.
    /// </summary>
    private static ActionParameter CaptureFile()
        => FilePath("path", "Save to file", "shot.png",
            "File the picture is written to; empty by default, which keeps it in the variable "
            + "alone. The format is PNG either way, and a file of the same name is replaced.",
            PathIntent.Write, "*.png", required: false);

    /// <summary>
    /// How long a picture a step reads may have been taken already, shared by every step that looks
    /// at the screen: what makes a macro that looks at the same window several times in a row read
    /// it once instead.
    /// </summary>
    private static ActionParameter FrameMaxAge()
        => Number("frameMaxAgeMs", "Accept a picture up to", 0,
            "How old a picture this step will accept, in milliseconds, from 0 to 60000; the default "
            + "is 0, which reads the screen every time. A step that allows a picture taken within "
            + "the last few milliseconds is handed one an earlier step took of the same window, or "
            + "of the screen, so looking at the same place again and again does not read it again "
            + "and again. The first reading with this allowed takes the whole window or the whole "
            + "screen, which is what the readings after it save.",
            min: 0, max: 60000, advanced: true);

    /// <summary>
    /// How long a step that looks at the screen leaves between two looks. A step that looks once —
    /// or a condition, which is asked for its answer as often as a block feels like asking — reads
    /// the same place every pass of a loop, and most of those readings are of a picture that has
    /// not changed; the number written here is the least time between two of them.
    /// </summary>
    private static ActionParameter LookGap(decimal defaultMs)
        => Number("intervalMs", "Time between looks", defaultMs,
            "The least time this step leaves between two looks at the screen, in milliseconds, from "
            + $"0 to 60000; the default is {Written(defaultMs)}, which looks again as soon as the "
            + "step is asked to. Writing a gap is worth it when the step sits inside a loop, where a "
            + "step that reads the same place on every pass spends most of its time reading a "
            + "picture that has not changed. Seven milliseconds is about 144 looks a second.",
            min: 0, max: 60000, advanced: true);

    /// <summary>
    /// The same field for a step that waits for something to turn up: there it is how often the
    /// screen is looked at while the step waits, which is the same question — how many looks a
    /// second the macro spends — asked of a step that has its own clock. It is left empty rather
    /// than filled with a number so that a step nobody has answered for looks once a screen
    /// refresh, which is as often as looking can tell anything and follows the display it runs on.
    /// Leaving it empty is an answer here, so the field is optional and an empty box is left out of
    /// the step rather than written down as a zero — a zero would mean "look again as fast as the
    /// last look finishes", which is the other answer this field can be given.
    /// </summary>
    private static ActionParameter LookPoll()
        => Number("intervalMs", "Time between looks", null,
            "How often the screen is looked at while this step waits for something to turn up, in "
            + "milliseconds, from 0 to 60000. Left empty it looks again once the screen has been "
            + "refreshed, which is 16 ms on a 60 Hz display and 4 ms on a 240 Hz one; 0 looks again "
            + "as soon as the look before it is done. Raise it when waiting should cost less, lower "
            + "it when what the step watches is only there for a moment.",
            min: 0, max: 60000, advanced: true, required: false);

    /// <summary>A number as the hint writes it, however the file was written.</summary>
    private static string Written(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A colour of the reference picture that takes no part in the comparing, shared by the picture
    /// finders: how a step ignores the part of a picture that keeps changing.
    /// </summary>
    private static ActionParameter SkipColour()
        => ColorPick("ignoreColor", "Colour to ignore", string.Empty,
            "Pixels of the reference picture that are this colour take no part in the comparing. "
            + "For a part of the picture that keeps changing, such as a number over a button or a "
            + "progress bar. Empty compares the whole picture, which is the default.",
            required: false, advanced: true);

    /// <summary>How many pairs of features have to line up, for the finders that look by features.</summary>
    private static ActionParameter MinFeatures()
        => Number("minFeatures", "Least feature pairs", 6,
            "Only used when the algorithm is features: how many pairs have to line up before the "
            + "picture counts as found. From 1 to 500, more is stricter; the default is 6.",
            min: 1, max: 500, advanced: true, onlyWith: ["feature"]);

    /// <summary>
    /// What order the hits are counted in, shared by everything that finds more than one: a colour
    /// in an area, a picture on the screen, a line of writing. Counting by size is offered only to
    /// the finders whose hits can be different sizes — a colour hit is one pixel, so every hit a
    /// colour search reports is the same size and the choice could not change the answer.
    /// </summary>
    private static ActionParameter MatchOrder(bool bySize = true, bool pixelsOnly = false) => bySize
        ? Choice("orderBy", "Count them in", ["reading", "score", "area", "random"], "reading",
            "What order the hits are numbered in, which is what the match number counts. Reading, "
            + "the default, is down the screen first and then across. Score takes the surest one "
            + "wherever it is. Size takes the biggest box first, which is how a step asks for a "
            + "whole banner rather than a piece of it. Random shuffles them, for a step that must "
            + "not always take the same one of several.",
            labels: ["Reading order", "Surest first", "Biggest first", "Shuffled"], advanced: true,
            onlyWith: pixelsOnly ? PixelWays() : null)
        : Choice("orderBy", "Count them in", ["reading", "score", "random"], "reading",
            "What order the hits are numbered in, which is what the match number counts. Reading, "
            + "the default, is down the screen first and then across. Score takes the pixel closest "
            + "to the colour, wherever it is. Random shuffles them, for a step that must not always "
            + "take the same one of several.",
            labels: ["Reading order", "Surest first", "Shuffled"], advanced: true,
            onlyWith: pixelsOnly ? PixelWays() : null);

    /// <summary>
    /// How a step's input is sent, shared by every action that presses a key or a mouse button.
    /// In front is how input has always been sent; the other two are what let a macro work on a
    /// window it does not have the focus of, or look like real hardware.
    /// </summary>
    /// <remarks>
    /// The window the messages are posted at is recognised by the same three parts the window
    /// actions use — what to look for, which part of a window to compare it with, and how to
    /// compare them — because a title that moves with the document or the interface language is
    /// otherwise the only handle there is on it. Only the background mode reads that window, so
    /// the three are asked for only while that is how this step's input is sent.
    /// </remarks>
    /// <param name="extra">
    /// One more sentence for the mode picker, for a step the posted-input mode cannot carry out:
    /// that mode hands the window messages and never moves the pointer, so everything a step asks
    /// about the pointer itself has to be said here rather than found out at the end of a run.
    /// </param>
    private static ActionParameter[] Delivery(string extra = "") =>
    [
        Choice("inputMode", "Input mode", ["foreground", "background", "driver"], "foreground",
            "How this step's input is sent. In front goes to whatever window has the focus. "
            + "Background posts the messages at the target window instead, which needs no focus "
            + "and leaves the on-screen pointer where it is. Driver sends it through a virtual "
            + "USB device, which needs the usbip-win2 driver and the VIIPER server; the settings "
            + "window shows whether this machine has them."
            + (extra.Length > 0 ? " " + extra : string.Empty),
            labels: ["In front", "Background (posted)", "Driver (virtual device)"],
            advanced: true),
        ..TargetWindowRule(),
    ];

    /// <summary>
    /// The window background input posts its messages at, together with how to recognise it. A
    /// title is what a person reads off the title bar, but it changes with the document and the
    /// language; the program that owns the window and the class it registered hold still, which is
    /// what a macro wants when the window it types into keeps coming back under a different name.
    /// </summary>
    private static ActionParameter[] TargetWindowRule() =>
    [
        MatchText("targetWindow", "Target window",
            "The window background input posts its messages at, recognised with the same two "
            + "fields a window action uses: the text is held up against the window's title, or "
            + "against the program that owns it or the class it registered, the way \"Match by\" "
            + "says, ignoring case. Only the background mode reads it, and a background step that "
            + "names no window fails before it sends anything.",
            advanced: true, onlyWith: [BackgroundMode], onlyField: "inputMode"),
        MatchByChoice("targetHow", advanced: true, onlyWith: [BackgroundMode],
            onlyField: "inputMode", extra: "Only the background input mode uses it."),
    ];

    /// <summary>What the input mode says while the messages are posted at the named window.</summary>
    private const string BackgroundMode = "background";

    /// <summary>
    /// What an action's coordinates are measured from, shared by every action that names a
    /// position or a rectangle. Screen pixels are what every macro written before this setting
    /// used; the other two make the numbers follow a window, which is what stops a macro from
    /// aiming at the wrong place the moment the window is dragged somewhere else.
    /// <paramref name="withElement"/> adds a fourth choice, measuring from a control rather than a
    /// window. Only the actions that put the pointer somewhere offer it: a control is what a person
    /// actually aims at, and the actions that merely read a point would grow a field for nothing.
    /// </summary>
    /// <param name="withElement">
    /// True for an action that can measure from a control, which the element picker fills in.
    /// </param>
    /// <remarks>
    /// A window is recognised by the same two fields the window actions use — what to look for, and
    /// the one choice that says what it is held against and how — because the steps that look at the
    /// screen read their pixels out of the window those numbers are counted from, and a title that
    /// moves with the document or the interface language is otherwise the only handle there is on it.
    /// </remarks>
    private static ActionParameter[] Anchor(bool withElement = false) =>
    [
        Choice("anchorMode", "Coordinates from",
            withElement ? ["screen", "window", "client", "element"] : ["screen", "window", "client"],
            "screen",
            "What this step's coordinates are measured from. Screen is a pixel position on the "
            + "desktop, which is how macros were written before this setting existed. Window and "
            + "client measure from the window named below instead, so the step lands in the same "
            + "place inside that window after it has been moved or resized. Element measures from a "
            + "control, found by the selector below, so the numbers follow that control around the "
            + "screen and through a list that scrolls.",
            labels: withElement
                ? ["Screen pixels", "Window top-left", "Window client area", "UI element top-left"]
                : ["Screen pixels", "Window top-left", "Window client area"],
            advanced: true),
        MatchText("anchorWindow", "Match text",
            "The text the window is recognised by, held up against its title, the program that owns "
            + "it or the class it registered, the way \"Match by\" says, ignoring case. The pixels "
            + "this step reads come out of that window. Leave it empty for the window in front.",
            advanced: true, onlyWith: ["window", "client"], onlyField: "anchorMode"),
        MatchByChoice("anchorHow", advanced: true, onlyWith: ["window", "client"],
            onlyField: "anchorMode", extra: "The screen and element origins do not use it."),
        ..(withElement ? AnchorElement() : Array.Empty<ActionParameter>()),
    ];

    /// <summary>
    /// The switch the mouse actions share for taking the place they act on from the pointer rather
    /// than from a pair of numbers. A macro that clicks wherever the user left the mouse has no way
    /// of writing that down as coordinates: the numbers it would write are of the pointer as it
    /// stood while the macro was being written.
    /// </summary>
    private static ActionParameter AtPointer()
        => Toggle(CursorPlaceToggle, "At the pointer's place", false,
            "With this on the step writes no coordinates and works wherever the pointer is at the "
            + "time. Use it when the place is decided by where the user left the mouse; the X and Y "
            + "fields are then not written and not read.");

    /// <summary>
    /// The two halves of the place a mouse action acts on. They are asked for only while that place
    /// is not taken from the pointer, so a step set that way carries no coordinates at all.
    /// </summary>
    private static ActionParameter[] PointerPosition(string xHint, string yHint) =>
    [
        Number("x", "X", 0, xHint, onlyWith: [CursorPlaceOff], onlyField: CursorPlaceToggle),
        Number("y", "Y", 0, yHint, onlyWith: [CursorPlaceOff], onlyField: CursorPlaceToggle),
    ];

    /// <summary>Name of the switch that takes a mouse step's place from the pointer.</summary>
    private const string CursorPlaceToggle = "useCursor";

    /// <summary>What that switch says while the step is still counting from its own numbers.</summary>
    private const string CursorPlaceOff = "false";

    /// <summary>
    /// The control a step measures from when it measures from one. The element picker fills this in
    /// the same way it fills in the selector of a step that looks for a control.
    /// </summary>
    private static ActionParameter[] AnchorElement() =>
    [
        Text("anchorSelector", "Anchor element", "Button[name='Save']", required: false,
            hint: "The control the coordinates are measured from, for example Button[name='Save']; "
                  + "the element picker can take it off the screen. It is looked up again on every "
                  + "run, so the numbers stay on the control after it has moved. The other "
                  + "coordinate origins do not use it.",
            advanced: true),
    ];

    /// <summary>
    /// How the pointer travels, shared by the moves that can bend their path. The straight line
    /// is the default so that a macro written before this setting existed still moves the same way.
    /// </summary>
    private static ActionParameter Movement()
        => Choice("style", "Movement", ["direct", "smooth", "human"], "direct",
            "How the pointer travels. A bent path bows out of the straight line; a hand-like one "
            + "bows, drifts slightly and eases in and out, so the move does not look like a machine "
            + "drew it. A bent path is travelled rather than jumped, so an instant move borrows "
            + "200 ms to have somewhere to bend.",
            labels: ["Straight line", "Bent path", "Hand-like"], advanced: true);

    /// <summary>
    /// 落手与节奏。Tuning a step that is nearly right means reading these together: how far the
    /// click may miss the place it aimed at, whether the pointer is put back afterwards, how many
    /// times the step happens and how long it waits in between, and whether the place still has to
    /// be right a moment later. Each is declared once here and drawn as one run at the end of the
    /// basic page, rather than written out again inside every action and split across two pages by
    /// the one field of the run that happened to be folded away. The names they write into the file
    /// are unchanged; what changes is where the form draws them.
    /// </summary>
    private static ActionParameter Aim()
        => Number("variance", "Aim within", 0,
            "How far the click may land from the place it aimed at, in pixels; the default is 0, "
            + "which lands on exactly that place. Given a number, every click is placed at random "
            + "within that many pixels either way, so a run of clicks is spread over the button "
            + "instead of landing on one pixel, which is what a button that only counts a press "
            + "somewhere inside it, and a game that notices the same pixel every time, are both "
            + "about. Keep it small enough that the whole spread stays inside the thing being "
            + "clicked.",
            min: 0, max: 100000);

    /// <summary>把指针放回原处：落手之后鼠标还在用户离开的地方。</summary>
    private static ActionParameter PutBack()
        => Toggle("moveBack", "Put the pointer back", false,
            "Move the pointer back to where it was before the click, so the mouse is left where the "
            + "user left it. It does nothing in background mode, where the pointer on screen never "
            + "moved in the first place.");

    /// <summary>按下去之后停多久。0 是普通的一次快按。</summary>
    private static ActionParameter Hold(decimal defaultMs, string what, string whenZero = "")
        => Number("holdMs", "Hold", defaultMs,
            $"How long the {what} stays down, in milliseconds; {defaultMs:0} by default."
            + (whenZero.Length > 0 ? " " + whenZero : string.Empty));

    /// <summary>重复几次、隔多久。</summary>
    private static ActionParameter[] Beat() =>
    [
        Number("repeat", "Repeat", 1, "How many times this step is sent in a row, from 1 up; 1 by "
            + "default. How long to wait between one and the next is the interval beside it.", min: 1),
        Number("intervalMs", "Interval", 0,
            "How long to wait between one and the next, in milliseconds; 0, the default, sends them "
            + "one after the other with no pause."),
    ];

    /// <summary>拖拽到那头之后再按一会儿才松手。</summary>
    private static ActionParameter Settle()
        => Number("settleMs", "Hold at the end", 0,
            "How long the button stays down at the end of the drag before it is released, in "
            + "milliseconds; the default is 0, which lets go as soon as the pointer arrives. A drop "
            + "that is only answered once the thing has been held over its new place for a moment, "
            + "which is how a drag onto a slot becomes a drop into that slot rather than a pass "
            + "over it.",
            min: 0);

    /// <summary>看清楚再落手：等一会儿再看一眼，东西还在才动手。</summary>
    private static ActionParameter Recheck(string what)
        => Number("recheckMs", "Look again after", 0,
            $"How long to wait before looking a second time, in milliseconds; the default is 0, "
            + $"which acts as soon as the {what} turns up. Given a number, the step waits that long, "
            + $"looks again and acts only if the {what} is still there, so something that flashed "
            + "past, or a screen that has already moved on, is not acted on by mistake. A second "
            + "look that finds nothing counts as finding nothing at all: it is one look rather than "
            + "another wait, so something that has gone is not waited for.",
            min: 0);

    /// <summary>
    /// A file or folder path, which may be written as an expression. The intent decides which
    /// dialog the button beside the field opens, and the filter which files it shows first.
    /// </summary>
    private static ActionParameter FilePath(string name, string label, string placeholder, string hint,
        PathIntent intent, string filter = "", string[]? onlyWith = null,
        string onlyField = "algorithm", bool required = true)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Variable,
            Placeholder = placeholder,
            Hint = hint + " A path on its own is taken to be inside the macros folder.",
            Required = required,
            PathIntent = intent,
            PathFilter = filter,
            AppliesWhen = onlyWith is null ? string.Empty : onlyField,
            AppliesWhenValues = onlyWith ?? [],
        };

    /// <summary>
    /// What to do when the file a step is about to write is already there. It is the same question
    /// for a copy and for a move, and in both the middle answer — leave what is there alone — is
    /// what a person would do rather than overrule the file that is already there.
    /// </summary>
    private static ActionParameter IfExists()
        => Choice("ifExists", "If it is already there",
            ["overwrite", "skip", "unique"], "overwrite",
            "What to do when a file of that name is already there: put this one over it, leave the "
            + "one that is there alone, or give the new one a number of its own (report (2).csv). "
            + "Putting this one over it is the default.",
            labels: ["Replace it", "Leave it alone", "Give the new one a number"]);

    /// <summary>
    /// The character CSV cells are separated by. The picker holds the ones that actually turn up in
    /// files, and the field beside it is for the rest: a program that writes something unusual in
    /// between the cells is not a reason for the macro author to be stuck.
    /// </summary>
    private static ActionParameter[] Separator(string defaultValue, bool mayBeAutomatic = false) =>
    [
        Choice("separator", "Separator",
            mayBeAutomatic
                ? ["auto", "comma", "semicolon", "tab", "pipe", "space"]
                : ["comma", "semicolon", "tab", "pipe", "space"],
            defaultValue,
            mayBeAutomatic
                ? "The character between two cells; working it out from the first line of the "
                    + "file is the default."
                : "The character between two cells; a comma is the default.",
            labels: mayBeAutomatic
                ? ["Work it out (auto)", "Comma ,", "Semicolon ;", "Tab", "Vertical bar |", "Space"]
                : ["Comma ,", "Semicolon ;", "Tab", "Vertical bar |", "Space"],
            advanced: true),
        Text("separatorText", "Or this character", required: false, defaultValue: "",
            hint: "Fill this in for a character the list does not have, such as : or #, and it is "
                + "used instead of the one chosen above. Empty by default.",
            advanced: true),
    ];

    /// <summary>
    /// How the text of a file is turned into bytes, shared by the actions that read or write text.
    /// UTF-8 without a mark is what everything new uses; the other three are what a machine that
    /// already has files on it, or a person who has to open them in an older program, needs.
    /// </summary>
    private static ActionParameter Encoding()
        => Choice("encoding", "Encoding", [.. TextEncoding.Names], TextEncoding.Default,
            "How the file's text is turned into bytes. UTF-8 is what everything new uses and is "
            + "the default; UTF-8 with a mark is what Notepad writes; GBK is what a Chinese Windows "
            + "writes its own text files in; UTF-16 is what some older Windows programs expect. "
            + "Reading follows the mark at the front of the file when it has one, and reads bytes "
            + "that are not UTF-8 at all in this machine's own code page, which is how a CSV "
            + "another machine exported still reads.",
            labels: ["UTF-8", "UTF-8 with BOM", "GBK (Chinese)", "UTF-16"], advanced: true);

    /// <summary>How OCR text searches compare their match.</summary>
    private static ActionParameter TextMatch()
        => Choice("matchMode", "Match mode", ["contains", "exact", "regex"], "contains",
            "How the text that was read is compared against the text being looked for: "
            + "contained in it, exactly it, or matching it as a regular expression; contained "
            + "in it by default.");

    /// <summary>
    /// What a step keeps of each piece it reads: the writing as it stands, one kind of character out
    /// of it, or what a pattern picks out. Shared by the three actions that read the screen with OCR,
    /// so one screen is read the same way by all of them.
    /// </summary>
    private static ActionParameter Content()
        => Choice("content", "Content",
            ["text", "digits", "letters", "chinese", "lettersAndChinese", "regex"], "text",
            "What is kept of each piece of writing that was read. All of it is kept as it stands. "
            + "Digits, letters, Chinese and letters-and-Chinese keep only that kind of character and "
            + "leave out a piece with none of them, which is how a screen full of labels comes back "
            + "as just the amounts or just the codes; by pattern cuts each piece down to what the "
            + "pattern below matches. Full-width letters and digits count as ordinary ones, and the "
            + "one model the program comes with reads Chinese and English both, so keeping Chinese "
            + "sorts what was read rather than asking for another model.",
            labels:
            [
                "All writing",
                "Digits only",
                "Letters only",
                "Chinese only",
                "Letters and Chinese",
                "By pattern",
            ]);

    /// <summary>
    /// The pattern a step picks its text out with, asked for only while the reading is set to keep
    /// what a pattern matches.
    /// </summary>
    private static ActionParameter ContentPattern()
        => Text("contentPattern", "Extract pattern", @"(\d+)",
            "The regular expression each piece of what was read is cut down by: a piece it does not "
            + "match is left out, and a piece it does match stands as the first bracketed part when "
            + "the pattern has one and as the whole match when it has none. For example, the pattern "
            + @"Order (\d+) leaves just 88231 out of a line that was read as Order 88231. Write (?i) "
            + "at the front to ignore case. Leave it empty to keep everything that was read.",
            required: false, advanced: true, onlyWith: ["regex"], onlyField: "content");

    /// <summary>
    /// Whether a step wants the picture tidied up before it is read. Small, low-contrast writing on
    /// a busy background is where this pays off; on clean writing of a decent size it does nothing
    /// but cost a moment.
    /// </summary>
    private static ActionParameter Preprocess(bool advanced = false)
        => Choice("preprocess", "Picture cleanup", [.. WhaleGenie.Core.Devices.OcrPreprocess.Recipes],
            "none",
            "What is done to the picture before it is read, no change by default. Grey takes the "
            + "colour out; black and white pushes light and dark apart around the picture's own "
            + "average, so a dark screen works as well as a light one; twice as big helps with "
            + "small writing; the last one does both.",
            advanced: advanced);

    /// <summary>
    /// The score below which a step will not act on a reading, shared by the two OCR actions that
    /// look for text. The score is the reading model's own — the average, over the characters it
    /// read, of how far ahead its best guess was — so it is not a percentage and has no fixed
    /// range; the hint carries what was measured on a real screen so the number can be picked
    /// with something to go on. Zero, the default, keeps every reading.
    /// </summary>
    private static ActionParameter MinScore()
        => Number("minScore", "Lowest reading score", 0,
            "A reading is left out when the model was less sure of it than this. The score is the "
            + "model's own, not a percentage and with no fixed range; measured on a real screen, "
            + "clean writing scores around 40, writing too blurred to read around 24 and rubbish "
            + "off a busy background around 16. 0 keeps every reading, which is the default.",
            advanced: true);

    /// <summary>
    /// The writing a step is looking for, written as a pattern rather than as the exact words.
    /// Shared by the two OCR actions that search, because what it does is narrow the search down,
    /// not change how the screen is read.
    /// </summary>
    private static ActionParameter Expected(bool advanced = true)
        => Text("expected", "Must read like", required: false, placeholder: "^HP \\d+/\\d+$",
            advanced: advanced,
            hint: "Optional pattern the whole reading has to fit, on top of containing the text "
            + "looked for; nothing is required of it by default. It is how one line is told apart "
            + "from a screen full of the same word: HP 120/300 is found by ^HP \\d+/\\d+$ while "
            + "HP is full is left alone. Written as a regular expression, the same as the 'pattern' "
            + "match mode uses.");

    /// <summary>
    /// A reading the step wants put right before anything is compared against it. Shared by the
    /// three actions that read the screen with OCR, because a model that misreads a stylised font
    /// misreads it for all of them.
    /// </summary>
    private static ActionParameter FixText(bool advanced = false)
        => new()
        {
            Name = "fixText",
            Label = "Corrections",
            Kind = ActionParameterKind.MultilineText,
            Placeholder = "l00 = 100",
            Required = false,
            Advanced = advanced,
            Hint = "Optional corrections, one per line, written as what the model read = what it "
                + "says: l00 = 100, O = 0. For a model that keeps misreading a stylised font. The "
                + "reading is put right before anything is compared against it.",
        };

    /// <summary>
    /// The colour the writing is in, for the screen where the writing has an outline, a shadow or a
    /// picture behind it: everything that is not that colour is thrown away before reading, which
    /// is the difference between a model reading the numbers off a health bar and reading nothing.
    /// Shared by the three actions that read the screen with OCR.
    /// </summary>
    private static ActionParameter ColourFilter(bool advanced = false)
        => new()
        {
            Name = "colorFilter",
            Label = "Colour of the writing",
            Kind = ActionParameterKind.Color,
            DefaultValue = string.Empty,
            Required = false,
            Advanced = advanced,
            AcceptsFormula = true,
            Hint = "Optional colour of the writing. Given one, everything that is not that colour "
                + "(outline, shadow, background) is thrown away before the picture is read. For "
                + "writing that has an outline or sits on a picture. Empty reads the picture as it "
                + "is, which is the default.",
        };

    /// <summary>How far a pixel may be from the writing's colour and still count as writing.</summary>
    private static ActionParameter ColourTolerance(bool advanced = false)
        => new()
        {
            Name = "colorTolerance",
            Label = "Colour tolerance %",
            Kind = ActionParameterKind.Number,
            DefaultValue = "10",
            Minimum = 0m,
            Maximum = 100m,
            Advanced = advanced,
            Hint = "How far a pixel may be from the writing's colour and still count as writing, "
                + "from 0 to 100 per cent, 10 by default. Raise it for writing drawn with a "
                + "gradient or with anti-aliased edges.",
        };

    /// <summary>
    /// The window a UI Automation step looks in, together with how it is recognised: the two fields
    /// every window rule is made of. The picker fills the text in from the windows that are open,
    /// and a name an earlier step worked out goes in as <c>$name</c> like any other text.
    /// </summary>
    private static ActionParameter[] WindowFilter(
        string hint = "The window to look in, held up against the window's title, or against the "
            + "program that owns it or the class it registered, the way \"Match by\" says, ignoring "
            + "case. Leave it empty to look in every window.",
        bool required = false)
    =>
    [
        MatchText("window", "Match text", hint, required: required),
        MatchByChoice(extra: "A step that names no window looks in every window there is."),
    ];

    /// <summary>
    /// How a step names the window it acts on: the text, and the one choice that says what the text
    /// is held against and how. <paramref name="emptyMeans"/> finishes the sentence about an empty
    /// text, which stands for the window in front to a step that acts on one window and for every
    /// window to a step that lists them.
    /// </summary>
    private static ActionParameter[] WindowTarget(string emptyMeans = "the window in front") =>
    [
        MatchText("window", "Match text",
            "The text the window is recognised by, held up against the window's title, or against "
            + "the program that owns it or the class it registered, the way \"Match by\" says, "
            + "ignoring case. Leave it empty for " + emptyMeans + "."),
        MatchByChoice(),
    ];

    /// <summary>
    /// The text a window is recognised by, one of the two fields every window rule is made of. It
    /// is text rather than a value read as a whole: a name written as <c>$variable</c> is filled in
    /// where it stands, which is what a macro that recognises a window by a title it worked out
    /// earlier wants to write.
    /// </summary>
    /// <param name="name">Field name, which the macro file stores: what it holds is a window.</param>
    /// <param name="hint">What the field takes, in the words of the family that asks for it.</param>
    /// <param name="required">
    /// True for a step that has nothing to do without it, such as bringing a window to the front.
    /// </param>
    private static ActionParameter MatchText(string name, string label, string hint,
        bool advanced = false, string[]? onlyWith = null, string onlyField = "algorithm",
        bool required = false)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Window,
            Placeholder = "Notepad",
            Hint = hint,
            Required = required,
            AcceptsVariables = true,
            Advanced = advanced,
            AppliesWhen = onlyWith is null ? string.Empty : onlyField,
            AppliesWhenValues = onlyWith ?? [],
        };

    /// <summary>
    /// How a window is recognised, the other of the two fields. "The title holds this" is what a
    /// macro nearly always means and "the program that owns it" is what it means when the title
    /// moves with the document or with the language, so the five ways a window can be recognised
    /// are one choice rather than one choice about the part and another about the comparing: two
    /// fields asking one question is two fields to get wrong, and nine arrangements of them for one
    /// thing a macro needs to say.
    /// </summary>
    /// <param name="extra">What the settings beside this field do when they are set otherwise.</param>
    private static ActionParameter MatchByChoice(string name = "how", bool advanced = false,
        string[]? onlyWith = null, string onlyField = "algorithm",
        string extra = "Change it when the title moves with the document or the interface "
            + "language.")
        => Choice(name, "Match by", [.. WindowHow.Values], WindowHow.TitleContains,
            "How the window is recognised: the text is held up against the window's title, or "
            + "against the name of the program that owns it or the class that program registered, "
            + "contained in it, at its start, or as a regular expression matched against it. "
            + extra,
            labels:
            [
                "Title contains",
                "Title starts with",
                "Title matches a pattern",
                "Process name",
                "Window class",
            ],
            advanced: advanced, onlyWith: onlyWith, onlyField: onlyField);

    /// <summary>Name of another macro in the project, chosen from a list while staying editable.</summary>
    private static ActionParameter MacroName(string name, string label, string hint)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Macro,
            Hint = hint,
            Required = true,
        };

    /// <summary>UI Automation element selector.</summary>
    private static ActionParameter Selector()
        => Text("selector", "Selector", "Button[name='Save']",
            "Element selector, for example Button[name='Save'] or Edit[automationId='input'].");
}
