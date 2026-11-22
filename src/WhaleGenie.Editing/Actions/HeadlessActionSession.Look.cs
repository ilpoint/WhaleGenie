using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Editing;

// Looking at the screen once for a picker that aims at what the step found. The picture is the
// whole thing the step reads, not only the searched rectangle, because an offset often lands just
// outside what was matched.
internal sealed partial class HeadlessActionSession
{
    public EditorLookRequest PrepareLook(IDeviceLayer devices, string name, string mode)
    {
        var field = Field(name);
        var step = new[] { BuildTrialStep() }.ToExecutable()[0];
        var looking = WhatThePickerLooksFor(step);
        var area = PickArea(devices);
        var outcome = MacroRunner.LookOnce(looking, devices);
        var look = outcome.Look
            ?? throw new InvalidOperationException(outcome.Detail.Length > 0
                ? Strings.Format(outcome.Key, outcome.Detail)
                : Strings.Get(outcome.Key));
        if (look.ChosenIndex <= 0 || look.ChosenIndex > look.Boxes.Count)
        {
            throw new InvalidOperationException(Strings.Get("Add.PickOffsetNeedsImage"));
        }

        var frame = _host.CaptureScreen(devices.Screen, area.X, area.Y, area.Width, area.Height);
        if (frame.IsEmpty)
        {
            throw new InvalidOperationException(Strings.Get("Add.PickOffsetNeedsImage"));
        }

        var chosen = look.Boxes[look.ChosenIndex - 1];
        return new EditorLookRequest(
            string.Equals(mode, "colours", StringComparison.Ordinal)
                ? EditorLookMode.Colours : EditorLookMode.Offset,
            look, frame, new ScreenPoint(area.X, area.Y), chosen.Match.Center,
            [.. _form.ColourPoints()], _form.CollectVariables());
    }

    public EditorActionFormSnapshot ApplyLook(EditorLookRequest request, EditorLookResult result)
    {
        if (!result.Chosen)
        {
            return Snapshot();
        }

        if (request.Mode == EditorLookMode.Offset)
        {
            _form.ApplyOffset(result.X, result.Y);
        }
        else
        {
            _form.SetColourPoints(result.Entries);
        }

        return Snapshot();
    }

    private (int X, int Y, int Width, int Height) PickArea(IDeviceLayer devices)
    {
        if (_form.NeedsWindowCoordinates && _form.CoordinateWindowBounds() is { } bounds)
        {
            return bounds;
        }

        var desk = devices.Screen.Desktop.Bounds;
        return (desk.Left, desk.Top, desk.Width, desk.Height);
    }

    private static ExecutableStep WhatThePickerLooksFor(ExecutableStep step)
        => step.Type == "vision.findColors" && step.Rows("points").Count == 0
            ? new ExecutableStep
            {
                Type = "vision.findColor",
                Id = step.Id,
                Meta = step.Meta,
                Parameters = [.. step.Parameters.Where(parameter => parameter.Name != "points")],
            }
            : step;
}
