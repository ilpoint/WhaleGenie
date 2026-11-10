using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// Picking an offset or a set of colour points, and the picture the picker looks at.
public partial class ActionEditorView : UserControl
{
    /// <summary>
    /// Picks where this step's click should land. The picture it is picked on is the one the step
    /// itself just saw rather than the reference picture: clicking the middle of a found picture is
    /// wrong whenever the thing to hit sits beside it, and "beside it" is only visible on a picture
    /// that has the whole screen on it. Working the offset out in the head is the kind of
    /// arithmetic a person gets wrong quietly.
    /// </summary>
    private async void OnPickOffset(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel
            || await LookForPickAsync(viewModel) is not { } pick)
        {
            return;
        }

        var offset = await LookPickWindow.PickOffsetAsync(HostWindow!, pick.Look, pick.Area,
            pick.Origin, pick.Centre, viewModel.CollectVariables());
        if (offset is { } point)
        {
            viewModel.ApplyOffset(point.X, point.Y);
        }
    }

    /// <summary>
    /// Picks the colours a pattern needs around its base colour, and the offsets they sit at. One
    /// recognition, then as many clicks as the pattern has colours: each click is one line of the
    /// field, worked out from the picture instead of by eye.
    /// </summary>
    internal async void OnPickColourPoints(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel
            || await LookForPickAsync(viewModel) is not { } pick)
        {
            return;
        }

        // The picker opens with the pattern the step already holds, and what it hands back is the
        // whole of it: the lines it was given, changed as they were changed there.
        var entries = await LookPickWindow.PickColoursAsync(HostWindow!, pick.Look, pick.Area,
            pick.Origin, pick.Centre, viewModel.CollectVariables(), viewModel.ColourPoints());
        if (entries is not null)
        {
            viewModel.SetColourPoints(entries);
        }
    }

    /// <summary>The picture a picker aims at, and the middle of what the step found on it.</summary>
    /// <summary>
    /// The picture a picker aims at, where it starts on the screen, and the middle of what the step
    /// found on it.
    /// </summary>
    private sealed record LookForPick(StepLook Look, ImageFrame Area, ScreenPoint Origin,
        ScreenPoint Centre);

    /// <summary>
    /// Looks at the screen once with the step as it is written, for a picker that aims at what the
    /// step found. Null — with the reason said under the form — when it found nothing: a picker with
    /// no hit under it has nothing to count an offset from.
    /// </summary>
    /// <remarks>
    /// The picture handed on is the whole of what the step reads — the screen, or the window it is
    /// anchored to — and not only the part it searched. An offset is measured from the match, and a
    /// click that lands beside the thing found is the ordinary case: a picker that shows only the
    /// searched rectangle cannot reach it, and the number then has to be worked out by hand.
    /// </remarks>
    private async Task<LookForPick?> LookForPickAsync(AddActionViewModel viewModel)
    {
        var step = new[] { viewModel.BuildStep() }.ToExecutable()[0];

        // Read before the windows go away: a window-anchored step measures from a window of another
        // program, and finding it is not something to do with this one hidden.
        var area = PickArea(viewModel);
        var looking = WhatThePickerLooksFor(step);

        // Off the thread that draws the window, and with this program's windows out of the picture,
        // for the same reasons the trial run does it. The picture is taken in the same breath as the
        // look, so the marks sit on the screen they were found on.
        var (outcome, picture) = await AwayAsync(() =>
            (MacroRunner.LookOnce(looking, Devices, new TrialFrames(this)),
                ScreenReady.Capture(Devices.Screen, area.X, area.Y, area.Width, area.Height)));
        _trialFrames.Clear();

        if (outcome.Look is null)
        {
            await ReportTestAsync(TrialFailure(
                new RunResult(RunStatus.Failed, outcome.Key, outcome.Detail, 0)));
            return null;
        }

        var look = outcome.Look;
        if (picture.IsEmpty
            || look.ChosenIndex <= 0
            || look.ChosenIndex > look.Boxes.Count)
        {
            await ReportTestAsync(Strings.Get("Add.PickOffsetNeedsImage"));
            return null;
        }

        return new LookForPick(look, picture, new ScreenPoint(area.X, area.Y),
            look.Boxes[look.ChosenIndex - 1].Match.Center);
    }

    /// <summary>
    /// The step as the picker should look at the screen. A multi-point colour search with nothing
    /// beside its base colour yet cannot run: it would be looking for the base colour alone, which
    /// is the plain colour search. That is exactly the state the picker is opened from, because
    /// picking the surroundings is how the pattern gets written, so for this one look it asks the
    /// question the picker needs answered: where is the base colour.
    /// </summary>
    internal static ExecutableStep WhatThePickerLooksFor(ExecutableStep step)
        => step.Type == "vision.findColors" && step.Rows("points").Count == 0
            ? new ExecutableStep
            {
                Type = "vision.findColor",
                Id = step.Id,
                Meta = step.Meta,
                Parameters = [.. step.Parameters.Where(parameter => parameter.Name != "points")],
            }
            : step;

    /// <summary>
    /// What the step reads as a whole: the client area of the window it is anchored to, or the whole
    /// desktop. It is the same rule the engine uses to find somewhere to look when a step names no
    /// region, said here so the picker offers the same area.
    /// </summary>
    private (int X, int Y, int Width, int Height) PickArea(AddActionViewModel viewModel)
    {
        if (viewModel.NeedsWindowCoordinates && viewModel.CoordinateWindowBounds() is { } bounds)
        {
            return bounds;
        }

        var desk = Devices.Screen.Desktop.Bounds;
        return (desk.Left, desk.Top, desk.Width, desk.Height);
    }
}
