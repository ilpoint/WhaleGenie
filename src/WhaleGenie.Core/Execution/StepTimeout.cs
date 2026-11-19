using System;
using System.Globalization;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The failure a step reports when its own clock runs out, and the keys that already say what was
/// being waited for. A step that was waiting for something knows what it was waiting for, so it says
/// that rather than only that it was slow: "the picture never turned up" points at the picture,
/// where "too slow" points at the number. Every other step has nothing more to say than that it ran
/// out of time.
/// </summary>
/// <remarks>
/// It reads the step's own fields, so it needs the same reader the step gets and the window rule the
/// step named. Nothing here records anything: the caller decides what to do with the failure.
/// </remarks>
internal sealed class StepTimeout(IRunContext run, WindowFacts windows)
{
    /// <summary>
    /// True for the failure keys a wait or a device raises when its own clock runs out. Those say
    /// what was being waited for, so running a little past the step's clock must not replace them
    /// with the bare "too slow": the caller needs the picture, element or program that never came.
    /// </summary>
    public static bool Says(string key)
        => key is RunKeys.Timeout
            or RunKeys.WaitColorTimeout or RunKeys.WaitTimeout
            or RunKeys.ImageNotFound or RunKeys.DetectNotFound or RunKeys.ScreenNotStable
            or RunKeys.TextNotFound or RunKeys.ElementNotFound or RunKeys.ClipboardTimeout
            or RunKeys.ColourNotFound
            or RunKeys.ProcessTimeout or RunKeys.ProgramNotFound or RunKeys.WindowTimeout
            or RunKeys.CommandTimeout or RunKeys.BrowserNotFound or RunKeys.BrowserNoNewTab;

    /// <summary>The failure this step reports when its own clock runs out.</summary>
    public StepFailure For(ExecutableStep step)
    {
        var patience = step.Meta.TimeoutMs.ToString(CultureInfo.InvariantCulture);
        try
        {
            return step.Type switch
            {
                "vision.waitColor" => new StepFailure(RunKeys.WaitColorTimeout,
                    PixelColor.Parse(run.Args(step).ValueOf("color").AsText()).ToHex()),
                "vision.clickColor" => new StepFailure(RunKeys.ColourNotFound,
                    PixelColor.Parse(run.Args(step).ValueOf("color").AsText()).ToHex()),
                "vision.waitImage" or "vision.clickImage" => new StepFailure(RunKeys.ImageNotFound,
                    StepRows.Written(step.Rows("images"))),
                "vision.waitDetect" or "vision.clickDetect" => new StepFailure(RunKeys.DetectNotFound,
                    SearchHits.Looking(run, step)),
                "vision.waitStable" => new StepFailure(RunKeys.ScreenNotStable, patience),
                "ocr.clickText" => new StepFailure(RunKeys.TextNotFound,
                    run.Args(step).ValueOf("queryText").AsText()),
                "control.waitUntil" => new StepFailure(RunKeys.WaitTimeout, WaitingFor(step, patience)),
                "clipboard.waitChange" or "clipboard.copy" =>
                    new StepFailure(RunKeys.ClipboardTimeout),
                "process.waitFor" => new StepFailure(RunKeys.ProgramNotFound, windows.Text(step)),
                "process.waitExit" => new StepFailure(RunKeys.ProcessTimeout, windows.Text(step)),
                "window.waitFor" => new StepFailure(RunKeys.WindowTimeout, windows.Text(step)),
                "uia.waitElement" or "uia.click" or "uia.invoke" =>
                    new StepFailure(RunKeys.ElementNotFound, run.Args(step).Text("uiSelector")),
                "command.run" => new StepFailure(RunKeys.CommandTimeout,
                    run.Args(step).ValueOf("commandFile").AsText()),
                _ => new StepFailure(RunKeys.Timeout, patience),
            };
        }
        catch (ExpressionException)
        {
            // A field that cannot be read at all is the business of another failure; the only
            // thing this one has to say is that the step ran out of time.
            return new StepFailure(RunKeys.Timeout, patience);
        }
    }

    /// <summary>
    /// What a wait-until step was waiting for, so its failure names the question rather than only
    /// that it was slow. A step with no condition left in it — which the editor does not write —
    /// has nothing to name, and is left with the length of time.
    /// </summary>
    private static string WaitingFor(ExecutableStep step, string patience)
        => step.Text("condition").Trim() is { Length: > 0 } asked ? asked : patience;
}
