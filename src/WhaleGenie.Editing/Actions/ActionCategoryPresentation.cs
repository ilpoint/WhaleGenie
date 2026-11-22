using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The grouping a person reads in the action library. The engine keeps OCR, vision, windows and
/// processes apart because different code runs them; the picker joins those pairs where a person
/// thinks of one task.
/// </summary>
public static class ActionCategoryPresentation
{
    private static readonly ActionCategory[] BasicOrder =
    [
        ActionCategory.Input,
        ActionCategory.Vision,
        ActionCategory.Uia,
        ActionCategory.Browser,
        ActionCategory.Script,
        ActionCategory.Process,
        ActionCategory.Data,
        ActionCategory.File,
        ActionCategory.Clipboard,
        ActionCategory.System,
    ];

    public static ActionCategory PickerCategory(ActionCategory category) => category switch
    {
        ActionCategory.Ocr => ActionCategory.Vision,
        ActionCategory.Window => ActionCategory.Process,
        _ => category,
    };

    /// <summary>
    /// The order the small groups of one basic category are listed in. The group each action
    /// belongs to is named by the display file; this table decides only which section comes first,
    /// the way the flow groups in the action catalogue have a rank. A group the table does not name
    /// is still listed — after the named ones — so an action can never disappear from the palette
    /// because somebody forgot a line here.
    /// </summary>
    private static readonly Dictionary<ActionCategory, string[]> BasicGroupOrder = new()
    {
        [ActionCategory.Input] = ["Keyboard", "Mouse", "Gamepad"],
        [ActionCategory.Vision] = ["Screen", "FindImage", "Colour", "Detect", "OcrText"],
        [ActionCategory.Uia] = ["Find", "Element", "Window"],
        [ActionCategory.Browser] = ["Navigate", "Element", "Page", "Wait"],
        [ActionCategory.Script] = ["Run", "Dependencies"],
        [ActionCategory.Process] = ["Program", "Window"],
        [ActionCategory.Data] = ["Variables", "Lists", "Encoding"],
        [ActionCategory.File] = ["Text", "Paths", "Archive", "Snapshots"],
        [ActionCategory.Clipboard] = ["Text", "Image", "Files", "Flow"],
        [ActionCategory.System] = ["Info", "Environment", "Desktop"],
    };

    /// <summary>The small groups of one basic category, in the order the palette shows them.</summary>
    public static IReadOnlyList<string> BasicGroups(ActionCategory category)
        => BasicGroupOrder.TryGetValue(category, out var groups) ? groups : [];

    /// <summary>
    /// The small group a control-flow action is listed under, the way the palette's flow dock
    /// groups it. A key the table never learned still gets "Other" rather than disappearing.
    /// </summary>
    public static string FlowGroup(string key) => key switch
    {
        "control.repeat" or "control.while" or "control.for"
            or "control.forEach" or "control.if" or "control.switch" or "control.try" => "Branch",
        "control.break" or "control.continue" or "control.return" or "control.rethrow"
            or "control.stop" or "control.fail" => "Jump",
        "control.delay" or "control.waitUntil"
            or "control.holdInput" or "control.holdBrowser"
            or "control.runMacro" => "Waiting",
        "control.ask" or "control.input" => "Data",
        "control.note" or "control.log" => "Notes",
        _ => "Other",
    };

    /// <summary>Where a flow group sits in the flow dock; unknown groups come last.</summary>
    public static int FlowGroupRank(string group) => group switch
    {
        "Branch" => 0,
        "Jump" => 1,
        "Waiting" => 2,
        "Data" => 3,
        "Notes" => 4,
        _ => 5,
    };

    public static IReadOnlyList<ActionCategoryChoice> BasicCategories() =>
    [
        new(null, Strings.Get("Add.AllActions"), Strings.Get("Add.AllActionsShort")),
        .. BasicOrder.Select(category =>
        {
            var label = Strings.Get($"Add.Category.{category}");
            return new ActionCategoryChoice(category, label,
                Strings.Get($"Add.Category.{category}.short", label));
        }),
    ];
}
