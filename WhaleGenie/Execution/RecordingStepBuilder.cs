using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Core.Recording;
using WhaleGenie.Models;

namespace WhaleGenie.Execution;

/// <summary>
/// Turns the actions a recording produced into the steps the editor holds. Each action maps
/// onto the macro action that does the same thing, so a recorded macro reads like one that
/// was written by hand.
/// </summary>
public static class RecordingStepBuilder
{
    /// <summary>Builds the steps for a whole recording.</summary>
    public static IReadOnlyList<MacroStep> ToSteps(this IEnumerable<RecordedAction> actions)
    {
        var steps = new List<MacroStep>();
        foreach (var action in actions)
        {
            steps.Add(action.ToStep());
        }

        return steps;
    }

    /// <summary>Builds the step for one recorded action.</summary>
    public static MacroStep ToStep(this RecordedAction action) => action.Kind switch
    {
        RecordedActionKind.Delay =>
            Build("control.delay", ("ms", Number(action.DurationMs))),

        RecordedActionKind.KeyPress =>
            Build("input.keyPress", ("key", action.Key), ("holdMs", Number(action.DurationMs))),

        RecordedActionKind.KeyDown =>
            Build("input.keyDown", ("key", action.Key)),

        RecordedActionKind.KeyUp =>
            Build("input.keyUp", ("key", action.Key)),

        RecordedActionKind.Hotkey =>
            Build("input.hotkey", ("keys", action.Key), ("holdMs", Number(action.DurationMs))),

        RecordedActionKind.MouseClick =>
            Build(
                "input.mouseClick",
                ("button", action.Button),
                ("x", Number(action.X)),
                ("y", Number(action.Y)),
                ("clicks", Number(Math.Max(1, action.Amount))),
                ("intervalMs", Number(0))),

        RecordedActionKind.MouseDoubleClick =>
            Build(
                "input.mouseDoubleClick",
                ("button", action.Button),
                ("x", Number(action.X)),
                ("y", Number(action.Y))),

        RecordedActionKind.MouseDown =>
            Build(
                "input.mouseDown",
                ("button", action.Button),
                ("x", Number(action.X)),
                ("y", Number(action.Y))),

        RecordedActionKind.MouseUp =>
            Build(
                "input.mouseUp",
                ("button", action.Button),
                ("x", Number(action.X)),
                ("y", Number(action.Y))),

        RecordedActionKind.MouseMove =>
            Build(
                "input.mouseMove",
                ("x", Number(action.X)),
                ("y", Number(action.Y)),
                ("durationMs", Number(0))),

        RecordedActionKind.MouseMoveRelative =>
            Build(
                "input.mouseMoveRelative",
                ("dx", Number(action.X)),
                ("dy", Number(action.Y)),
                ("durationMs", Number(0))),

        RecordedActionKind.Scroll =>
            Build(
                "input.mouseScroll",
                ("direction", action.Direction),
                ("amount", Number(Math.Max(1, action.Amount))),
                ("x", Number(action.X)),
                ("y", Number(action.Y))),

        _ => throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unknown recorded action."),
    };

    /// <summary>
    /// Writes the parameters in the order the action expects them, taking each parameter's
    /// kind from the catalogue so the editor offers the right field for it.
    /// </summary>
    private static MacroStep Build(string type, params (string Name, string Value)[] values)
    {
        var definition = ActionCatalog.Find(type);
        var parameters = new List<StepParameter>(values.Length);

        foreach (var (name, value) in values)
        {
            var kind = definition?.Parameters
                .FirstOrDefault(parameter => parameter.Name == name)?.Kind
                ?? ActionParameterKind.Text;

            parameters.Add(new StepParameter
            {
                Name = name,
                Kind = kind,
                Value = value,
            });
        }

        return new MacroStep
        {
            Type = type,
            Parameters = parameters,
        };
    }

    /// <summary>Formats a number the way the steps are saved, whatever the machine's locale is.</summary>
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
