using System.Collections.Generic;

namespace Viktor.Models;

public class MacroItem
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Short description of what starts the macro, shown in the macro list.</summary>
    public string Trigger { get; set; } = string.Empty;

    /// <summary>Short description of how the macro loops, shown in the macro list.</summary>
    public string Action { get; set; } = string.Empty;

    public MacroTrigger TriggerMode { get; set; } = MacroTrigger.KeystrokesButtonInputs;

    public MacroLoop LoopMode { get; set; } = MacroLoop.Toggle;

    /// <summary>Key bound to the macro, or empty when no key has been selected.</summary>
    public string BindKey { get; set; } = string.Empty;

    public MousePositionMode PositionCapture { get; set; } = MousePositionMode.SaveCurrentPosition;

    /// <summary>How the watched colour is compared to the trigger position.</summary>
    public ColorMatchCondition ColorMatch { get; set; } = ColorMatchCondition.ColorMatches;

    /// <summary>Horizontal screen position watched for the trigger colour.</summary>
    public int ColorPositionX { get; set; }

    /// <summary>Vertical screen position watched for the trigger colour.</summary>
    public int ColorPositionY { get; set; }

    /// <summary>When set, the watched position follows the cursor instead of the stored coordinates.</summary>
    public bool UseCursorPosition { get; set; }

    /// <summary>When set, the macro runs only once instead of repeating.</summary>
    public bool TriggerOnce { get; set; }

    /// <summary>Colour watched for the trigger, as a hex string (for example <c>#000000</c>).</summary>
    public string HexColor { get; set; } = "#000000";

    /// <summary>Allowed colour difference (percent) before the watched pixel counts as a match.</summary>
    public int ColorTolerance { get; set; } = 5;

    public bool RecordMouseMovement { get; set; } = true;

    public bool RecordMouseButtons { get; set; } = true;

    public bool RecordKeyboardKeys { get; set; } = true;

    public bool RecordDelays { get; set; } = true;

    /// <summary>Delays shorter than this are dropped from the recording.</summary>
    public int IgnoreDelaysBelowMs { get; set; } = 50;

    public List<MacroAction> Actions { get; } = [];
}
