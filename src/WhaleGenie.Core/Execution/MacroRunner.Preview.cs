using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

// What the editor's trial look shows for one step, and the table that says which look each action
// gets. It is a table rather than a second switch so the catalogue and the preview cannot drift
// apart without a check failing: every vision and OCR action has an entry here.
public sealed partial class MacroRunner
{
    /// <summary>
    /// The look a trial makes for each action: what the step would see right now, without waiting,
    /// clicking or writing a result. A search looks once, a wait does not wait, and the mouse is
    /// left alone. The actions that share a look share one entry, because what is previewed is the
    /// looking, not the verb that follows it.
    /// </summary>
    private static readonly Dictionary<string, Action<MacroRunner, ExecutableStep>> Previews =
        new(StringComparer.Ordinal)
        {
            ["vision.findImage"] = (run, step) => ImageActions.Preview(run, step),
            ["vision.waitImage"] = (run, step) => ImageActions.Preview(run, step),
            ["vision.clickImage"] = (run, step) => ImageActions.Preview(run, step),

            ["vision.detect"] = (run, step) => run._sight.Watching(() => DetectionActions.Sightings(run, step, run._run.RunToken)),
            ["vision.waitDetect"] = (run, step) => run._sight.Watching(() => DetectionActions.Sightings(run, step, run._run.RunToken)),
            ["vision.clickDetect"] = (run, step) => run._sight.Watching(() => DetectionActions.Sightings(run, step, run._run.RunToken)),

            ["vision.findColor"] = (run, step) => ColourActions.Preview(run, step),
            ["vision.waitColor"] = (run, step) => ColourActions.Preview(run, step),
            ["vision.clickColor"] = (run, step) => ColourActions.Preview(run, step),
            ["vision.findColors"] = (run, step) => ColourActions.Preview(run, step),
            ["vision.matchColor"] = (run, step) => ColourActions.Preview(run, step),
            ["vision.getPixel"] = (run, step) => ColourActions.Preview(run, step),

            // Whether something has stopped moving is only answered by time passing, so one look
            // shows what is on the screen and leaves the waiting to a run.
            ["vision.waitStable"] = (run, step) => CaptureActions.Preview(run, step),
            ["vision.capture"] = (run, step) => CaptureActions.Preview(run, step),
            ["vision.captureWindow"] = (run, step) => CaptureActions.Preview(run, step),

            ["ocr.recognize"] = (run, step) => run._sight.Watching(() => run._reads.WasRead(step, run._reads.ReadBox(step))),
            ["ocr.findText"] = (run, step) => run._sight.Watching(() => run._reads.SawSought(step)),
            ["ocr.clickText"] = (run, step) => run._sight.Watching(() => run._reads.SawSought(step)),
        };

    /// <summary>
    /// Every action a trial look can show. A check holds this against the catalogue, so an action
    /// that looks at the screen but has no entry here is a failing test rather than a trial that
    /// silently shows nothing.
    /// </summary>
    internal static IReadOnlyCollection<string> PreviewedActions => Previews.Keys;

    /// <summary>
    /// One look at the screen for a step, without the step: a search looks once rather than
    /// waiting, a wait does not wait, and the mouse is left alone.
    /// </summary>
    private void SeeOnce(ExecutableStep step)
    {
        if (Previews.TryGetValue(step.Type, out var look))
        {
            look(this, step);
        }
    }
}
