using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// The names the macro declares, and the settings that belong to the macro rather than a trigger.
public partial class MacroItem : ObservableObject, ILanguageAware
{
    /// <summary>What a recorded pointer position stores: where it is, or how it moved.</summary>
    public MousePositionMode PositionCapture { get; set; } = MousePositionMode.SaveCurrentPosition;

    public bool RecordMouseMovement { get; set; } = true;

    public bool RecordMouseButtons { get; set; } = true;

    public bool RecordKeyboardKeys { get; set; } = true;

    /// <summary>The buttons of the controller in the person's hands, read while a recording is going.</summary>
    public bool RecordGamepadButtons { get; set; } = true;

    public bool RecordDelays { get; set; } = true;

    /// <summary>Delays shorter than this are dropped from the recording.</summary>
    public int IgnoreDelaysBelowMs { get; set; } = 50;

    /// <summary>
    /// How much longer or shorter this macro's waits are made when it runs. 1 keeps every wait
    /// as it was written; 2 waits twice as long. It is applied while running, so the steps
    /// themselves stay exactly as the author left them.
    /// </summary>
    public double DelayScale { get; set; } = 1;

    /// <summary>
    /// Events per second this macro may send, or null when it follows the program setting. Zero is
    /// no limit, which is a different answer from "follow the program": a program change must not
    /// silently lift a limit its author set for this one macro.
    /// </summary>
    public int? InputRate { get; set; }

    /// <summary>
    /// The key that arms or disarms this macro while the program runs, or empty. It is not a
    /// trigger: pressing it sends nothing and runs no step, which is why it lives with the macro's
    /// own settings rather than among its trigger entries.
    /// </summary>
    public string ToggleKey { get; set; } = string.Empty;

    /// <summary>The recorded or hand-built steps, saved as the macro's JSON tree.</summary>
    public List<MacroStep> Steps { get; } = [];

    /// <summary>
    /// Callable macros owned by this macro. They travel inside the parent document, are not listed
    /// as project macros, and are reached by name from the parent or from one another.
    /// </summary>
    public List<MacroItem> SubMacros { get; } = [];

    /// <summary>The public macro copy this definition came from, when it came from one.</summary>
    public MacroOrigin? Origin { get; set; }

    /// <summary>
    /// The values this macro asks whoever calls it to fill in. A call is a form of exactly these
    /// fields, so what a macro expects is written down once, where it is called from.
    /// </summary>
    public List<MacroParameter> Parameters { get; } = [];

    /// <summary>
    /// The names this macro offers to hand back to whoever called it. A call may ask for any of
    /// them; a name that is not here is a mistake at the call site.
    /// </summary>
    public List<MacroResult> Results { get; } = [];

    /// <summary>
    /// The values this macro keeps for itself and starts every run with. A local variable used to
    /// exist only because a step wrote one; declaring it here lets a macro say where a count starts
    /// and be read back that way.
    /// </summary>
    public List<MacroLocalVariable> Locals { get; } = [];

    /// <summary>What this macro asks of a call to it, read by the editor, the check and the engine.</summary>
    public MacroCallable Callable => new(Name, Parameters, Results, Steps, Locals);

    /// <summary>Names of the variables this macro creates for itself.</summary>
    public IReadOnlyList<string> LocalVariableNames()
    {
        var names = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var step in Steps)
        {
            step.CollectVariables(names);
        }

        // The interface names are values of this macro too: the runtime seeds the parameters and
        // clears the declared results, so the variable page and the pickers should not act as if
        // they only appear once a step happens to write them.
        foreach (var parameter in Parameters)
        {
            var name = parameter.Name.Trim();
            if (VariableName.IsName(name))
            {
                names.Add(name);
            }
        }

        foreach (var result in Results)
        {
            var name = result.Name.Trim();
            if (VariableName.IsName(name))
            {
                names.Add(name);
            }
        }

        // A declared local is a value of this macro exactly like a parameter is: the runtime puts
        // it into the macro's own table before the first step, so the pickers should offer it.
        foreach (var local in Locals)
        {
            var name = local.Name.Trim();
            if (VariableName.IsName(name))
            {
                names.Add(name);
            }
        }

        return [.. names];
    }
}
