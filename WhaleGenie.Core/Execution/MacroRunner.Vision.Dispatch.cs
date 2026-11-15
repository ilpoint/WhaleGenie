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
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Which sight step goes where: the table the dispatch reads.
public sealed partial class MacroRunner
{
    /// <summary>
    /// The vision actions, each answered by the method written for it. A step of
    /// another family is not in this table.
    /// </summary>
    private Dictionary<string, ActionHandler> VisionActions
        => _visionActions ??= new(StringComparer.Ordinal)
        {
            ["vision.capture"] = async (step, depth, token) =>
            {
                await Gate(token);
                Watching(() => Capture(step, depth));
                return Signal.Normal;
            },

            ["vision.captureWindow"] = async (step, depth, token) =>
            {
                await Gate(token);
                Watching(() => CaptureWindow(step, depth));
                return Signal.Normal;
            },

            ["vision.matchColor"] = (step, depth, token) =>
            {
                Watching(() => MatchColor(step, depth));
                return Task.FromResult(Signal.Normal);
            },

            ["vision.getPixel"] = async (step, depth, token) =>
            {
                await Gate(token);
                Watching(() => GetPixel(step, depth));
                return Signal.Normal;
            },

            ["vision.waitColor"] = async (step, depth, token) =>
            {
                await Gate(token);
                await Watching(() => WaitColor(step, depth, token));
                return Signal.Normal;
            },

            ["vision.findColor"] = async (step, depth, token) =>
            {
                await Gate(token);
                Watching(() => FindColor(step, depth));
                return Signal.Normal;
            },

            ["vision.findColors"] = async (step, depth, token) =>
            {
                await Gate(token);
                Watching(() => FindColours(step, depth));
                return Signal.Normal;
            },

            ["vision.waitStable"] = async (step, depth, token) =>
            {
                await Gate(token);
                await WaitStable(step, depth, token);
                return Signal.Normal;
            },

            ["vision.findImage"] = async (step, depth, token) =>
            {
                await Gate(token);
                Watching(() => LookFor(step, depth));
                return Signal.Normal;
            },

            ["vision.waitImage"] = async (step, depth, token) =>
            {
                await Gate(token);
                await Watching(() => WaitForImage(step, depth, token));
                return Signal.Normal;
            },

            ["vision.clickColor"] = async (step, depth, token) =>
            {
                await Gate(token);
                await Watching(() => ClickColor(step, depth, token));
                return Signal.Normal;
            },

            ["vision.clickImage"] = async (step, depth, token) =>
            {
                await Gate(token);
                await Watching(() => ClickImage(step, depth, token));
                return Signal.Normal;
            },

            ["vision.detect"] = async (step, depth, token) =>
            {
                await Gate(token);
                Watching(() => LookForObjects(step, depth));
                return Signal.Normal;
            },

            ["vision.waitDetect"] = async (step, depth, token) =>
            {
                await Gate(token);
                await Watching(() => WaitForObjects(step, depth, token));
                return Signal.Normal;
            },

            ["vision.clickDetect"] = async (step, depth, token) =>
            {
                await Gate(token);
                await Watching(() => ClickObject(step, depth, token));
                return Signal.Normal;
            },
        };

    private Dictionary<string, ActionHandler>? _visionActions;

}
