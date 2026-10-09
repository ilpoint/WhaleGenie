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

    private static readonly Geometry SpreadsheetIcon =
        Geometry.Parse("M5,4 H19 V20 H5 Z M5,9 H19 M5,14 H19 M12,4 V20");

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
                Text("name", "Name", hint: "Optional label shown in the step list.", required: false),
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
                Number("ms", "Wait", 1000, "How long to wait."),
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
                Number("minMs", "Shortest wait", 400, "Shortest wait."),
                Number("maxMs", "Longest wait", 1200, "Longest wait."),
            ],
        },
        new()
        {
            Key = "control.waitUntil",
            Category = ActionCategory.Control,
            DisplayName = "Wait Until",
            Description = "Wait until a condition holds, then carry on.",
            Parameters =
            [
                Condition("condition", "Condition", "Asked again and again until it holds. "
                    + "Any condition can be waited on: a colour, a picture, a piece of text, an "
                    + "element on screen, or a comparison between variables."),
                Number("timeoutMs", "Timeout", 10000, "Give up after this long."),
                Number("pollMs", "Check every", 200, "How long to wait between two checks.",
                    min: 10m, max: 60000m),
                Choice("onTimeout", "If it never holds", ["stop", "continue"], "stop",
                    "What the macro does when the time runs out.",
                    labels: ["Stop the macro", "Carry on with the next step"]),
                Variable("elapsedVariable", "Waited ms", "waited",
                    "Variable that receives how long the wait lasted, in milliseconds.",
                    required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "control.repeat",
            Category = ActionCategory.Control,
            DisplayName = "Repeat",
            Description = "Run child steps a fixed number of times — for when you already know "
                + "how many rounds it takes.",
            Parameters =
            [
                Number("times", "Times", 10, "How many iterations to run.", min: 1),
                Number("intervalMs", "Interval", 0, "Pause between iterations."),
                Steps("body", "Body steps", "Steps that run once per iteration."),
            ],
        },
        new()
        {
            Key = "control.while",
            Category = ActionCategory.Control,
            DisplayName = "While",
            Description = "Repeat child steps while a condition stays true — for when only the "
                + "state decides when to stop.",
            Parameters =
            [
                Condition("condition", "Condition", "Checked before every iteration."),
                Number("maxIterations", "Max iterations", 1000, "Safety cap that stops runaway loops.", min: 1),
                Steps("body", "Body steps", "Steps that run while the condition holds."),
            ],
        },
        new()
        {
            Key = "control.forEach",
            Category = ActionCategory.Control,
            DisplayName = "For Each",
            Description = "Run child steps once per item in a list or variable — for a batch of "
                + "values that each want the same handling.",
            Parameters =
            [
                Expression("items", "Items", "$names",
                    "A list, or a $variable holding one. For example [1, 2, 3], split($text, \",\") "
                    + "or $names."),
                Text("itemVariable", "Item variable", hint: "Variable that receives the current item.",
                    defaultValue: "item"),
                Text("indexVariable", "Index variable", required: false,
                    hint: "Optional variable that receives which round this is, counted from 0 in "
                          + "the order the items are walked in. Leave it empty to keep the round "
                          + "number out of the variables."),
                Toggle("reverse", "Reverse order", false, "Iterate from the last item to the first."),
                Steps("body", "Body steps", "Steps that run once per item."),
            ],
        },
        new()
        {
            Key = "control.for",
            Category = ActionCategory.Control,
            DisplayName = "Count From To",
            Description = "Count from one number to another, running the child steps each time — "
                + "for when the steps need the number of the round they are on.",
            Parameters =
            [
                Number("from", "From", 1, "The first value the counter takes.", min: -1000000),
                Number("to", "To", 5,
                    "The last value the counter takes, included, so counting 1 to 3 runs three "
                    + "times.", min: -1000000),
                Number("step", "Step", 1,
                    "What the counter is increased by each round. A negative step counts down; "
                    + "0 means \"count upwards, or downwards when From is above To\".", min: -1000000),
                Number("intervalMs", "Interval", 0, "Pause between rounds."),
                Variable("variable", "Counter variable", "i",
                    "Variable that receives the value of this round.",
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
                Condition("condition", "Condition", "Checked once when the step runs."),
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
                // The value is an operand rather than an expression: it reads from the variable
                // list like a condition does, and plain text such as "Running fast" is a value,
                // not a mistake to flag. A one-step formula still works, the engine reads both.
                Variable("value", "Value (operand)", "count",
                    "What the cases are compared against, read once when the step runs. Pick a "
                    + "variable from the list or type a name, a number or text of your own."),
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
            Category = ActionCategory.Control,
            DisplayName = "Try",
            Description = "Attempt some steps, handle a failure in another block, and always "
                + "run a third.",
            Parameters =
            [
                Steps("body", "Try steps", "Steps to attempt."),
                Steps("catch", "Catch steps", "Steps to run when the attempt fails."),
                Steps("finally", "Finally steps", "Steps that always run, however it ended."),
                Variable("errorVariable", "Error variable", "error",
                    "Variable that receives the reason the attempt failed, or is emptied when "
                    + "it did not.",
                    required: false, namesVariable: true),
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
                Text("reason", "Reason", hint: "Optional note written to the log.", required: false),
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
                    "Local values belong to this macro, global values are shared by every macro.",
                    labels: ["Local (this macro)", "Global (shared)"]),
                Expression("value", "Value", "3",
                    "A number, text, $variable, or a formula such as $count + 1 or upper($name)."),
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
                    + "variables must exist already, add them in the Variable Center.",
                    namesVariable: true),
                Choice("scope", "Scope", ["local", "global"], "local",
                    "Local values belong to this macro, global values are shared by every macro.",
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
                    + "Lists hold any mixture of values.",
                    namesVariable: true),
                Choice("scope", "Scope", ["local", "global"], "local",
                    "Local lists belong to this macro, global lists are shared by every macro.",
                    labels: ["Local (this macro)", "Global (shared)"]),
                Expression("items", "Starting items", "[1, 2, 3]",
                    "Values the list starts with. Leave empty to start with an empty list.",
                    required: false),
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
                Variable("name", "List variable", "names", "The list the macro sees.", namesVariable: true),
                Expression("value", "Value", "$item", "Value put at the end of the list."),
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
                Variable("name", "List variable", "names", "The list the macro sees.", namesVariable: true),
                Expression("index", "Index", "0", "Position to insert at; 0 is the front."),
                Expression("value", "Value", "$item", "Value inserted at that position."),
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
                Variable("name", "List variable", "names", "The list the macro sees.", namesVariable: true),
                Expression("index", "Index", "0", "Position to remove; -1 removes the last item."),
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
                Variable("name", "List variable", "names", "The list the macro sees.", namesVariable: true),
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
                Multiline("message", "Message", "Text written to the log.", "Loop finished"),
                Choice("level", "Level", ["info", "warn", "error", "debug"], "info"),
            ],
        },
        new()
        {
            Key = "control.runMacro",
            Category = ActionCategory.Control,
            DisplayName = "Run Another Macro",
            Description = "Run another macro in this project, as though its steps had been written "
                + "here. The called macro works on values of its own: it reads what the arguments "
                + "hand it, and what it leaves behind comes back through the results list.",
            Parameters =
            [
                MacroName("macro", "Macro", "Another macro in this project."),
                Multiline("arguments", "Arguments",
                    "One NAME=value per line, handed to the called macro as its own values. The "
                    + "value is read the way other fields are, so count=$n + 1 passes a number "
                    + "and xs=$list passes the list itself. Lines starting with # are skipped.",
                    "count=$n + 1\nlabel=$name", required: false),
                Text("returns", "Results", "answer, status",
                    "Names of the called macro's values to bring back into this macro, separated "
                    + "by commas. A name the called macro never set comes back empty.", required: false),
            ],
        },

        // ------------------------------------------------------------------- file
        new()
        {
            Key = "file.readText",
            Category = ActionCategory.File,
            DisplayName = "Read Text",
            Description = "Read a text file into a variable.",
            Parameters =
            [
                FilePath("path", "File", "notes.txt", "File to read.", PathIntent.Read),
                Encoding(),
                Variable("resultVariable", "Result variable", "text",
                    "Variable that receives the contents.", required: false, namesVariable: true),
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
                Multiline("text", "Text", "Text to write.", "", required: false),
                Choice("mode", "Mode", ["overwrite", "append"], "overwrite",
                    "Replace the file, or add to what is already there.",
                    labels: ["Replace it", "Add to the end"]),
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
                    "What the line says. Variables are filled in the way they are everywhere else. "
                    + "The line ends with a line break, so the next one starts on a new line.",
                    acceptsFormula: true),
                Toggle("timestamp", "Put the time first", true,
                    "Write the date and time in front of the line. A file of lines without them is "
                    + "hard to read after the fact."),
            ],
        },
        new()
        {
            Key = "file.exists",
            Category = ActionCategory.File,
            DisplayName = "File Exists",
            Description = "Check whether a file or folder is there.",
            Parameters =
            [
                FilePath("path", "File", "notes.txt", "File or folder to look for.", PathIntent.Read),
                Variable("resultVariable", "Result variable", "exists",
                    "Variable that receives true or false.", required: false, namesVariable: true),
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
                Toggle("overwrite", "Overwrite", true, "Replace the copy when it is already there."),
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
                Toggle("overwrite", "Overwrite", true, "Replace the file when it is already there."),
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
                    + "same place.",
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
                Variable("resultVariable", "Result variable", "path",
                    "Variable that receives the answer.", namesVariable: true, defaultValue: "path"),
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
                    "Replace files that are already there."),
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
                    "A folder that still holds something is left alone unless this is on."),
            ],
        },
        new()
        {
            Key = "file.listFiles",
            Category = ActionCategory.File,
            DisplayName = "List Files",
            Description = "Collect the files in a folder into a list.",
            Parameters =
            [
                FilePath("folder", "Folder", ".", "Folder to look in.", PathIntent.Folder),
                Text("pattern", "Pattern", "*.txt", "Which files to collect, for example *.txt."),
                Toggle("recurse", "Include subfolders"),
                Variable("resultVariable", "Result variable", "files",
                    "Variable that receives the list of full paths.", required: false,
                    namesVariable: true),
            ],
        },
        new()
        {
            Key = "file.readJson",
            Category = ActionCategory.File,
            DisplayName = "Read JSON",
            Description = "Read a value out of a JSON file.",
            Parameters =
            [
                FilePath("path", "File", "config.json", "JSON file to read.", PathIntent.Read,
                    "*.json"),
                Text("query", "Where", "server.name",
                    "Path to the value, such as server.name or items[0].id. "
                    + "Leave empty for the whole file."),
                Variable("resultVariable", "Result variable", "value",
                    "Variable that receives the value.", required: false, namesVariable: true),
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
                    "Path to the value. Missing objects on the way are created."),
                Text("value", "Value", "", "Value to store.", required: false),
            ],
        },
        new()
        {
            Key = "file.readCsv",
            Category = ActionCategory.File,
            DisplayName = "Read CSV",
            Description = "Read a CSV file into a list of rows.",
            Parameters =
            [
                FilePath("path", "File", "rows.csv", "CSV file to read.", PathIntent.Read, "*.csv"),
                Encoding(),
                ..Separator("auto", mayBeAutomatic: true),
                Toggle("hasHeader", "First row is a header", true,
                    "Leave the first row out of the result, and keep what it says in the names "
                    + "variable below."),
                Variable("headerVariable", "Column names variable", "columns",
                    "Variable that receives the names in the header row, in column order, which is "
                    + "how a macro finds the column it wants. Leave empty for none.",
                    required: false, namesVariable: true),
                Toggle("skipBlankLines", "Skip blank lines", true,
                    "Drop lines that hold nothing. A file written by another program often ends "
                    + "with one, and it would otherwise read as a row of one empty cell."),
                Number("startRow", "Start at row", 1,
                    "The line of the file to start at, counted from 1. When the first row is a "
                    + "header, the header is this line.", min: 1),
                Number("maxRows", "At most rows", 0,
                    "Read this many rows at most. 0 reads every row from there on.", max: 1000000),
                Toggle("trim", "Trim spaces", false,
                    "Drop spaces around each cell, which a file written by hand often has."),
                Text("columns", "Only these columns", "金额, 日期", required: false,
                    hint: "The columns to keep, written the way they are named in the header or as "
                        + "the letter they sit under: 金额, or B, or 金额,B. Each row comes back with "
                        + "those columns and no others, in the order written here. Leave empty for "
                        + "every column."),
                Choice("shape", "Result shape", ["rows", "values"], "rows",
                    "A list of rows, or — when exactly one column was named above — the values of "
                    + "that column as one plain list, ready to walk through or add up.",
                    labels: ["One list per row", "The values of one column"], advanced: true),
                Text("matchColumn", "Only rows where", "订单号", required: false,
                    hint: "Read just the rows whose cell in this column is the value below; the "
                        + "column is named the same way. Leave empty to read every row."),
                Text("matchValue", "Is", "A123", required: false,
                    hint: "The value those rows hold. Written out, or named as a variable with "
                        + "$name.",
                    acceptsVariables: true),
                Choice("matchMode", "Matches", MatchModes, "equals",
                    "How the cell is compared against the value: exactly, or parts of it.",
                    labels: MatchModeLabels, advanced: true),
                Variable("resultVariable", "Result variable", "rows",
                    "Variable that receives a list of rows, each a list of cells.",
                    required: false, namesVariable: true),
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
                    "A list of rows. Each row may itself be a list of cells.", namesVariable: false),
                Variable("header", "Header row", "$columns",
                    "Column names to put above the data: a list, or the names written out on one "
                    + "line with the separator between them. Adding to a file that is not there "
                    + "yet writes it; adding to one that already is does not, so a log keeps one "
                    + "header at the top. Leave it empty for a table with nothing above it.",
                    required: false),
                ..Separator("comma"),
                Choice("mode", "Mode", ["replace", "append"], "replace",
                    "Write the file from the start, or add these rows below what is already in it.",
                    labels: ["Replace the file", "Add below the last row"]),
                Choice("lineEnding", "Line ending", ["windows", "unix"], "windows",
                    "Which characters end a line: CRLF, which Windows writes, or LF, which "
                    + "everything else does.",
                    labels: ["Windows (CRLF)", "Unix (LF)"], advanced: true),
                Toggle("quoteAll", "Quote every cell", false,
                    "Put quotes around every cell rather than only around the ones that need them. "
                    + "Some programs insist on it."),
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
                    "Names to save, separated by commas. Leave empty for every local and "
                    + "shared variable.", required: false),
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

        // ------------------------------------------------------------- spreadsheet
        new()
        {
            Key = "excel.readSheet",
            Category = ActionCategory.Spreadsheet,
            DisplayName = "Read Excel Sheet",
            Description = "Read a sheet of an Excel file into a list of rows.",
            Parameters =
            [
                FilePath("path", "File", "report.xlsx", "The Excel file to read.", PathIntent.Read,
                    "*.xlsx"),
                Sheet(),
                Text("range", "Cells", "B2:D40", required: false,
                    hint: "Which cells to read, written the way the name box writes them: B2:D40, "
                        + "or B2 for everything from that cell down and to the right. Leave empty "
                        + "for every cell the sheet uses."),
                Text("columns", "Only these columns", "金额, 日期", required: false,
                    hint: "The columns to keep, written the way they are named in the header or as "
                        + "the letter they sit under: 金额, or B, or 金额,B. Each row comes back with "
                        + "those columns and no others, in the order written here. Leave empty for "
                        + "every column."),
                Toggle("hasHeader", "First row is a header", true,
                    "Leave the first row out of the result, and keep what it says in the names "
                    + "variable below."),
                Number("headerRow", "Header row", 1,
                    "Which row holds the column names, counted from the first row that was read. A "
                    + "report with a title above its table usually has it on the second or third "
                    + "row; everything above it is left out. Only used when the first row is a "
                    + "header.", min: 1, advanced: true),
                Variable("headerVariable", "Column names variable", "columns",
                    "Variable that receives the names in the header row, in column order, which is "
                    + "how a macro finds the column it wants. Leave empty for none.",
                    required: false, namesVariable: true),
                Number("maxRows", "At most rows", 0,
                    "Read this many rows at most. 0 reads every row from there on.", max: 1000000),
                Choice("shape", "Result shape", ["rows", "values"], "rows",
                    "A list of rows, or — when exactly one column was named above — the values of "
                    + "that column as one plain list, ready to walk through or add up.",
                    labels: ["One list per row", "The values of one column"], advanced: true),
                Text("matchColumn", "Only rows where", "订单号", required: false,
                    hint: "Read just the rows whose cell in this column is the value below; the "
                        + "column is named the same way. Leave empty to read every row."),
                Text("matchValue", "Is", "A123", required: false,
                    hint: "The value those rows hold. Written out, or named as a variable with "
                        + "$name.",
                    acceptsVariables: true),
                Choice("matchMode", "Matches", MatchModes, "equals",
                    "How the cell is compared against the value: exactly, or parts of it.",
                    labels: MatchModeLabels, advanced: true),
                Toggle("asText", "Read what the cells show", false,
                    "Read the text each cell shows rather than the value it holds, which is what "
                    + "somebody looking at the sheet reads: a phone number that shows leading "
                    + "zeros, a date, a column of money."),
                Variable("resultVariable", "Result variable", "rows",
                    "Variable that receives a list of rows, each a list of cells.",
                    required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "excel.writeSheet",
            Category = ActionCategory.Spreadsheet,
            DisplayName = "Write Excel Sheet",
            Description = "Write a list of rows into a sheet of an Excel file, making the file or "
                + "the sheet when they are not there yet.",
            Parameters =
            [
                FilePath("path", "File", "report.xlsx",
                    "The Excel file to write. It is made when it is not there, and everything "
                    + "else already in it is kept.", PathIntent.Write, "*.xlsx"),
                Sheet(),
                Variable("rows", "Rows", "$rows",
                    "The table to write: a list of rows, each of them a list of cells. A list of "
                    + "plain values is one value per row, which is how a column comes back from "
                    + "reading one, and a single value is a single cell. A number is written as a "
                    + "number and a flag as a flag, so a column of numbers stays a column of "
                    + "numbers in the sheet rather than text that only looks like one.",
                    namesVariable: false),
                Variable("header", "Header row", "$columns",
                    "Column names to put above the rows: a list, as the action that read the table "
                    + "hands back when a variable is named for the header row. Adding to a sheet "
                    + "that already holds something does not write them again, so a log keeps one "
                    + "header at the top. Leave it empty for a table with nothing above it.",
                    required: false),
                Toggle("align", "Put each value under the column with that name", false,
                    "Write every cell into the column whose name above it matches the name this "
                    + "step gives that cell, instead of into the same place across the row. The "
                    + "names of this step's cells come from the header row above, and the names the "
                    + "sheet already holds decide where each one goes: that is what lets a macro "
                    + "fill in a sheet whose columns sit in another order, or write only some of "
                    + "its columns, without counting columns. A sheet that has no names yet takes "
                    + "the rows in the order they are written in."),
                Number("headerRow", "The names are on row", 1,
                    "Which row of the sheet holds the column names, counted from the top. Only "
                    + "used when the values are put in by column name.",
                    min: 1, max: 1048576, advanced: true),
                Choice("mode", "Mode", ["replace", "insert", "append"], "replace",
                    "What happens to what the sheet already holds: clear it and write, write over "
                    + "the cells from the starting cell on and leave the rest, or add below the "
                    + "last row. Append is how a macro keeps a log in a sheet across runs.",
                    labels: ["Clear the sheet", "Write over from the cell on", "Add below the last row"]),
                Text("startCell", "Start at cell", "A1", required: false, defaultValue: "A1",
                    hint: "The cell the first row starts at, as B2. Appending ignores it: adding "
                        + "always lands below the last row."),
                Toggle("formula", "Text starting with = is a formula", true,
                    "Write a cell that begins with = as a formula, the way Excel does when somebody "
                    + "types one. Turn it off to write such text as it stands."),
                Toggle("autoFit", "Widen the columns", false,
                    "Make each used column wide enough to show what is in it."),
                Variable("numberFormat", "Shown as", "yyyy-mm-dd",
                    "What each written column is shown as, written the way Excel writes a format: "
                    + "yyyy-mm-dd for a date, #,##0.00 for money, 0.00% for a share, @ to keep a "
                    + "text that looks like a number as text. One per column, counted from the left "
                    + "of what this step writes, given as a list the way the header row is; a single "
                    + "one is the format of every column written, and a column without a format is "
                    + "left exactly as it was.",
                    required: false, namesVariable: false),
                Choice("alignment", "Where the text sits", ["leave", "left", "center", "right"],
                    "leave",
                    "Where what is written sits across its column: leave it alone, pushed to the "
                    + "left, centred, or pushed to the right. Numbers written into a sheet are "
                    + "already put on the right and text on the left, so this is for the report "
                    + "that wants its headings centred.",
                    labels: ["Leave it alone", "Left", "Centred", "Right"], advanced: true),
                Toggle("wrapText", "Let text wrap onto more than one line", false,
                    "Show a cell whose text is longer than its column on more than one line inside "
                    + "the cell rather than letting it run over the neighbours."),
                Number("columnWidth", "Make the columns this wide", 0,
                    "How wide the columns this step writes are made, or 0 to leave their width "
                    + "alone. Asking to widen the columns to fit comes after this, so it is the one "
                    + "that wins when both are set.",
                    min: 0, max: 255, advanced: true),
            ],
        },
        new()
        {
            Key = "excel.listSheets",
            Category = ActionCategory.Spreadsheet,
            DisplayName = "List Excel Sheets",
            Description = "Read the names of the sheets in an Excel file.",
            Parameters =
            [
                FilePath("path", "File", "report.xlsx", "The Excel file to read.", PathIntent.Read,
                    "*.xlsx"),
                Variable("resultVariable", "Result variable", "sheets",
                    "Variable that receives the sheet names, in the order they sit along the "
                    + "bottom of the window.", namesVariable: true, defaultValue: "sheets"),
            ],
        },
        new()
        {
            Key = "excel.addSheet",
            Category = ActionCategory.Spreadsheet,
            DisplayName = "Add Excel Sheet",
            Description = "Put an empty sheet at the end of an Excel file, making the file when it "
                + "is not there yet.",
            Parameters =
            [
                FilePath("path", "File", "report.xlsx", "The Excel file to change.", PathIntent.Read,
                    "*.xlsx"),
                Sheet(),
            ],
        },
        new()
        {
            Key = "excel.deleteSheet",
            Category = ActionCategory.Spreadsheet,
            DisplayName = "Delete Excel Sheet",
            Description = "Take a sheet out of an Excel file, with everything in it.",
            Parameters =
            [
                FilePath("path", "File", "report.xlsx", "The Excel file to change.", PathIntent.Read,
                    "*.xlsx"),
                Sheet(),
            ],
        },
        new()
        {
            Key = "excel.renameSheet",
            Category = ActionCategory.Spreadsheet,
            DisplayName = "Rename Excel Sheet",
            Description = "Put another name on a sheet of an Excel file.",
            Parameters =
            [
                FilePath("path", "File", "report.xlsx", "The Excel file to change.", PathIntent.Read,
                    "*.xlsx"),
                Sheet(),
                Text("newName", "New name", "Data", "The name the sheet takes."),
            ],
        },
        new()
        {
            Key = "excel.insertRows",
            Category = ActionCategory.Spreadsheet,
            DisplayName = "Insert Excel Rows",
            Description = "Make room for rows in a sheet, pushing everything under them down.",
            Parameters =
            [
                FilePath("path", "File", "report.xlsx", "The Excel file to change.", PathIntent.Read,
                    "*.xlsx"),
                Sheet(),
                Number("at", "Above row", 1,
                    "The rows go in above this one, and the rows already there from this one down "
                    + "move down: inserting in front of row five is what makes room for a line that "
                    + "belongs there.", min: 1, max: 1048576),
                Number("count", "How many rows", 1, "How many rows to make room for.",
                    min: 1, max: 1048576),
            ],
        },
        new()
        {
            Key = "excel.deleteRows",
            Category = ActionCategory.Spreadsheet,
            DisplayName = "Delete Excel Rows",
            Description = "Take rows out of a sheet, bringing everything under them up.",
            Parameters =
            [
                FilePath("path", "File", "report.xlsx", "The Excel file to change.", PathIntent.Read,
                    "*.xlsx"),
                Sheet(),
                Number("at", "From row", 1,
                    "The first row to take out. This row and the ones under it go; the rows after "
                    + "them move up, and the columns are not touched.", min: 1, max: 1048576),
                Number("count", "How many rows", 1,
                    "How many rows to take out. Asking for more than the sheet holds takes out the "
                    + "ones it does hold, so a macro that clears a block does not have to know how "
                    + "long the block turned out.", min: 1, max: 1048576),
            ],
        },

        // -------------------------------------------------------------------- data
        new()
        {
            Key = "data.base64Encode",
            Category = ActionCategory.Data,
            DisplayName = "Encode as Base64",
            Description = "Turn text into Base64, the form a value takes inside a URL or a token.",
            Parameters =
            [
                Text("text", "Text", "hello",
                    "The text to encode. Variables are filled in first, then the whole value is "
                    + "read as UTF-8.", acceptsFormula: true),
                Variable("resultVariable", "Result variable", "encoded",
                    "Variable that receives the Base64 text.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "data.base64Decode",
            Category = ActionCategory.Data,
            DisplayName = "Decode from Base64",
            Description = "Turn Base64 back into the text it holds.",
            Parameters =
            [
                Text("text", "Base64", "aGVsbG8=",
                    "The Base64 text to decode. Variables are filled in first.", acceptsFormula: true),
                Variable("resultVariable", "Result variable", "decoded",
                    "Variable that receives the decoded text, read as UTF-8.",
                    required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "data.hash",
            Category = ActionCategory.Data,
            DisplayName = "Hash Text",
            Description = "Work out the checksum of a piece of text.",
            Parameters =
            [
                Choice("algorithm", "Algorithm", ["md5", "sha1", "sha256", "sha512"], "sha256",
                    "Which checksum to work out. SHA-256 is the ordinary choice; MD5 and SHA-1 are "
                    + "here for matching a value some other program hands out.",
                    labels: ["MD5", "SHA-1", "SHA-256", "SHA-512"]),
                Text("text", "Text", "hello", "The text to work out the checksum of.",
                    acceptsFormula: true),
                Variable("resultVariable", "Result variable", "digest",
                    "Variable that receives the checksum in lowercase letters and digits.",
                    required: false, namesVariable: true),
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
                Multiline("text", "Text", "Text to put on the clipboard."),
            ],
        },
        new()
        {
            Key = "clipboard.readText",
            Category = ActionCategory.Clipboard,
            DisplayName = "Read Clipboard",
            Description = "Read the text on the clipboard into a variable.",
            Parameters =
            [
                Variable("resultVariable", "Result variable", "clipboard",
                    "Variable that receives the text.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "clipboard.readImage",
            Category = ActionCategory.Clipboard,
            DisplayName = "Read Clipboard Picture",
            Description = "Take the picture on the clipboard into an image variable.",
            Parameters =
            [
                Variable("resultVariable", "Result variable", "picture",
                    "Variable that receives the picture, ready for a find-image step to look for "
                    + "on screen. $name.width and $name.height hold its size; a picture on the "
                    + "clipboard has no place on screen, so there is no $name.x.",
                    required: false, namesVariable: true),
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
            Category = ActionCategory.Clipboard,
            DisplayName = "Read Clipboard Files",
            Description = "Take the paths of the files on the clipboard into a list.",
            Parameters =
            [
                Variable("resultVariable", "Result variable", "files",
                    "Variable that receives the list of full paths. An empty list means the "
                    + "clipboard holds no files.",
                    required: false, namesVariable: true),
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
                    + "folder, and every file has to be there: a paste of a path that points "
                    + "nowhere fails quietly in whatever program receives it.",
                    namesVariable: false),
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
            Category = ActionCategory.Clipboard,
            DisplayName = "Wait for Clipboard",
            Description = "Wait until something new is copied to the clipboard.",
            Parameters =
            [
                Number("timeoutMs", "Timeout", 5000, "How long to wait before giving up."),
                Variable("resultVariable", "Result variable", "clipboard",
                    "Variable that receives the new text. Leave empty to only wait.",
                    required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "clipboard.copy",
            Category = ActionCategory.Clipboard,
            DisplayName = "Copy Selection",
            Description = "Press the copy shortcut, then keep what landed on the clipboard.",
            Parameters =
            [
                Text("keys", "Shortcut", "Ctrl+C", "Keys joined with + that copy the selection.",
                    required: true, defaultValue: "Ctrl+C"),
                Number("timeoutMs", "Timeout", 1500, "How long to wait for the clipboard."),
                Variable("resultVariable", "Result variable", "clipboard",
                    "Variable that receives the copied text.", required: false, namesVariable: true),
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
                Multiline("text", "Text", "Text to paste. Leave empty to paste what is already copied.",
                    required: false),
                Text("keys", "Shortcut", "Ctrl+V", "Keys joined with + that paste.",
                    required: true, defaultValue: "Ctrl+V"),
            ],
        },

        // ---------------------------------------------------------------- process
        new()
        {
            Key = "process.start",
            Category = ActionCategory.Process,
            DisplayName = "Start Program",
            Description = "Start another program, optionally without showing its window.",
            Parameters =
            [
                PickablePath("file", "Program", "notepad.exe", "The program to start.",
                    PathIntent.Read, "*.exe;*.com;*.bat;*.cmd"),
                Text("arguments", "Arguments", "", "What to pass to the program.", required: false),
                PickablePath("workingDirectory", "Working folder", "",
                    "Folder to start the program in.", PathIntent.Folder, required: false),
                Toggle("hidden", "Hidden window", true,
                    "Start the program without showing its window."),
                Toggle("runAsAdmin", "Run as administrator", false,
                    "Ask Windows to start the program with administrator rights. Windows shows "
                    + "its own approval prompt, and refusing it fails the step. A program started "
                    + "elevated cannot be kept off the screen."),
                Multiline("environment", "Environment variables",
                    "One NAME=value per line for the program to start with. Leave it empty to "
                    + "start the program with WhaleGenie's own environment. Lines starting with # are "
                    + "skipped. Giving a program its own environment means naming the program "
                    + "itself, not a document or a shortcut.",
                    "LANG=zh_CN.UTF-8", required: false),
                Variable("resultVariable", "Result variable", "processId",
                    "Variable that receives the process id.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "process.waitFor",
            Category = ActionCategory.Process,
            DisplayName = "Wait for Program",
            Description = "Wait until a program is running.",
            Parameters =
            [
                Text("name", "Program", "notepad", "Name of the program to watch for."),
                Number("timeoutMs", "Timeout", 10000, "How long to wait before giving up."),
                Variable("resultVariable", "Result variable", "processId",
                    "Variable that receives the process id.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "process.waitExit",
            Category = ActionCategory.Process,
            DisplayName = "Wait for Exit",
            Description = "Wait until a program started by this macro has finished.",
            Parameters =
            [
                Text("id", "Process id", "$processId",
                    "The process to watch, usually the one Start Program gave back."),
                Number("timeoutMs", "Timeout", 60000, "How long to wait before giving up."),
                Variable("resultVariable", "Result variable", "exitCode",
                    "Variable that receives the exit code.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "process.exists",
            Category = ActionCategory.Process,
            DisplayName = "Program Running",
            Description = "Check whether a program is running.",
            Parameters =
            [
                Text("name", "Program", "notepad", "Name of the program to look for."),
                Variable("resultVariable", "Result variable", "running",
                    "Variable that receives true or false.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "process.list",
            Category = ActionCategory.Process,
            DisplayName = "List Programs",
            Description = "Collect the names of the running programs into a list.",
            Parameters =
            [
                Variable("resultVariable", "Result variable", "processes",
                    "Variable that receives the list of names.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "process.info",
            Category = ActionCategory.Process,
            DisplayName = "Program Details",
            Description = "Read what is known about a running program.",
            Parameters =
            [
                Text("target", "Program", "notepad", "Name of the program, or a process id."),
                Variable("resultVariable", "Result variable", "process",
                    "Variable that receives the program's file. $name.id, $name.name, $name.path, "
                    + "$name.memoryMb and $name.cpuSeconds hold the parts, and the file is empty "
                    + "when Windows will not say it.",
                    required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "process.kill",
            Category = ActionCategory.Process,
            DisplayName = "Stop Program",
            Description = "Close a program, asking it to exit first unless told to be firm.",
            Parameters =
            [
                Text("target", "Program", "notepad", "Name of the program, or a process id."),
                Toggle("force", "Force close", false,
                    "Close it at once instead of asking it to exit."),
                Variable("resultVariable", "Result variable", "stopped",
                    "Variable that receives how many were closed.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "command.run",
            Category = ActionCategory.Process,
            DisplayName = "Run Command",
            Description = "Run a command line and collect what it printed.",
            Parameters =
            [
                PickablePath("file", "Command", "cmd.exe", "The program to run, such as cmd.exe.",
                    PathIntent.Read, "*.exe;*.com;*.bat;*.cmd"),
                Text("arguments", "Arguments", "/c dir",
                    "What to pass to the command.", required: false),
                PickablePath("workingDirectory", "Working folder", "",
                    "Folder to run the command in.", PathIntent.Folder, required: false),
                Multiline("environment", "Environment variables",
                    "One NAME=value per line for the command to run with. Leave it empty to run "
                    + "it with WhaleGenie's own environment. Lines starting with # are skipped.",
                    "LANG=zh_CN.UTF-8", required: false),
                Multiline("standardInput", "Standard input",
                    "What the command reads on its standard input. Leave it empty to give the "
                    + "command nothing to read. Sent as UTF-8, and $name is replaced by what that "
                    + "variable holds.",
                    "first line\nsecond line", required: false),
                Number("timeoutMs", "Timeout", 30000, "How long the command may run."),
                Toggle("streamOutput", "Write output to the log as it arrives", false,
                    "Put every line the command prints into the run log while it is still running, "
                    + "which is how a long build or script can be watched. The result variable still "
                    + "holds the whole output."),
                Choice("outputEncoding", "Output encoding", ["system", "utf8", "utf16"], "system",
                    "The code page the command prints in. Left as it is, its output is read in this "
                    + "machine's own, which is what the programs Windows ships with — cmd.exe, "
                    + "Windows PowerShell, Python — write in when they are not talking to a screen. "
                    + "A program that prints UTF-8 whichever machine it is on, Node.js for one, "
                    + "needs to be read that way instead.",
                    labels: ["This machine's", "UTF-8", "UTF-16"]),
                Variable("resultVariable", "Result variable", "output",
                    "Variable that receives what the command printed.",
                    required: false, namesVariable: true),
                Variable("errorVariable", "Error variable", "",
                    "Variable that receives anything printed on the error stream.",
                    required: false, namesVariable: true),
                Variable("exitCodeVariable", "Exit code variable", "exitCode",
                    "Variable that receives the exit code.", required: false, namesVariable: true),
            ],
        },

        // ----------------------------------------------------------------- system
        new()
        {
            Key = "system.info",
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
                        "batteryPercent", "onAcPower", "monitorCount", "monitorBounds", "dpi",
                        "dpiPercent", "uptimeSeconds",
                    ],
                    "userName", "Which fact to read.",
                    labels:
                    [
                        "User name", "Computer name", "Domain", "Windows version",
                        "Processor count", "Windows folder", "Temp folder", "User folder",
                        "Desktop", "Documents", "Downloads", "Startup folder", "App data",
                        "Program Files", "Network connected", "Network names", "Local address",
                        "Battery %", "On mains power", "Screen count", "Screen rectangles",
                        "DPI", "DPI percent", "Seconds since start",
                    ]),
                Variable("resultVariable", "Result variable", "info",
                    "Variable that receives the value.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "system.environment",
            Category = ActionCategory.System,
            DisplayName = "Environment Variable",
            Description = "Read an environment variable.",
            Parameters =
            [
                Text("name", "Name", "TEMP", "Name of the environment variable."),
                Variable("resultVariable", "Result variable", "value",
                    "Variable that receives the value.", required: false, namesVariable: true),
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
                    "Which of the things the Start menu's power button does. Locking the screen and "
                    + "turning the monitor off cost nothing. Signing out, restarting and shutting "
                    + "down close what is open, so anything unsaved goes with it.",
                    labels:
                    [
                        "Lock the screen", "Turn the monitor off", "Sign out", "Sleep", "Hibernate",
                        "Restart", "Shut down", "Cancel a countdown",
                    ]),
                Number("graceSeconds", "Warning seconds", 0,
                    "How much warning Windows gives before restarting or shutting down: a countdown "
                    + "appears on screen and the shutdown can still be called off with \"Cancel a "
                    + "countdown\" or shutdown /a. Zero means at once. Ignored by the rest.",
                    max: 600),
            ],
        },
        new()
        {
            Key = "system.volume",
            Category = ActionCategory.System,
            DisplayName = "Volume",
            Description = "Read or change the volume of the speakers Windows is using.",
            Parameters =
            [
                Choice("what", "Do what",
                    ["get", "set", "up", "down", "mute", "unmute", "toggleMute"], "get",
                    "Whether to read the volume, set it to a level, move it up or down by a few "
                    + "points, or switch the sound off and on. The sound card is the machine's own, "
                    + "so this is not the same as the volume keys, which turn up whatever window "
                    + "has the focus.",
                    labels:
                    [
                        "Read it", "Set the level", "Turn it up", "Turn it down", "Mute",
                        "Unmute", "Mute or unmute",
                    ]),
                Number("percent", "Level %", 50,
                    "The level to set, from 0 to 100. Used by \"Set the level\".", max: 100),
                Number("stepPercent", "By how much", 5,
                    "How far to move the volume, in points. Used by \"Turn it up\" and \"Turn it "
                    + "down\", which never go past 0 or 100.",
                    min: 1, max: 100),
                Variable("resultVariable", "Result variable", "volume",
                    "Variable that receives the volume the machine is left at, from 0 to 100. A "
                    + "step that only mutes still reports the level it is waiting at.",
                    namesVariable: true, defaultValue: "volume"),
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
                    "Which of the machine's own sounds to play. A macro that has finished, or gone "
                    + "wrong, can say so out loud without putting anything on the screen. Which "
                    + "sound each of these is follows the machine's own sound settings: a machine "
                    + "told to keep quiet plays nothing and the step is done either way, while one "
                    + "with no sound device at all fails the step and says why.",
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
                    "The heading of the notification. Leave it empty to head it with the program's "
                    + "own name.", required: false, acceptsFormula: true),
                Text("message", "Message", "All the files were copied.",
                    "What the notification says. Variables in it are filled in the way they are "
                    + "everywhere else, so a macro can report what it just did.",
                    required: true, acceptsFormula: true),
                Choice("what", "Kind", ["information", "warning", "error"], "information",
                    "How the notification is marked: an ordinary note, something worth looking at, "
                    + "or something that went wrong. A machine with its notifications switched off "
                    + "shows nothing and the step is done either way.",
                    labels: ["Note", "Warning", "Error"]),
            ],
        },
        new()
        {
            Key = "system.ime",
            Category = ActionCategory.System,
            DisplayName = "Input Method",
            Description = "Read or change the keyboard layout and input method of the window in "
                + "front.",
            Parameters =
            [
                Choice("what", "Do what",
                    ["get", "list", "switch"], "get",
                    "Which keyboard layout the window in front is typing in, and how to give it "
                    + "another. A macro that types Latin keys while a Chinese layout is in use gets "
                    + "Chinese candidates instead of its shortcut, so switching to the English "
                    + "layout first is often what makes a macro work. Turning a Chinese input "
                    + "method's own Chinese/English switch off is a key press rather than a setting "
                    + "— Windows keeps that state inside the program being typed into — so that part "
                    + "is done by sending Ctrl+Space or Shift.",
                    labels:
                    ["Which layout is in use", "List the installed layouts", "Switch to another layout"]),
                Text("layout", "Layout", "英语(美国)", required: false,
                    hint: "Which layout to switch to, used by \"Switch to another layout\". Part of "
                          + "the name is enough; \"List the installed layouts\" writes out the names "
                          + "to choose from. The step waits for the window to really change over, so "
                          + "the step after it can rely on typing in that language."),
                Variable("resultVariable", "Result variable", "ime",
                    "Variable that receives the answer: the layout in use, or the list of layouts "
                    + "that could be switched to.",
                    namesVariable: true, defaultValue: "ime"),
            ],
        },
        new()
        {
            Key = "system.brightness",
            Category = ActionCategory.System,
            DisplayName = "Brightness",
            Description = "Read or change how bright the screens are.",
            Parameters =
            [
                Choice("what", "Do what", ["get", "set", "up", "down"], "get",
                    "Whether to read the brightness, set it to a level, or move it up or down a "
                    + "few points. Every screen that answers is changed together — this talks to the "
                    + "screen itself over its video cable, the same way the brightness buttons on a "
                    + "monitor do, so a laptop's own panel or a screen that does not carry the "
                    + "setting may not answer at all.",
                    labels: ["Read it", "Set the level", "Turn it up", "Turn it down"]),
                Number("percent", "Level %", 50,
                    "The level to set, from 0 to 100. Used by \"Set the level\".", max: 100),
                Number("stepPercent", "By how much", 10,
                    "How far to move the brightness, in points. Used by \"Turn it up\" and \"Turn it "
                    + "down\", which never go past 0 or 100.",
                    min: 1, max: 100),
                Variable("resultVariable", "Result variable", "brightness",
                    "Variable that receives the brightness the screens are left at, from 0 to 100.",
                    namesVariable: true, defaultValue: "brightness"),
            ],
        },

        // ------------------------------------------------------------------ window
        new()
        {
            Key = "window.exists",
            Category = ActionCategory.Window,
            DisplayName = "Window Exists",
            Description = "Check whether a matching window is open.",
            Parameters =
            [
                ..WindowTarget(),
                Variable("resultVariable", "Result variable", "found",
                    "Variable that receives true or false.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "window.waitFor",
            Category = ActionCategory.Window,
            DisplayName = "Wait for Window",
            Description = "Wait until a matching window appears.",
            Parameters =
            [
                ..WindowTarget(),
                Number("timeoutMs", "Timeout", 10000, "How long to wait before giving up."),
                Variable("resultVariable", "Result variable", "window",
                    "Variable that receives the window title.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "window.activate",
            Category = ActionCategory.Window,
            DisplayName = "Activate Window",
            Description = "Bring a window to the front, restoring it first if it was shrunk.",
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
            Description = "Shrink a window down to the taskbar.",
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
            Description = "Make a window fill the screen.",
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
            Description = "Put a shrunk or full-screen window back to its normal size.",
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
            Description = "Move a window and give it a new size.",
            Parameters =
            [
                ..WindowTarget(),
                Number("x", "X", 0, "Left edge, in screen pixels.", min: -100_000m, max: 100_000m),
                Number("y", "Y", 0, "Top edge, in screen pixels.", min: -100_000m, max: 100_000m),
                Number("width", "Width", 800, "New width in pixels.", min: 1m, max: 100_000m),
                Number("height", "Height", 600, "New height in pixels.", min: 1m, max: 100_000m),
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
            Category = ActionCategory.Window,
            DisplayName = "List Windows",
            Description = "Collect the titles of the open windows, or of the ones that match, "
                + "into a list.",
            Parameters =
            [
                Text("filter", "Filter", "notepad",
                    "Keep only the windows whose title, process or class contains this text. "
                    + "Leave it empty for every window.", required: false),
                Choice("filterBy", "Filter by", ["title", "process", "class"], "title",
                    "Which part of a window the filter text is compared with."),
                Variable("resultVariable", "Result variable", "windows",
                    "Variable that receives the list of titles.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "window.info",
            Category = ActionCategory.Window,
            DisplayName = "Window Info",
            Description = "Read where a window sits and how big it is into variables.",
            Parameters =
            [
                ..WindowTarget(),
                Variable("resultVariable", "Result variable", "box",
                    "Variable that receives the window's rectangle, written as x,y,width,height "
                    + "so it can be used as a search region. .x, .y, .width and .height hold the "
                    + "parts on their own, and .title holds what the title bar says.",
                    namesVariable: true, defaultValue: "box"),
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
                KeyBind("key", "Key", "Key to press, for example F5 or Enter."),
                Number("holdMs", "Hold", 50, "How long the key stays down."),
                Number("repeat", "Repeat", 1, "How many times to press the key.", min: 1),
                Number("intervalMs", "Interval", 0, "Pause between repeated presses."),
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
                KeyBind("key", "Key"),
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
                KeyBind("key", "Key"),
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
                Text("keys", "Keys", "Ctrl+Shift+S", "Keys joined with + and pressed together."),
                Number("holdMs", "Hold", 50, "How long the combination stays down."),
                Number("repeat", "Repeat", 1, "How many times to send the combination.", min: 1),
                Number("intervalMs", "Interval", 0, "Pause between repeated presses."),
                ..Delivery(),
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
                Multiline("text", "Text", "Text to type.", "Hello world"),
                Number("intervalMs", "Interval", 30, "Delay between characters."),
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
                Number("x", "X", 0, "Target screen column."),
                Number("y", "Y", 0, "Target screen row."),
                ..Anchor(withElement: true),
                Number("durationMs", "Duration", 0, "0 jumps straight to the target."),
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
                Number("dx", "Offset X", 0, "Pixels to move horizontally.", min: -100000m),
                Number("dy", "Offset Y", 0, "Pixels to move vertically.", min: -100000m),
                Number("durationMs", "Duration", 0, "0 jumps straight to the target."),
                Movement(),
                ..Delivery(),
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
                Number("x", "X", 0, "Screen column to click."),
                Number("y", "Y", 0, "Screen row to click."),
                ..Anchor(withElement: true),
                Number("clicks", "Clicks", 1, "How many clicks to send.", min: 1),
                Number("intervalMs", "Interval", 0, "Pause between repeated clicks."),
                Number("holdMs", "Hold", 0,
                    "How long the button stays down before it is released. 0 sends a normal "
                    + "quick click; a longer hold is for buttons that only answer a press-and-hold."),
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
                Number("x", "X", 0, "Screen column to click."),
                Number("y", "Y", 0, "Screen row to click."),
                ..Anchor(withElement: true),
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
                Number("x", "X", 0, "Screen column to press."),
                Number("y", "Y", 0, "Screen row to press."),
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
                Number("x", "X", 0, "Screen column to release at."),
                Number("y", "Y", 0, "Screen row to release at."),
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
                Choice("direction", "Direction", ["up", "down", "left", "right"], "down"),
                Choice("unit", "Unit", ["notches", "pixels"], "notches",
                    "Notches are the steps a wheel normally turns in. Pixels measure the same " +
                    "turn more finely, with 120 pixels to a notch; an application that only " +
                    "looks at whole notches may ignore the smaller amounts."),
                Number("amount", "Amount", 3, "How far to scroll, counted in the unit above.", min: 1),
                Number("smoothMs", "Smooth", 0,
                    "Spread the scroll over this many milliseconds instead of sending it in one " +
                    "jump, so an application that animates its scrolling can follow. 0 sends it " +
                    "all at once."),
                Number("x", "X", 0, "Screen column to scroll at."),
                Number("y", "Y", 0, "Screen row to scroll at."),
                ..Anchor(withElement: true),
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
                Number("startX", "Start X", 0, "Where the drag begins."),
                Number("startY", "Start Y", 0),
                Number("endX", "End X", 0, "Where the drag ends."),
                Number("endY", "End Y", 0),
                ..Anchor(withElement: true),
                Button(),
                Number("durationMs", "Duration", 300, "How long the drag takes."),
                Number("steps", "Move steps", 20, "Intermediate move events sent while dragging.", min: 1),
                Movement(),
                ..Delivery(),
            ],
        },

        // ----------------------------------------------------------------- gamepad
        new()
        {
            Key = "gamepad.connect",
            Category = ActionCategory.Input,
            DisplayName = "Connect Controller",
            Description = "Put the virtual controller — an Xbox 360 pad — on the machine, which "
                + "the steps after this one press the buttons of. The steps that drive a controller "
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
                    "A tap presses and lets go; hold and let up are for a button that has to stay "
                    + "down across other steps.",
                    labels: ["Tap", "Hold down", "Let up"]),
                Number("holdMs", "Hold", 50, "How long the button stays down when it is tapped."),
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
                    labels: ["Left stick", "Right stick"]),
                Number("x", "X", 0,
                    "How far sideways: 100 is all the way right, -100 all the way left, 0 centred.",
                    min: -100, max: 100),
                Number("y", "Y", 0,
                    "How far up or down: 100 is all the way up, -100 all the way down, 0 centred.",
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
                    labels: ["Left trigger", "Right trigger"]),
                Number("amount", "Amount", 100, "How far it is pulled, as a whole percent.",
                    min: 0, max: 100),
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
            Category = ActionCategory.Vision,
            DisplayName = "Capture",
            Description = "Capture a screen region into an image variable.",
            Parameters =
            [
                Number("x", "X", 0, "Region origin column."),
                Number("y", "Y", 0, "Region origin row."),
                Number("width", "Width", 100, "Region width in pixels.", min: 1),
                Number("height", "Height", 100, "Region height in pixels.", min: 1),
                ..Anchor(withElement: true),
                Variable("saveTo", "Save to variable", "shot",
                    "Variable that receives the captured image. $name.x, $name.y, $name.width and "
                    + "$name.height hold the rectangle it covered, so a later step can search it.",
                    namesVariable: true, defaultValue: "shot"),
            ],
        },
        new()
        {
            Key = "vision.captureWindow",
            Category = ActionCategory.Vision,
            DisplayName = "Capture Window",
            Description = "Capture a whole window, title bar and all, into an image variable.",
            Parameters =
            [
                ..WindowTarget(),
                Variable("saveTo", "Save to variable", "shot",
                    "Variable that receives the captured image. $name.x, $name.y, $name.width and "
                    + "$name.height hold the rectangle it covered, so a later step can search it.",
                    namesVariable: true, defaultValue: "shot"),
            ],
        },
        new()
        {
            Key = "vision.findImage",
            Category = ActionCategory.Vision,
            DisplayName = "Find Image",
            Description = "Look for a reference image on screen.",
            Parameters =
            [
                Image("image", "Image file", @"C:\images\ok.png",
                   "Reference image: a file path, or the variable a Capture step saved ($shot)."),
                Number("confidence", "Confidence %", 90, "Required match confidence.", max: 100, advanced: true),
                Text("region", "Search region", required: false, hint: RegionHint, acceptsVariables: true),
                ..Anchor(),
                MatchIndex(),
                AllMatches(),
                Variable("resultVariable", "Result variable", "match",
                    "Variable that receives the match centre, empty when nothing was found. "
                    + "$name.x, $name.y, $name.width, $name.height and $name.score hold the parts, "
                    + "and $name.count and $name.list the whole set when the step records it.",
                    namesVariable: true, defaultValue: "match"),
            ],
        },
        new()
        {
            Key = "vision.waitImage",
            Category = ActionCategory.Vision,
            DisplayName = "Wait Image",
            Description = "Wait until a reference image appears on screen.",
            Parameters =
            [
                Image("image", "Image file", @"C:\images\ok.png",
                    "Reference image to wait for: a file path, or the variable a Capture saved."),
                Number("confidence", "Confidence %", 90, "Required match confidence.", max: 100, advanced: true),
                Text("region", "Search region", required: false, hint: RegionHint, acceptsVariables: true),
                ..Anchor(),
                MatchIndex(),
                Number("timeoutMs", "Timeout", 5000, "Give up after this long."),
                Number("intervalMs", "Interval", 200, "Delay between checks."),
                Variable("resultVariable", "Result variable", "match",
                    "Variable that receives the match centre. $name.x, $name.y and $name.score "
                    + "hold the parts.", namesVariable: true, defaultValue: "match"),
            ],
        },
        new()
        {
            Key = "vision.clickImage",
            Category = ActionCategory.Vision,
            DisplayName = "Click Image",
            Description = "Find a reference image on screen and click it.",
            Parameters =
            [
                Image("image", "Image file", @"C:\images\ok.png"),
                Number("confidence", "Confidence %", 90, "Required match confidence.", max: 100, advanced: true),
                Text("region", "Search region", required: false, hint: RegionHint, acceptsVariables: true),
                ..Anchor(),
                MatchIndex(),
                Number("offsetX", "Offset X", 0, "Pixels added to the match centre.",
                    min: -100000m, advanced: true),
                Number("offsetY", "Offset Y", 0, min: -100000m, advanced: true),
                Number("timeoutMs", "Timeout", 5000, "Wait this long for the image before giving up."),
                Button(),
            ],
        },
        new()
        {
            Key = "vision.getPixel",
            Category = ActionCategory.Vision,
            DisplayName = "Get Pixel",
            Description = "Read the colour of a pixel.",
            Parameters =
            [
                Number("x", "X", 0, "Screen column to sample."),
                Number("y", "Y", 0, "Screen row to sample."),
                ..Anchor(),
                Variable("resultVariable", "Result variable", "color",
                    "Variable that receives the colour.", namesVariable: true, defaultValue: "color"),
                Toggle("asHex", "Store as hex", true, "Store #RRGGBB instead of raw colour channels."),
            ],
        },
        new()
        {
            Key = "vision.waitColor",
            Category = ActionCategory.Vision,
            DisplayName = "Wait Color",
            Description = "Wait until a pixel shows a specific colour.",
            Parameters =
            [
                Number("x", "X", 0, "Screen column to watch."),
                Number("y", "Y", 0, "Screen row to watch."),
                ..Anchor(),
                ColorPick("color", "Colour", "#000000", "Colour the pixel has to show."),
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference.", max: 100, advanced: true),
                Number("timeoutMs", "Timeout", 5000, "Give up after this long."),
            ],
        },
        new()
        {
            Key = "vision.findColor",
            Category = ActionCategory.Vision,
            DisplayName = "Find Color",
            Description = "Look for a colour inside a screen region.",
            Parameters =
            [
                ColorPick("color", "Colour", "#000000", "Colour to look for."),
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference.", max: 100, advanced: true),
                Number("matchIndex", "Match number", 1,
                    "Which hit to use, counted from the top left: down the screen first, then "
                    + "across. 1 is the first one.", min: 1, max: 200),
                AllMatches(),
                Text("region", "Search region", required: false, hint: RegionHint, acceptsVariables: true),
                ..Anchor(),
                Number("timeoutMs", "Timeout", 0,
                    "0 looks once and leaves the result empty when the colour is not there. "
                    + "A number waits that long for it and fails when it never turns up."),
                Number("intervalMs", "Interval", 200, "Delay between checks while waiting."),
                Variable("resultVariable", "Result variable", "match",
                    "Variable that receives where the colour was found, empty when it was not. "
                    + "$name.x, $name.y and $name.score hold the parts, and $name.count and "
                    + "$name.list the whole set when the step records it.",
                    namesVariable: true, defaultValue: "match"),
            ],
        },
        new()
        {
            Key = "vision.waitStable",
            Category = ActionCategory.Vision,
            DisplayName = "Wait for Screen to Settle",
            Description = "Wait until the screen stops changing, so the step after it works on a "
                + "picture that is not still being drawn.",
            Parameters =
            [
                Text("region", "Search region", required: false, hint: RegionHint, acceptsVariables: true),
                ..Anchor(),
                Number("quietMs", "Hold still for", 500,
                    "How long the area has to stay unchanged before the step goes on. A page that "
                    + "is still filling in, or an animation still running, keeps this from being "
                    + "reached."),
                Number("tolerance", "Tolerance %", 5,
                    "How far a pixel may drift and still count as the same pixel. Raise it for a "
                    + "picture with noise or a slow fade in it.", max: 100, advanced: true),
                Number("changedPercent", "Allowed change %", 0.1m,
                    "How much of the area may move and still count as still. A spinner turning, a "
                    + "list scrolling or a progress bar filling moves more than this.",
                    max: 100, advanced: true),
                Number("timeoutMs", "Timeout", 10000, "Give up after this long."),
                Number("intervalMs", "Interval", 100,
                    "How often the area is looked at. Shorter notices the screen moving sooner; "
                    + "longer costs less while the wait runs.", min: 1, advanced: true),
            ],
        },

        // -------------------------------------------------------------------- ocr
        new()
        {
            Key = "ocr.recognize",
            Category = ActionCategory.Ocr,
            DisplayName = "Recognize",
            Description = "Read text from a screen region.",
            Parameters =
            [
                Number("x", "X", 0, "Region origin column."),
                Number("y", "Y", 0, "Region origin row."),
                Number("width", "Width", 400, "Region width in pixels.", min: 1),
                Number("height", "Height", 120, "Region height in pixels.", min: 1),
                ..Anchor(),
                Choice("language", "Language", ["auto", "en", "zh"], "auto"),
                Content(),
                Toggle("table", "Read as a table", false,
                    "Keep the lines and columns the writing was read in: the variable holds rows of "
                    + "cells — the same shape as reading a table through UI Automation — and "
                    + "$name.text holds the whole lot as text, one line per row."),
                Preprocess(),
                Variable("resultVariable", "Result variable", "text",
                    "Variable that receives the recognised text, or the rows of cells when the step "
                    + "reads a table. $name.text is the writing either way.",
                    namesVariable: true, defaultValue: "text"),
            ],
        },
        new()
        {
            Key = "ocr.findText",
            Category = ActionCategory.Ocr,
            DisplayName = "Find Text",
            Description = "Search the screen for text.",
            Parameters =
            [
                Text("text", "Text to find", "Save", "Text to look for."),
                Text("region", "Search region", required: false, hint: RegionHint, acceptsVariables: true),
                ..Anchor(),
                TextMatch(),
                Content(),
                Preprocess(),
                MinScore(),
                AllMatches(),
                Variable("resultVariable", "Result variable", "match",
                    "Variable that receives the match centre, empty when the text was not found. "
                    + "$name.x, $name.y, $name.width, $name.height, $name.text and $name.score hold "
                    + "the parts, and $name.count and $name.list the whole set when the step records "
                    + "it. The score is what the reading model made of it, not a percentage: bigger "
                    + "means surer.",
                    namesVariable: true, defaultValue: "match"),
            ],
        },
        new()
        {
            Key = "ocr.clickText",
            Category = ActionCategory.Ocr,
            DisplayName = "Click Text",
            Description = "Find text on screen and click it.",
            Parameters =
            [
                Text("text", "Text to find", "Save"),
                TextMatch(),
                Content(),
                Preprocess(),
                MinScore(),
                Number("offsetX", "Offset X", 0, "Pixels added to the match centre.",
                    min: -100000m, advanced: true),
                Number("offsetY", "Offset Y", 0, min: -100000m, advanced: true),
                Number("timeoutMs", "Timeout", 5000, "Wait this long for the text before giving up."),
                Button(),
            ],
        },

        // -------------------------------------------------------------------- uia
        new()
        {
            Key = "uia.exists",
            Category = ActionCategory.Uia,
            DisplayName = "Exists",
            Description = "Check whether a UI Automation element exists.",
            Parameters =
            [
                Window(),
                Selector(),
                MatchIndex(),
                Number("timeoutMs", "Timeout", 0, "0 checks once and returns immediately."),
                Variable("resultVariable", "Result variable", "exists",
                    "Variable that receives true or false.",
                    namesVariable: true, defaultValue: "exists"),
            ],
        },
        new()
        {
            Key = "uia.find",
            Category = ActionCategory.Uia,
            DisplayName = "Find Element",
            Description = "Find a UI Automation element and remember where it is.",
            Parameters =
            [
                Window(),
                Selector(),
                MatchIndex(),
                AllMatches(),
                Variable("resultVariable", "Result variable", "element",
                    "Variable that receives the element's centre, empty when it was not found. "
                    + "$name.x, $name.y, $name.width, $name.height and $name.text hold the parts, "
                    + "which is what a later step measures from, and $name.count and $name.list "
                    + "the whole set when the step records it.",
                    namesVariable: true, defaultValue: "element"),
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
                Window(),
                Selector(),
                MatchIndex(),
                Number("timeoutMs", "Timeout", 10000, "Give up after this long."),
                Number("pollMs", "Check every", 200, "Delay between checks."),
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
                Window(),
                Selector(),
                MatchIndex(),
                Number("timeoutMs", "Timeout", 5000, "Wait this long for the element before clicking."),
                Button(),
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
                Window(),
                Selector(),
                MatchIndex(),
                Text("text", "Text", "Hello", "Text written into the element."),
                Toggle("clearFirst", "Clear first", true, "Clear the field before typing."),
            ],
        },
        new()
        {
            Key = "uia.getText",
            Category = ActionCategory.Uia,
            DisplayName = "Get Text",
            Description = "Read text from a UI Automation element.",
            Parameters =
            [
                Window(),
                Selector(),
                MatchIndex(),
                Variable("resultVariable", "Result variable", "text",
                    "Variable that receives the text.",
                    namesVariable: true, defaultValue: "text"),
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
                Window(),
                Selector(),
                MatchIndex(),
                Text("item", "Entry", "Ready", required: false,
                    hint: "The entry to pick, by the text it shows; a $variable holding the text "
                          + "works too. Leave it empty and fill in the number below instead.",
                    acceptsFormula: true),
                Number("itemIndex", "Entry number", 0,
                    "Which entry to pick, counted from 1. 0 means pick the one named above.",
                    max: 10000),
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
                Window(),
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
                Window(),
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
                Window(),
                Selector(),
                MatchIndex(),
            ],
        },
        new()
        {
            Key = "uia.readTable",
            Category = ActionCategory.Uia,
            DisplayName = "Read Table",
            Description = "Read a table or a grid into a list of rows.",
            Parameters =
            [
                Window(),
                Selector(),
                MatchIndex(),
                Number("maxRows", "Rows to read", 100,
                    "Stop after this many rows of data, counted from the top. Rows that hold "
                    + "nothing at all — the strip the column titles live in, and the empty row "
                    + "some grids keep at the bottom for typing a new one in — are left out.",
                    min: 1, max: 10000, advanced: true),
                Variable("columnsVariable", "Column names variable", "columns",
                    "Variable that receives the names of the columns, in column order, which is how "
                    + "a macro finds the column it wants. Leave empty for none.",
                    required: false, namesVariable: true),
                Variable("resultVariable", "Result variable", "table",
                    "Variable that receives the rows. $name holds a list of rows, and every row is "
                    + "a list of cells, the same shape the CSV reader gives back.",
                    namesVariable: true, defaultValue: "table"),
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
                Window(hint: "Window title, or a $variable holding it. The picker fills in one of "
                             + "the windows that are open.", required: true),
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
                    + "gets a message naming the command that puts them there.",
                    labels: ["Edge (already on Windows)", "Chrome", "Chromium", "Firefox", "WebKit"]),
                Text("url", "Address", "https://example.com",
                    "The page to open. Leave it empty to open the browser without a page yet.",
                    required: false),
                Toggle("headless", "Hidden window", false,
                    "Open the browser without showing it. A page kept off the screen is quicker, "
                    + "but nothing can be watched while the macro runs."),
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
                Text("url", "Address", "https://example.com", "The page to open."),
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
                    "What to type. Variables are filled in first, so a macro can type what it just "
                    + "read somewhere else.", acceptsFormula: true),
            ],
        },
        new()
        {
            Key = "browser.readText",
            Category = ActionCategory.Browser,
            DisplayName = "Read Text",
            Description = "Reads the text of an element, or of the whole page.",
            Parameters =
            [
                Text("target", "Selector", "",
                    "Which element to read, named the way the click action names one. Leave it "
                    + "empty to read the whole page.", required: false),
                Variable("resultVariable", "Result variable", "text",
                    "Variable that receives the text.", required: false, namesVariable: true),
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
                    "Which tab the steps after this one work on. \"The newest one\" is the tab that "
                    + "appeared last — the one a click leaves behind when it opens a new tab — and "
                    + "this step waits a few seconds for it, because a tab reaches the browser a "
                    + "moment after the click that opened it; none turning up is an error rather "
                    + "than staying where the macro was, which on a site whose pages look alike "
                    + "would go unnoticed.",
                    labels: ["The newest one", "By number", "By title", "By address"]),
                Number("index", "Tab number", 1,
                    "Which tab, counted from the left of the tab strip starting at 1. Used with "
                    + "\"by number\" above.", min: 1, max: 50),
                Text("match", "Title or address holds", "",
                    "The text the tab's title (or address) has to contain. Used with \"by title\" "
                    + "and \"by address\" above.", required: false),
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
            Category = ActionCategory.Script,
            DisplayName = "Run Script",
            Description = "Run a short script with an interpreter of your choice.",
            Parameters =
            [
                Choice("language", "Interpreter",
                    ["powershell", "cmd", "node", "python", "custom"], "powershell",
                    "Which interpreter runs the script. PowerShell and Command Prompt are on every "
                    + "Windows machine; Node and Python have to be installed first; another "
                    + "program is for anything else the machine has, such as dotnet-script.",
                    labels: ["PowerShell", "Command Prompt", "Node.js", "Python", "Another program"]),
                Text("interpreter", "Interpreter command", "dotnet-script", required: false,
                    hint: "The program that runs the script, with any flags of its own in front, "
                          + "used with \"another program\" above. The script's path goes after it, "
                          + "the way it would be typed on a command line. A path with spaces in it "
                          + "goes in quotes."),
                Text("extension", "Script file ending", ".csx", required: false,
                    hint: "The file name ending that program reads, such as .csx for dotnet-script "
                          + "or .vbs for cscript. Only used with \"another program\" above."),
                Choice("encoding", "Script file encoding", ["auto", .. TextEncoding.Names], "auto",
                    "How the interpreter's copy of the script is written. Left to decide for "
                    + "itself, PowerShell gets a byte-order mark and the rest do not, which is what "
                    + "each of them reads correctly; a program that reads the system code page "
                    + "instead, cscript for one, needs GBK to read Chinese text.",
                    labels: ["As the interpreter expects", "UTF-8", "UTF-8 with BOM",
                        "GBK (Chinese)", "UTF-16"]),
                Multiline("script", "Script",
                    "The script itself. {{name}} is replaced by what the variable name holds "
                    + "before the script runs, which is how a macro value gets in; everything the "
                    + "script prints comes back in the result variable below.",
                    "Write-Host \"Hello\"", foreign: true),
                Text("arguments", "Arguments", "first second", required: false,
                    hint: "Extra arguments for the script's command line. {{name}} is filled in "
                          + "here as well.", foreign: true),
                PickablePath("folder", "Working folder", string.Empty,
                    "The folder the script runs in. Leave it empty to run it in the macros folder, "
                    + "or wherever WhaleGenie is when that folder is not there yet.",
                    PathIntent.Folder, required: false),
                Number("timeoutMs", "Timeout", 60000,
                    "Stop the script and fail the step after this long."),
                Variable("resultVariable", "Result variable", "output",
                    "Variable that receives what the script printed, without the blank lines "
                    + "around it.",
                    namesVariable: true, defaultValue: "output"),
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
                Image("image", "Image file", @"C:\images\ok.png"),
                Number("confidence", "Confidence %", 90, "Required match confidence.", max: 100, advanced: true),
                Text("region", "Search region", required: false, hint: RegionHint, acceptsVariables: true),
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
                Image("image", "Image file", @"C:\images\ok.png"),
                Number("confidence", "Confidence %", 90, "Required match confidence.", max: 100, advanced: true),
                Text("region", "Search region", required: false, hint: RegionHint, acceptsVariables: true),
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
                Text("text", "Text", "Ready"),
                Text("region", "Search region", required: false, hint: RegionHint, acceptsVariables: true),
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
                Text("text", "Text", "Ready"),
                Text("region", "Search region", required: false, hint: RegionHint, acceptsVariables: true),
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
                Window(),
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
                Window(),
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
                Number("x", "X", 0, "Screen column to compare."),
                Number("y", "Y", 0, "Screen row to compare."),
                ..Anchor(),
                ColorPick("color", "Colour", "#000000"),
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference.", max: 100, advanced: true),
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
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference.", max: 100, advanced: true),
                Choice("mode", "Mode", ["all", "any"], "all",
                    "Whether every point has to match or one is enough.",
                    labels: ["Every point", "Any point"]),
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
                    "How the variable is compared against the value.",
                    labels: ComparisonOperatorLabels),
                Variable("value", "Variable or value", "3",
                    "Pick another variable from the list, or type a number or text. "
                    + "Not needed for \"exists\".",
                    required: false),
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
                    "How that step should have ended: it did what it was asked to, it failed, or "
                    + "the run never got to it.",
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
                    + "with $items."),
                Variable("value", "Value", "1",
                    "The value that has to be in the list. A number is compared as a number, so "
                    + "1 finds 1.0, and text is compared without caring about case."),
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
                    acceptsVariables: true),
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
                Choice("match", "Look at", ["title", "process"], "title",
                    "Whether the value is looked for in the window's title, which is what is "
                    + "written on it, or in the name of the program that owns it.",
                    labels: ["The title", "The program"]),
                Text("value", "Window", "Untitled - Notepad",
                    "Part of it is enough, and case does not matter. Leave it empty to ask "
                    + "whether any window at all is open.",
                    required: false, acceptsVariables: true),
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
                    "The value to place on the number line."),
                Variable("min", "Lowest", "1",
                    "One end of the range. Either end may be written first."),
                Variable("max", "Highest", "10",
                    "The other end of the range. Both ends count as inside, so a range of 1 to "
                    + "10 holds 1 and 10."),
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
                    "How the first date stands against the second. \"The same day\" ignores the "
                    + "time of day, which is what asking whether something has happened today "
                    + "needs.",
                    labels: ["Is before", "Is after", "Is exactly", "Is the same day"]),
                Variable("right", "Against", "2026-01-01",
                    "The date it is compared against, written the same ways.",
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
                    "How the child conditions are combined. Available once the group has two or more.",
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
                Number("percent", "Chance %", 50, "Probability that the condition is true.", max: 100),
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
        ActionCategory.Spreadsheet => SpreadsheetIcon,
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
        bool foreign = false, bool acceptsVariables = false, bool acceptsFormula = false)
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
        };

    private static ActionParameter Multiline(string name, string label, string hint = "",
        string placeholder = "", bool required = true, bool foreign = false)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.MultilineText,
            Hint = hint,
            Placeholder = placeholder,
            Required = required,
            ForeignText = foreign,
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
        bool acceptsFormula = false)
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
        string hint, PathIntent intent, string filter = "", bool required = true)
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
        };

    private static ActionParameter Number(string name, string label, decimal defaultValue,
        string hint = "", decimal min = 0m, decimal max = StepMeta.LongestPauseMs,
        bool advanced = false)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Number,
            Hint = hint,
            DefaultValue = defaultValue.ToString(CultureInfo.InvariantCulture),
            Minimum = min,
            Maximum = max,
            // Every pause in the catalogue ends its name in "Ms", so the editor can offer the
            // unit dropdown without each call site having to ask for it. A number that is not a
            // length of time is simply never named that way.
            IsDuration = name == "ms" || name.EndsWith("Ms", StringComparison.Ordinal),
            Advanced = advanced,
        };

    private static ActionParameter Toggle(string name, string label, bool defaultValue = false,
        string hint = "")
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Bool,
            Hint = hint,
            DefaultValue = defaultValue ? "true" : "false",
        };

    private static ActionParameter Choice(string name, string label, string[] options,
        string defaultValue, string hint = "", string[]? labels = null,
        string enabledBySibling = "", bool advanced = false)
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

    private static ActionParameter ColorPick(string name, string label, string defaultValue = "#000000",
        string hint = "")
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Color,
            DefaultValue = defaultValue,
            Hint = hint,
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

    /// <summary>Mouse button picker shared by every input action that clicks.</summary>
    private static ActionParameter Button()
        => Choice("button", "Button", ["left", "right", "middle", "back", "forward"], "left");

    /// <summary>
    /// The button of the virtual controller, listed with the names the macro writes, which are the
    /// ones printed on the pad.
    /// </summary>
    private static ActionParameter GamepadButton()
        => Choice("button", "Button", [.. GamepadNames.Buttons], "a",
            "The button to press, named the way it is printed on the pad.",
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
        "Optional x,y,width,height limit, written out or held in a variable. Several rectangles may "
        + "be listed, separated by a semicolon, and every one of them is searched. Leave empty to "
        + "search the whole screen.";

    /// <summary>
    /// Which of several hits a step means, shared by the actions that can find more than one. Hits
    /// are counted from the top left, the order a person counts them in on a screenshot.
    /// </summary>
    private static ActionParameter MatchIndex()
        => Number("matchIndex", "Match number", 1,
            "Which hit to use, counted from the top left: down the screen first, then across. "
            + "1 is the first one.", min: 1, max: 200, advanced: true);

    /// <summary>
    /// Whether a step records the whole set of hits as well as the one it picked, shared by the
    /// finders that can report more than one. The list is what lets a macro walk every place
    /// something turned up.
    /// </summary>
    private static ActionParameter AllMatches()
        => Toggle("allMatches", "Record every match", false,
            "Also record how many places matched and where they all are: $name.count is the number "
            + "and $name.list holds one \"x,y\" per hit, ready for count(), get() and forEach.");

    /// <summary>
    /// How a step's input is sent, shared by every action that presses a key or a mouse button.
    /// In front is how input has always been sent; the other two are what let a macro work on a
    /// window it does not have the focus of, or look like real hardware.
    /// </summary>
    private static ActionParameter[] Delivery() =>
    [
        Choice("inputMode", "Input mode", ["foreground", "background", "driver"], "foreground",
            "How this step's input is sent. In front goes to whatever window has the focus. "
            + "Background posts the messages at the target window instead, which needs no focus "
            + "and leaves the on-screen pointer where it is. Driver sends it through a virtual "
            + "USB device, which needs the usbip-win2 driver and the VIIPER server; the settings "
            + "window shows whether this machine has them.",
            labels: ["In front", "Background (posted)", "Driver (virtual device)"],
            advanced: true),
        new()
        {
            Name = "targetWindow",
            Label = "Target window",
            Kind = ActionParameterKind.Window,
            Placeholder = "Notepad",
            Hint = "The window background input posts its messages at; part of the title is "
                   + "enough. The other input modes do not use it.",
            Required = false,
            Advanced = true,
        },
    ];

    /// <summary>
    /// What an action's coordinates are measured from, shared by every action that names a
    /// position or a rectangle. Screen pixels are what every macro written before this setting
    /// used; the other two make the numbers follow a window, which is what stops a macro from
    /// aiming at the wrong place the moment the window is dragged somewhere else.
    /// <paramref name="withElement"/> adds a fourth choice, measuring from a control rather than a
    /// window. Only the actions that put the pointer somewhere offer it: a control is what a person
    /// actually aims at, and the actions that merely read a point would grow a field for nothing.
    /// </summary>
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
        new()
        {
            Name = "anchorWindow",
            Label = "Anchor window",
            Kind = ActionParameterKind.Window,
            Placeholder = "Notepad",
            Hint = "The window the coordinates are measured from; part of the title is enough. "
                   + "Leave it empty for the window in front. The screen mode does not use it.",
            Required = false,
            Advanced = true,
        },
        ..(withElement ? AnchorElement() : Array.Empty<ActionParameter>()),
    ];

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
            labels: ["Straight line", "Bent path", "Hand-like"]);

    /// <summary>
    /// A file or folder path, which may be written as an expression. The intent decides which
    /// dialog the button beside the field opens, and the filter which files it shows first.
    /// </summary>
    private static ActionParameter FilePath(string name, string label, string placeholder, string hint,
        PathIntent intent, string filter = "")
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Variable,
            Placeholder = placeholder,
            Hint = hint + " A path on its own is taken to be inside the macros folder.",
            PathIntent = intent,
            PathFilter = filter,
        };

    /// <summary>
    /// Which sheet of a workbook a step means, by the name on its tab. A name left empty is the
    /// first sheet, so a step does not have to know what a workbook calls its sheets in order to
    /// read the usual one.
    /// </summary>
    private static ActionParameter Sheet()
        => Text("sheet", "Sheet", "Sheet1", required: false,
            hint: "Which sheet to use, by the name on its tab. Leave empty for the first sheet.");

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
            "The character between two cells.",
            labels: mayBeAutomatic
                ? ["Work it out (auto)", "Comma ,", "Semicolon ;", "Tab", "Vertical bar |", "Space"]
                : ["Comma ,", "Semicolon ;", "Tab", "Vertical bar |", "Space"]),
        Text("separatorText", "Or this character", required: false, defaultValue: "",
            hint: "Fill this in for a character the list does not have, such as : or #, and it is "
                + "used instead of the one chosen above. Leave it empty for the chosen one."),
    ];

    /// <summary>
    /// How the text of a file is turned into bytes, shared by the actions that read or write text.
    /// UTF-8 without a mark is what everything new uses; the other three are what a machine that
    /// already has files on it, or a person who has to open them in an older program, needs.
    /// </summary>
    private static ActionParameter Encoding()
        => Choice("encoding", "Encoding", [.. TextEncoding.Names], TextEncoding.Default,
            "How the file's text is turned into bytes. UTF-8 is what everything new uses; UTF-8 "
            + "with a mark is what Notepad writes; GBK is what a Chinese Windows writes its own "
            + "text files in; UTF-16 is what some older Windows programs expect. Reading follows "
            + "the mark at the front of the file when it has one, and reads bytes that are not "
            + "UTF-8 at all in this machine's own code page — which is how a CSV another machine "
            + "exported still reads.",
            labels: ["UTF-8", "UTF-8 with BOM", "GBK (Chinese)", "UTF-16"]);

    /// <summary>How OCR text searches compare their match.</summary>
    private static ActionParameter TextMatch()
        => Choice("matchMode", "Match mode", ["contains", "exact", "regex"], "contains",
            "How the found text is compared against the search text.");

    /// <summary>
    /// Whether a step wants everything that can be read, or only the numbers. Shared by the three
    /// actions that read the screen with OCR, so one screen is read the same way by all of them.
    /// </summary>
    private static ActionParameter Content()
        => Choice("content", "Content", ["text", "digits"], "text",
            "All of the text, or only the numbers: a piece that holds a number keeps the number "
            + "itself, and the money sign, the thousands separators and the label around it are "
            + "dropped. A piece with no digit in it is not a number and is left out.");

    /// <summary>
    /// Whether a step wants the picture tidied up before it is read. Small, low-contrast writing on
    /// a busy background is where this pays off; on clean writing of a decent size it does nothing
    /// but cost a moment.
    /// </summary>
    private static ActionParameter Preprocess()
        => Choice("preprocess", "Clean the picture up", [.. WhaleGenie.Core.Devices.OcrPreprocess.Recipes],
            "none",
            "Tidy the picture up before reading it: grey takes the colour out, black and white is "
            + "grey with light and dark pushed apart — the cut is the picture's own average, so a "
            + "dark screen works as well as a light one — and twice as big helps with small writing. "
            + "The last one does both.");

    /// <summary>
    /// The score below which a step will not act on a reading, shared by the two OCR actions that
    /// look for text. The score is the reading model's own — the average, over the characters it
    /// read, of how far ahead its best guess was — so it is not a percentage and has no fixed
    /// range; the hint carries what was measured on a real screen so the number can be picked
    /// with something to go on. Zero, the default, keeps every reading.
    /// </summary>
    private static ActionParameter MinScore()
        => Number("minScore", "Lowest score", 0,
            "Leave a reading out when the model was less sure of it than this. Measured on a "
            + "real screen: clean writing scores around 40, writing too blurred to read around "
            + "24, and rubbish read off a busy background around 16. 0 keeps everything.",
            advanced: true);

    /// <summary>A window title, filled in from the windows that are open by the window picker.</summary>
    private static ActionParameter Window(string name = "window",
        string hint = "Optional window title filter. Leave empty to search every window.",
        bool required = false)
        => new()
        {
            Name = name,
            Label = "Window",
            Kind = ActionParameterKind.Window,
            Placeholder = "Notepad",
            Hint = hint,
            Required = required,
        };

    /// <summary>
    /// The window a window-management action works on, together with how to recognise it. A title
    /// is what a person reads off the title bar, but it changes with the document and the language;
    /// the program that owns the window and the class it registered hold still, which is what a
    /// macro wants when the same window keeps coming back under a different name.
    /// </summary>
    private static ActionParameter[] WindowTarget() =>
    [
        new()
        {
            Name = "title",
            Label = "Window",
            Kind = ActionParameterKind.Window,
            Placeholder = "Notepad",
            Hint = "What to recognise the window by, matched without regard to case. "
                   + "Leave empty for the window in front.",
            Required = false,
        },
        Choice("matchBy", "Match by", ["title", "process", "class"], "title",
            "Which part of a window the text above is compared with: its title, the name of the "
            + "program that owns it, or the window class that program registered."),
    ];

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
