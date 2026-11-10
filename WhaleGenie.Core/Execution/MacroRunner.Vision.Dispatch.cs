using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Which sight step goes where: the table the dispatch reads.
public sealed partial class MacroRunner
{
    /// <summary>
    /// The vision actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchVision(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "vision.capture":
                await Gate(token);
                Watching(() => Capture(step, depth));
                return Signal.Normal;
            case "vision.captureWindow":
                await Gate(token);
                Watching(() => CaptureWindow(step, depth));
                return Signal.Normal;
            case "vision.matchColor":
                Watching(() => MatchColor(step, depth));
                return Signal.Normal;
            case "vision.getPixel":
                await Gate(token);
                Watching(() => GetPixel(step, depth));
                return Signal.Normal;
            case "vision.waitColor":
                await Gate(token);
                await Watching(() => WaitColor(step, depth, token));
                return Signal.Normal;
            case "vision.findColor":
                await Gate(token);
                Watching(() => FindColor(step, depth));
                return Signal.Normal;
            case "vision.findColors":
                await Gate(token);
                Watching(() => FindColours(step, depth));
                return Signal.Normal;
            case "vision.waitStable":
                await Gate(token);
                await WaitStable(step, depth, token);
                return Signal.Normal;
            case "vision.findImage":
                await Gate(token);
                Watching(() => LookFor(step, depth));
                return Signal.Normal;
            case "vision.waitImage":
                await Gate(token);
                await Watching(() => WaitForImage(step, depth, token));
                return Signal.Normal;
            case "vision.clickColor":
                await Gate(token);
                await Watching(() => ClickColor(step, depth, token));
                return Signal.Normal;
            case "vision.clickImage":
                await Gate(token);
                await Watching(() => ClickImage(step, depth, token));
                return Signal.Normal;
            case "vision.detect":
                await Gate(token);
                Watching(() => LookForObjects(step, depth));
                return Signal.Normal;
            case "vision.waitDetect":
                await Gate(token);
                await Watching(() => WaitForObjects(step, depth, token));
                return Signal.Normal;
            case "vision.clickDetect":
                await Gate(token);
                await Watching(() => ClickObject(step, depth, token));
                return Signal.Normal;

            // -------------------------------------------------------------------- ocr
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }
}
