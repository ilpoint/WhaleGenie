namespace Viktor.Models;

/// <summary>What makes a macro start running.</summary>
public enum MacroTrigger
{
    /// <summary>Keyboard or mouse button activity.</summary>
    KeystrokesButtonInputs,

    /// <summary>A change of colour at a watched screen position.</summary>
    ColorPixelChanges,

    /// <summary>A time, either every so often or at a set time of day.</summary>
    Timer,

    /// <summary>A file or a folder being written to, added, removed or renamed.</summary>
    FileChanges,

    /// <summary>A program starting or finishing.</summary>
    Process,

    /// <summary>A window that matches a description appearing or going away.</summary>
    Window,
}

/// <summary>How a macro repeats once it has been triggered.</summary>
public enum MacroLoop
{
    /// <summary>Until Key Pressed Again.</summary>
    Toggle,

    /// <summary>While Holding Key.</summary>
    Hold,

    /// <summary>Once, When Key Pressed.</summary>
    Press,

    /// <summary>Once, When Key Released.</summary>
    Release,
}

/// <summary>How a timer trigger decides when the macro runs.</summary>
public enum ScheduleMode
{
    /// <summary>Every so many seconds, minutes or hours.</summary>
    Interval,

    /// <summary>Once a day, at a set time.</summary>
    Daily,
}

/// <summary>The unit an interval schedule counts its wait in.</summary>
public enum ScheduleUnit
{
    Seconds,
    Minutes,
    Hours,
}

/// <summary>Which change of a watched file or folder starts the macro.</summary>
public enum FileChangeKind
{
    /// <summary>Any of the other three.</summary>
    Any,

    /// <summary>A file or folder that appeared.</summary>
    Created,

    /// <summary>A file that was written to.</summary>
    Changed,

    /// <summary>A file or folder that went away, by being deleted or renamed.</summary>
    Deleted,
}

/// <summary>Whether a program starting or a program finishing starts the macro.</summary>
public enum ProcessChangeKind
{
    /// <summary>The program appears among the running ones.</summary>
    Started,

    /// <summary>The program is no longer among the running ones.</summary>
    Stopped,

    /// <summary>Either of the other two.</summary>
    Any,
}

/// <summary>Whether a window appearing or a window going away starts the macro.</summary>
public enum WindowChangeKind
{
    /// <summary>A window matching the description is there now and was not before.</summary>
    Appeared,

    /// <summary>No window matching the description is left.</summary>
    Disappeared,

    /// <summary>Either of the other two.</summary>
    Any,
}

/// <summary>How the watched colour is compared to the pixel under the trigger position.</summary>
public enum ColorMatchCondition
{
    /// <summary>Trigger when the pixel shows the chosen colour.</summary>
    ColorMatches,

    /// <summary>Trigger when the pixel does not show the chosen colour.</summary>
    ColorNotMatches,
}

/// <summary>What is stored for the pointer while a macro records.</summary>
public enum MousePositionMode
{
    /// <summary>Store the absolute pointer position.</summary>
    SaveCurrentPosition,

    /// <summary>Store the movement between pointer positions.</summary>
    SavePositionDifferences,
}
