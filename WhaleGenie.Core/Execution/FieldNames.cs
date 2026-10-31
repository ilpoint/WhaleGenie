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

    /// <summary>Where a copied submacro came from and which source version it froze.</summary>
    public const string Origin = "origin";

    public const string Version = "version";

    public const string Description = "description";

    public const string CpuSeconds = "cpuSeconds";
}
