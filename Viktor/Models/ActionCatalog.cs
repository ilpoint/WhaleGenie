using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;

namespace Viktor.Models;

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

    private static readonly Geometry FileIcon =
        Geometry.Parse("M6,3 H14 L18,7 V21 H6 Z M14,3 V7 H18 M9,12 H15 M9,16 H15");

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
                Number("ms", "Milliseconds", 1000, "How long to wait, in milliseconds."),
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
                Number("minMs", "Minimum ms", 400, "Shortest wait, in milliseconds."),
                Number("maxMs", "Maximum ms", 1200, "Longest wait, in milliseconds."),
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
                Number("timeoutMs", "Timeout ms", 10000, "Give up after this long."),
                Number("pollMs", "Check every ms", 200, "How long to wait between two checks.",
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
            Description = "Run child steps a fixed number of times.",
            Parameters =
            [
                Number("times", "Times", 10, "How many iterations to run.", min: 1),
                Number("intervalMs", "Interval ms", 0, "Pause between iterations."),
                Steps("body", "Body steps", "Steps that run once per iteration."),
            ],
        },
        new()
        {
            Key = "control.while",
            Category = ActionCategory.Control,
            DisplayName = "While",
            Description = "Repeat child steps while a condition stays true.",
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
            Description = "Run child steps once per item in a list or variable.",
            Parameters =
            [
                Expression("items", "Items", "$names",
                    "A list, or a $variable holding one. For example [1, 2, 3], split($text, \",\") "
                    + "or $names."),
                Text("itemVariable", "Item variable", hint: "Variable that receives the current item.",
                    defaultValue: "item"),
                Toggle("reverse", "Reverse order", false, "Iterate from the last item to the first."),
                Steps("body", "Body steps", "Steps that run once per item."),
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
            Description = "Write a message to the Viktor log.",
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
                + "here. The called macro shares this macro's variables, so it can read what you "
                + "set up and leave its answer behind.",
            Parameters =
            [
                MacroName("macro", "Macro", "Another macro in this project."),
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
                FilePath("path", "File", "notes.txt", "File to read."),
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
                FilePath("path", "File", "notes.txt", "File to write."),
                Multiline("text", "Text", "Text to write.", "", required: false),
                Choice("mode", "Mode", ["overwrite", "append"], "overwrite",
                    "Replace the file, or add to what is already there.",
                    labels: ["Replace it", "Add to the end"]),
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
                FilePath("path", "File", "notes.txt", "File or folder to look for."),
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
                FilePath("path", "File", "old.txt", "File to remove."),
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
                FilePath("from", "From", "report.csv", "File to copy."),
                FilePath("to", "To", @"backup\report.csv", "Where the copy goes."),
                Toggle("overwrite", "Overwrite", true, "Replace the copy when it is already there."),
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
                FilePath("folder", "Folder", ".", "Folder to look in."),
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
                FilePath("path", "File", "config.json", "JSON file to read."),
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
                FilePath("path", "File", "config.json", "JSON file to update."),
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
                FilePath("path", "File", "rows.csv", "CSV file to read."),
                Separator("separator", "Separator", "comma"),
                Toggle("hasHeader", "First row is a header", true,
                    "Leave the first row out of the result."),
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
                FilePath("path", "File", "rows.csv", "CSV file to write."),
                Variable("rows", "Rows", "$rows",
                    "A list of rows. Each row may itself be a list of cells.", namesVariable: false),
                Separator("separator", "Separator", "comma"),
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
                FilePath("path", "File", "state.json", "File to write."),
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
                FilePath("path", "File", "state.json", "File to read."),
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
                Number("timeoutMs", "Timeout ms", 5000, "How long to wait before giving up."),
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
                Number("timeoutMs", "Timeout ms", 1500, "How long to wait for the clipboard."),
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
                Text("file", "Program", "notepad.exe", "The program to start."),
                Text("arguments", "Arguments", "", "What to pass to the program.", required: false),
                Text("workingDirectory", "Working folder", "",
                    "Folder to start the program in.", required: false),
                Toggle("hidden", "Hidden window", true,
                    "Start the program without showing its window."),
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
                Number("timeoutMs", "Timeout ms", 10000, "How long to wait before giving up."),
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
                Number("timeoutMs", "Timeout ms", 60000, "How long to wait before giving up."),
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
                Text("file", "Command", "cmd.exe", "The program to run, such as cmd.exe."),
                Text("arguments", "Arguments", "/c dir",
                    "What to pass to the command.", required: false),
                Text("workingDirectory", "Working folder", "",
                    "Folder to run the command in.", required: false),
                Number("timeoutMs", "Timeout ms", 30000, "How long the command may run."),
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
                        "programFiles",
                    ],
                    "userName", "Which fact to read.",
                    labels:
                    [
                        "User name", "Computer name", "Domain", "Windows version",
                        "Processor count", "Windows folder", "Temp folder", "User folder",
                        "Desktop", "Documents", "Downloads", "Startup folder", "App data",
                        "Program Files",
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

        // ------------------------------------------------------------------ window
        new()
        {
            Key = "window.exists",
            Category = ActionCategory.Window,
            DisplayName = "Window Exists",
            Description = "Check whether a window with this title is open.",
            Parameters =
            [
                WindowTitle(),
                Variable("resultVariable", "Result variable", "found",
                    "Variable that receives true or false.", required: false, namesVariable: true),
            ],
        },
        new()
        {
            Key = "window.waitFor",
            Category = ActionCategory.Window,
            DisplayName = "Wait for Window",
            Description = "Wait until a window with this title appears.",
            Parameters =
            [
                WindowTitle(),
                Number("timeoutMs", "Timeout ms", 10000, "How long to wait before giving up."),
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
                WindowTitle(),
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
                WindowTitle(),
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
                WindowTitle(),
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
                WindowTitle(),
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
                WindowTitle(),
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
                WindowTitle(),
            ],
        },
        new()
        {
            Key = "window.list",
            Category = ActionCategory.Window,
            DisplayName = "List Windows",
            Description = "Collect the titles of the open windows into a list.",
            Parameters =
            [
                Variable("resultVariable", "Result variable", "windows",
                    "Variable that receives the list of titles.", required: false, namesVariable: true),
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
                Number("holdMs", "Hold ms", 50, "How long the key stays down."),
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
                Number("holdMs", "Hold ms", 50, "How long the combination stays down."),
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
                Number("intervalMs", "Interval ms", 30, "Delay between characters."),
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
                ..Anchor(),
                Number("durationMs", "Duration ms", 0, "0 jumps straight to the target."),
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
                Number("durationMs", "Duration ms", 0, "0 jumps straight to the target."),
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
                ..Anchor(),
                Number("clicks", "Clicks", 1, "How many clicks to send.", min: 1),
                Number("intervalMs", "Interval ms", 0, "Pause between repeated clicks."),
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
                ..Anchor(),
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
                ..Anchor(),
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
                ..Anchor(),
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
                Number("amount", "Amount", 3, "Number of wheel notches.", min: 1),
                Number("x", "X", 0, "Screen column to scroll at."),
                Number("y", "Y", 0, "Screen row to scroll at."),
                ..Anchor(),
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
                ..Anchor(),
                Button(),
                Number("durationMs", "Duration ms", 300, "How long the drag takes."),
                Number("steps", "Move steps", 20, "Intermediate move events sent while dragging.", min: 1),
                Movement(),
                ..Delivery(),
            ],
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
                ..Anchor(),
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
                Number("confidence", "Confidence %", 90, "Required match confidence.", max: 100),
                Text("region", "Search region", required: false,
                    hint: "Optional x,y,width,height limit, written out or held in a variable. "
                          + "Leave empty to search the whole screen."),
                ..Anchor(),
                Variable("resultVariable", "Result variable", "match",
                    "Variable that receives the match centre, empty when nothing was found. "
                    + "$name.x, $name.y, $name.width, $name.height and $name.score hold the parts.",
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
                Number("confidence", "Confidence %", 90, "Required match confidence.", max: 100),
                Number("timeoutMs", "Timeout ms", 5000, "Give up after this long."),
                Number("intervalMs", "Interval ms", 200, "Delay between checks."),
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
                Number("confidence", "Confidence %", 90, "Required match confidence.", max: 100),
                Number("offsetX", "Offset X", 0, "Pixels added to the match centre.", min: -100000m),
                Number("offsetY", "Offset Y", 0, min: -100000m),
                Number("timeoutMs", "Timeout ms", 5000, "Wait this long for the image before giving up."),
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
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference.", max: 100),
                Number("timeoutMs", "Timeout ms", 5000, "Give up after this long."),
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
                Variable("resultVariable", "Result variable", "text",
                    "Variable that receives the recognised text.",
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
                Text("region", "Search region", required: false,
                    hint: "Optional x,y,width,height limit, written out or held in a variable. "
                          + "Leave empty to search the whole screen."),
                ..Anchor(),
                TextMatch(),
                Variable("resultVariable", "Result variable", "match",
                    "Variable that receives the match centre, empty when the text was not found. "
                    + "$name.x, $name.y, $name.text and $name.score hold the parts.",
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
                Number("offsetX", "Offset X", 0, "Pixels added to the match centre.", min: -100000m),
                Number("offsetY", "Offset Y", 0, min: -100000m),
                Number("timeoutMs", "Timeout ms", 5000, "Wait this long for the text before giving up."),
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
                Number("timeoutMs", "Timeout ms", 0, "0 checks once and returns immediately."),
                Variable("resultVariable", "Result variable", "exists",
                    "Variable that receives true or false.",
                    namesVariable: true, defaultValue: "exists"),
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
                Number("timeoutMs", "Timeout ms", 10000, "Give up after this long."),
                Number("pollMs", "Poll ms", 200, "Delay between checks."),
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
                Number("timeoutMs", "Timeout ms", 5000, "Wait this long for the element before clicking."),
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
                Variable("resultVariable", "Result variable", "text",
                    "Variable that receives the text.",
                    namesVariable: true, defaultValue: "text"),
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
                Number("confidence", "Confidence %", 90, "Required match confidence.", max: 100),
                Text("region", "Search region", required: false,
                    hint: "Optional x,y,width,height limit, written out or held in a variable. "
                          + "Leave empty to search the whole screen."),
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
                Text("region", "Search region", required: false,
                    hint: "Optional x,y,width,height limit, written out or held in a variable. "
                          + "Leave empty to search the whole screen."),
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
                Number("tolerance", "Tolerance %", 5, "Allowed colour difference.", max: 100),
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

    /// <summary>Line-art icon shared by every action of a category.</summary>
    public static Geometry? IconFor(ActionCategory category) => category switch
    {
        ActionCategory.Control => ControlIcon,
        ActionCategory.File => FileIcon,
        ActionCategory.Clipboard => ClipboardIcon,
        ActionCategory.Process => ProcessIcon,
        ActionCategory.System => SystemIcon,
        ActionCategory.Window => WindowIcon,
        ActionCategory.Input => InputIcon,
        ActionCategory.Vision => VisionIcon,
        ActionCategory.Ocr => OcrIcon,
        ActionCategory.Uia => UiaIcon,
        ActionCategory.Condition => ConditionIcon,
        _ => null,
    };

    // ------------------------------------------------------------------ builders

    private static ActionParameter Text(string name, string label, string placeholder = "",
        string hint = "", bool required = true, string defaultValue = "")
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Text,
            Placeholder = placeholder,
            Hint = hint,
            Required = required,
            DefaultValue = defaultValue,
        };

    private static ActionParameter Multiline(string name, string label, string hint = "",
        string placeholder = "", bool required = true)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.MultilineText,
            Hint = hint,
            Placeholder = placeholder,
            Required = required,
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

    /// <summary>Name of a variable, edited with a suggestion list.</summary>
    private static ActionParameter Variable(string name, string label, string placeholder, string hint,
        bool required = true, bool namesVariable = false, string defaultValue = "")
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
        };

    private static ActionParameter Number(string name, string label, decimal defaultValue,
        string hint = "", decimal min = 0m, decimal max = 3_600_000m)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Number,
            Hint = hint,
            DefaultValue = defaultValue.ToString(CultureInfo.InvariantCulture),
            Minimum = min,
            Maximum = max,
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
        string enabledBySibling = "")
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
            + "USB device, which needs the VIIPER server running.",
            labels: ["In front", "Background (posted)", "Driver (virtual device)"]),
        new()
        {
            Name = "targetWindow",
            Label = "Target window",
            Kind = ActionParameterKind.Window,
            Placeholder = "Notepad",
            Hint = "The window background input posts its messages at; part of the title is "
                   + "enough. The other input modes do not use it.",
            Required = false,
        },
    ];

    /// <summary>
    /// What an action's coordinates are measured from, shared by every action that names a
    /// position or a rectangle. Screen pixels are what every macro written before this setting
    /// used; the other two make the numbers follow a window, which is what stops a macro from
    /// aiming at the wrong place the moment the window is dragged somewhere else.
    /// </summary>
    private static ActionParameter[] Anchor() =>
    [
        Choice("anchorMode", "Coordinates from", ["screen", "window", "client"], "screen",
            "What this step's coordinates are measured from. Screen is a pixel position on the "
            + "desktop, which is how macros were written before this setting existed. Window and "
            + "client measure from the window named below instead, so the step lands in the same "
            + "place inside that window after it has been moved or resized.",
            labels: ["Screen pixels", "Window top-left", "Window client area"]),
        new()
        {
            Name = "anchorWindow",
            Label = "Anchor window",
            Kind = ActionParameterKind.Window,
            Placeholder = "Notepad",
            Hint = "The window the coordinates are measured from; part of the title is enough. "
                   + "Leave it empty for the window in front. The screen mode does not use it.",
            Required = false,
        },
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

    /// <summary>A file or folder path, which may be written as an expression.</summary>
    private static ActionParameter FilePath(string name, string label, string placeholder, string hint)
        => new()
        {
            Name = name,
            Label = label,
            Kind = ActionParameterKind.Variable,
            Placeholder = placeholder,
            Hint = hint + " A path on its own is taken to be inside the macros folder.",
        };

    /// <summary>The character CSV cells are separated by.</summary>
    private static ActionParameter Separator(string name, string label, string defaultValue)
        => Choice(name, label, ["comma", "semicolon", "tab", "pipe"], defaultValue,
            "The character between two cells.",
            labels: ["Comma ,", "Semicolon ;", "Tab", "Vertical bar |"]);

    /// <summary>How OCR text searches compare their match.</summary>
    private static ActionParameter TextMatch()
        => Choice("matchMode", "Match mode", ["contains", "exact", "regex"], "contains",
            "How the found text is compared against the search text.");

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

    /// <summary>The window a window-management action works on, matched by part of its title.</summary>
    private static ActionParameter WindowTitle()
        => new()
        {
            Name = "title",
            Label = "Window",
            Kind = ActionParameterKind.Window,
            Placeholder = "Notepad",
            Hint = "Part of the window title, matched without regard to case. "
                   + "Leave empty for the window in front.",
            Required = false,
        };

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
