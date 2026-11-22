using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

// Running the step as it is written, once, and handing back what it did and what it saw.
internal sealed partial class HeadlessActionSession
{
    public bool CannotBeUndone => ActionSemanticsCatalog.CannotBeUndone(
        new[] { BuildTrialStep() }.ToExecutable()[0]);

    public string IrreversibleQuestion => IrreversiblePrompt.Question(_form.SelectedKey);

    public async Task<EditorTrialResult> RunTrialAsync(IDeviceLayer devices,
        IMacroLibrary macros, VariableSession variables)
    {
        var step = BuildTrialStep();
        var looks = new TrialLooks();
        var run = await StepTrial.TryAsync(new[] { step }.ToExecutable()[0], devices,
            looks: looks,
            macros: macros,
            variables: _host.SeedVariables(devices, variables));
        var seen = looks.Seen;
        return new EditorTrialResult(
            run.Status is RunStatus.Completed,
            run.Status.ToString(),
            run.Key,
            run.Detail,
            seen is null ? null : DataUrl(seen.Frame),
            seen is null
                ? []
                : [.. seen.Boxes.Select(box => new EditorTrialBox(
                    box.Match.Location.X, box.Match.Location.Y,
                    box.Match.Size.Width, box.Match.Size.Height,
                    box.Match.Score, box.Role.ToString(), box.Label))],
            seen?.ChosenIndex ?? 0,
            seen?.Looking ?? string.Empty,
            seen?.Origin.X ?? 0,
            seen?.Origin.Y ?? 0,
            seen?.Frame.Width ?? 0,
            seen?.Frame.Height ?? 0);
    }

    private MacroStep BuildTrialStep()
    {
        var built = _form.BuildStep();
        if (_form.WindowAbove.Length == 0)
        {
            return built;
        }

        foreach (var field in built.Parameters.Where(field =>
                     string.Equals(field.Name, "window", StringComparison.Ordinal)
                     && WindowRule.TryParse(field.Value, out var rule) && rule.Follow))
        {
            field.Written = Formula.Text(_form.WindowAbove);
        }

        return built;
    }

    private static string DataUrl(ImageFrame frame)
        => "data:image/png;base64," + Convert.ToBase64String(PngWriter.Encode(frame));

    private sealed class TrialLooks : IRunLooks
    {
        public StepLook? Seen { get; private set; }

        public void Look(StepLook look) => Seen = look;

        public void Ran(StepRun run)
        {
            // The trial's report is the step's result, not a row moving in a run list.
        }

        public void Reading(WindowInfo? window)
        {
            // The picture is what is worth seeing; the window being read is already in it.
        }
    }
}
