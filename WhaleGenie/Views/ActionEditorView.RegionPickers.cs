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

// The buttons beside a place: picking a region, a window bounds or a point, and adding a row.
public partial class ActionEditorView : UserControl
{
    /// <summary>Opens the region picker and writes the rectangle it returns into the action.</summary>
    private async void OnPickRegion(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        RegionPickerWindow.Region? bounds = null;
        if (viewModel.NeedsWindowCoordinates)
        {
            if (viewModel.CoordinateWindowBounds() is not { } target)
            {
                await ReportTestAsync(Strings.Get("Add.PickCoordinateNeedsWindow"));
                return;
            }

            bounds = new RegionPickerWindow.Region(target.X, target.Y, target.Width, target.Height);
        }

        if (viewModel.SelectedKey == "input.mouseDrag")
        {
            if (await RegionPickerWindow.PickDragAsync(HostWindow!, Devices.Screen, Devices.Windows,
                    bounds, Devices.Hook) is { } drag)
            {
                viewModel.ApplyDrag(drag.StartX, drag.StartY, drag.EndX, drag.EndY);
            }

            return;
        }

        var region = await RegionPickerWindow.PickAsync(HostWindow!, Devices.Screen, Devices.Windows,
            null, bounds, hook: Devices.Hook);
        if (region is { } picked)
        {
            viewModel.ApplyRegion(picked.X, picked.Y, picked.Width, picked.Height);
        }
    }

    /// <summary>
    /// Frames the target window as it stands now, then lets its edges be adjusted before writing
    /// the screen rectangle into the mover. Nothing is moved until the macro itself runs.
    /// </summary>
    private async void OnPickWindowBounds(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (viewModel.CurrentWindow() is not { } target)
        {
            await ReportTestAsync(Strings.Get("Add.PickWindowBoundsNeedsWindow"));
            return;
        }

        var initial = new RegionPickerWindow.Region(target.Location.X, target.Location.Y,
            target.Size.Width, target.Size.Height);
        if (await RegionPickerWindow.PickAsync(HostWindow!, Devices.Screen, Devices.Windows, initial,
                null, target.Handle, Devices.Hook)
            is { } picked)
        {
            viewModel.ApplyRegion(picked.X, picked.Y, picked.Width, picked.Height);
        }
    }

    /// <summary>
    /// Follows the pointer with a crosshair and UI Automation outline. F1 takes the exact pixel,
    /// and the action form stores it in whichever basis the step uses.
    /// </summary>
    private async void OnPickPoint(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (viewModel.NeedsWindowCoordinates && viewModel.CoordinateWindow() is null)
        {
            await ReportTestAsync(Strings.Get("Add.PickCoordinateNeedsWindow"));
            return;
        }

        if (await PointPickerWindow.PickAsync(HostWindow!, Devices.Screen, Devices.Windows, Devices.Ui,
            Devices.Hook, viewModel.CoordinateWindow())
            is { } point)
        {
            viewModel.ApplyCursorPosition(point.X, point.Y);
        }
    }

    /// <summary>Adds an empty place to look at to a step's list of them.</summary>
    private void OnAddRegionRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.AddRegion();
        }
    }

    /// <summary>Adds another colour to the pattern the step is matching.</summary>
    private void OnAddColourPoint(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.AddColourPoint();
        }
    }

    /// <summary>Adds another press to a key run.</summary>
    private void OnAddKeyRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.AddKeyRow();
        }
    }

    /// <summary>Adds an empty row to the pictures a search looks for.</summary>
    private void OnAddImageRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.AddPicture();
        }
    }

    /// <summary>Chooses a picture file for one row of a search's list of them.</summary>
    private async void OnBrowseImageRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ImageRowViewModel row })
        {
            return;
        }

        if (await PickPictureAsync() is { Length: > 0 } path)
        {
            row.Text = path;
        }
    }

    /// <summary>
    /// Drags a rectangle on the screen and keeps it as a picture inside the macro project, which is
    /// how a reference picture is normally made.
    /// </summary>
    private async void OnCaptureImageRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ImageRowViewModel row }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        try
        {
            RegionPickerWindow.Region? bounds = null;
            if (viewModel.NeedsWindowCoordinates)
            {
                if (viewModel.CoordinateWindowBounds() is not { } target)
                {
                    await ReportTestAsync(Strings.Get("Add.PickCoordinateNeedsWindow"));
                    return;
                }

                bounds = new RegionPickerWindow.Region(target.X, target.Y,
                    target.Width, target.Height);
            }

            if (await RegionPickerWindow.CaptureAsync(HostWindow!, Devices.Screen, Devices.Windows,
                    viewModel.AssetFolder, bounds, Devices.Hook)
                is { Length: > 0 } picture)
            {
                row.Text = picture;
            }
        }
        catch (Exception)
        {
            await ConfirmDialog.ShowAsync(HostWindow!, Strings.Get("Add.CaptureImage"),
                Strings.Get("Add.ImageFailed"), Strings.Get("Common.Ok"), showCancel: false);
        }
    }

    /// <summary>
    /// Points the keyboard drawn on screen at one press of a key run. The keys already there stay,
    /// because a combination of several keys is built a key at a time.
    /// </summary>
    private void OnPickKeyRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: KeyRowViewModel row })
        {
            OpenKeyPad(Strings.Format("Add.KeyRunPress", row.Number), row.AddKey);
        }
    }

    /// <summary>
    /// Starts or stops recording keys into one press of a run. A run takes one press after another,
    /// so the capture stays up until it is clicked off — which is the only way out that is not a
    /// key, because every key pressed belongs to the run.
    /// </summary>
    private void OnCaptureKeyRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: KeyRowViewModel row })
        {
            return;
        }

        var wasRecording = ReferenceEquals(_capturingRow, row);
        EndKeyCapture();
        if (wasRecording)
        {
            return;
        }

        _capturingRow = row;
        row.StartCapture();
    }

    /// <summary>
    /// Points the keyboard at the field whose button was clicked: one key is set outright, a
    /// combination is joined to what is already there.
    /// </summary>
    private void OpenKeyPadFor(StepParameterViewModel parameter)
        => OpenKeyPad(parameter.Definition.LocalLabel, parameter.WriteKey);

    /// <summary>
    /// Shows the keyboard drawn on screen, or points the one already up at something else. A key is
    /// a name the macro has to spell exactly, and a combination is built a key at a time, so the
    /// window takes one cap after another instead of being opened again for each of them.
    /// </summary>
    private void OpenKeyPad(string filling, Action<string> write)
    {
        _keyTarget = write;

        if (_keyPad is null)
        {
            _keyPad = new VirtualKeyboardWindow();
            _keyPad.KeyChosen += OnKeyChosen;
            _keyPad.Closed += (_, _) => ForgetKeyPad();
        }

        _keyPad.Filling = filling;
        // Put away a moment ago and asked for again: it is brought back rather than opened twice.
        if (_keyPad.WindowState == WindowState.Minimized)
        {
            _keyPad.WindowState = WindowState.Normal;
            _keyPad.Activate();
        }
        else if (_keyPad.IsVisible)
        {
            _keyPad.Activate();
        }
        else
        {
            _keyPad.ShowOver(HostWindow!);
        }
    }

    /// <summary>A cap was clicked: the key goes into whatever the keyboard was pointed at.</summary>
    private void OnKeyChosen(string key) => _keyTarget?.Invoke(key);

    /// <summary>
    /// The keyboard is gone. What it was writing into goes with it, so a later click cannot land in
    /// a field the user has since left.
    /// </summary>
    private void ForgetKeyPad()
    {
        _keyPad = null;
        _keyTarget = null;
    }

    /// <summary>
    /// Drags a rectangle out on the screen and writes it into the row that asked for it, which is
    /// how a region is given without the four numbers being read off a ruler.
    /// </summary>
    private async void OnPickRegionRow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: RegionRowViewModel row }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        RegionPickerWindow.Region? bounds = null;
        if (viewModel.NeedsWindowCoordinates)
        {
            if (viewModel.CoordinateWindowBounds() is not { } target)
            {
                await ReportTestAsync(Strings.Get("Add.PickCoordinateNeedsWindow"));
                return;
            }

            bounds = new RegionPickerWindow.Region(target.X, target.Y, target.Width, target.Height);
        }

        if (await RegionPickerWindow.PickAsync(HostWindow!, Devices.Screen, Devices.Windows,
                null, bounds, hook: Devices.Hook) is { } picked)
        {
            row.Put(picked.X, picked.Y, picked.Width, picked.Height);
        }
    }
}
