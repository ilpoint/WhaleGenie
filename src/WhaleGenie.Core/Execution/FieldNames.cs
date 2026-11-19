namespace WhaleGenie.Core.Execution;

/// <summary>
/// Names that appear inside records rather than among an action's own parameters. They are file
/// format, so a reader, a writer and a row editor all spell them from here.
/// </summary>
public static class FieldNames
{
    /// <summary>The engine's own name of a result in a returns row.</summary>
    public const string Key = "key";

    /// <summary>The name a returns row is kept under.</summary>
    public const string Name = "name";

    public const string Left = "left";

    public const string Top = "top";

    public const string Width = "width";

    public const string Height = "height";

    /// <summary>The middle of the rectangle, which is the point an action aims at.</summary>
    public const string X = "x";

    public const string Y = "y";

    /// <summary>The rectangle written the way a search region is spelled: left,top,width,height.</summary>
    public const string Box = "box";

    /// <summary>How sure a match is: a picture's similarity or a reading's confidence.</summary>
    public const string Score = "score";

    /// <summary>The writing a match read, empty when it read none.</summary>
    public const string Text = "text";

    /// <summary>The kind a detection model put a match in, or -1 when it was not a detection.</summary>
    public const string Class = "class";

    /// <summary>The name a label file gives that kind, empty when it names none.</summary>
    public const string Label = "label";

    public const string Title = "title";

    public const string Process = "process";

    public const string Handle = "handle";

    public const string How = "how";

    public const string CaseSensitive = "caseSensitive";

    public const string Trim = "trim";

    public const string Multiline = "multiline";

    /// <summary>
    /// True on a window rule that says "the same window the steps before this one used" instead of
    /// naming a window of its own.
    /// </summary>
    public const string Follow = "follow";

    /// <summary>Which coordinate space a point's x and y are counted in.</summary>
    public const string Basis = "basis";

    public const string Minimized = "minimized";

    public const string Maximized = "maximized";

    /// <summary>The part of a window inside its border and under its title bar, as a rectangle text.</summary>
    public const string ClientBox = "clientBox";

    public const string Right = "right";

    public const string Bottom = "bottom";

    public const string Primary = "primary";

    public const string Enabled = "enabled";

    public const string Visible = "visible";

    public const string Value = "value";

    public const string AutomationId = "automationId";

    public const string ControlType = "controlType";

    /// <summary>The file a program was started from.</summary>
    public const string Info = "info";

    public const string Id = "id";

    public const string MemoryMb = "memoryMb";
    public const string Results = "results";
    public const string Error = "error";
    public const string Status = "status";
    public const string Macro = "macro";

    /// <summary>
    /// The row list a call step fills in: one row per argument of the macro it names, with the name
    /// of the argument and the value going into it. A call and a return are the same kind of thing
    /// — a list of named things — so they are written the same way.
    /// </summary>
    public const string Arguments = "arguments";

    /// <summary>
    /// The column of an argument row that says "this value counts, even though it is empty". An
    /// argument left out takes its declared default; the one filled in empty and not marked takes
    /// the default too, and only the mark makes the empty value the value.
    /// </summary>
    public const string Given = "given";

    /// <summary>A callable macro copied beneath another macro.</summary>
    public const string SubMacros = "subMacros";

    /// <summary>
    /// The values a macro declares for itself: name, kind and the value it starts every run with.
    /// </summary>
    public const string Locals = "locals";

    /// <summary>Where a copied submacro came from and which source version it froze.</summary>
    public const string Origin = "origin";

    public const string Version = "version";

    public const string Description = "description";

    public const string CpuSeconds = "cpuSeconds";

    // ---------------------------------------------------------------- triggers

    /// <summary>Which kind a trigger entry is. The one word every trigger record is read by.</summary>
    public const string Kind = "kind";

    /// <summary>The key, mouse button or controller button a key trigger answers to.</summary>
    public const string BindKey = "bindKey";

    public const string LoopMode = "loopMode";

    /// <summary>"Run this once, then wait to be armed again."</summary>
    public const string TriggerOnce = "triggerOnce";

    /// <summary>The record a timer trigger keeps its clock in.</summary>
    public const string Schedule = "schedule";

    public const string Mode = "mode";

    public const string Interval = "interval";

    public const string Unit = "unit";

    /// <summary>The time of day a daily schedule runs at.</summary>
    public const string Time = "time";

    /// <summary>The record a file trigger keeps what it watches in.</summary>
    public const string Watch = "watch";

    public const string Path = "path";

    /// <summary>Which change counts, for a file or a program. The kind decides what it is read as.</summary>
    public const string Change = "change";

    public const string Filter = "filter";

    public const string Subfolders = "subfolders";

    /// <summary>The menu page a menu entry sits on.</summary>
    public const string Page = "page";

    public const string Row = "row";

    public const string Column = "column";

    public const string Icon = "icon";

    public const string ColorMatch = "colorMatch";

    /// <summary>The point a colour trigger watches when it does not follow the cursor.</summary>
    public const string ColorPosition = "colorPosition";

    public const string UseCursorPosition = "useCursorPosition";

    public const string HexColor = "hexColor";

    public const string ColorTolerance = "colorTolerance";

    public const string Abbreviation = "abbreviation";

    public const string EndChars = "endChars";

    public const string MatchInsideWord = "matchInsideWord";

    public const string RequireEndChar = "requireEndChar";

    public const string EraseTyped = "eraseTyped";

    public const string KeepEndChar = "keepEndChar";

    public const string IdleSeconds = "idleSeconds";

    public const string WindowChange = "windowChange";

    /// <summary>The window rule a window or interface trigger is about.</summary>
    public const string Window = "window";

    /// <summary>The trigger entries a project holds.</summary>
    public const string Entries = "entries";

    public const string InterfaceLayouts = "interfaceLayouts";

    /// <summary>The record a project's menu pages and blocked rules live in.</summary>
    public const string Menu = "menu";

    public const string BlockedRules = "blockedRules";

    public const string Pages = "pages";

    /// <summary>The wheel's scenes and slots.</summary>
    public const string Wheel = "wheel";

    public const string Region = "region";

    public const string Default = "default";

    public const string ToggleHotkey = "toggleHotkey";
}
