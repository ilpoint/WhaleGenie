using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The two looks a trigger can take at the machine without running anything: a colour under the
/// pointer and the window a rule should watch. Both step the whole program aside while they look,
/// and both report what was caught on one line under the trigger, which is why they are kept
/// together rather than beside the fields they fill in.
/// </summary>
internal sealed class EditorPickers(AppWindow host, Func<IDeviceLayer> devices)
{
    /// <summary>Clears the "what the last pick caught" line a moment after it is shown.</summary>
    private DispatcherTimer? _timer;

    /// <summary>Opens the magnifier, which follows the pointer and reads the pixel under it.</summary>
    public async Task PickColourAsync(MacroEditorViewModel viewModel)
    {
        var layer = devices();
        var pick = await ColorPickerWindow.PickAsync(host, layer.Screen, layer.Input, layer.Hook);
        host.Activate();

        if (pick is not { } picked)
        {
            return;
        }

        // The place comes back with the colour rather than being read off the pointer afterwards:
        // what the magnifier was aimed at is where the colour is, and the pointer may have moved.
        viewModel.HexColor = picked.Colour.ToHex();
        viewModel.ColorPositionX = picked.At.X.ToString(CultureInfo.InvariantCulture);
        viewModel.ColorPositionY = picked.At.Y.ToString(CultureInfo.InvariantCulture);
        Announce(Strings.Format("Editor.PickedColor", picked.Colour.ToHex()));
    }

    /// <summary>Captures the window a window trigger should watch.</summary>
    public async Task PickWindowAsync(MacroEditorViewModel viewModel)
    {
        var layer = devices();
        if (await WindowRuleWindow.PickAsync(host, layer.Windows, layer.Hook,
                viewModel.WindowTarget) is { } rule)
        {
            viewModel.UseWindowTarget(rule);
        }
    }

    /// <summary>Alt + X: the pointer's current screen position becomes the watched position.</summary>
    public void TakeCursorPosition(MacroEditorViewModel viewModel)
    {
        var position = devices().Screen.Cursor;
        viewModel.ColorPositionX = position.X.ToString(CultureInfo.InvariantCulture);
        viewModel.ColorPositionY = position.Y.ToString(CultureInfo.InvariantCulture);
        Announce(Strings.Format("Editor.PickedPosition", position.X, position.Y));
    }

    /// <summary>
    /// Alt + C: the colour of the pixel under the pointer becomes the watched colour, and the
    /// position it was read from is kept alongside it, so the trigger can be checked straight away.
    /// </summary>
    public void TakeCursorColour(MacroEditorViewModel viewModel)
    {
        var position = devices().Screen.Cursor;
        PixelColor colour;
        try
        {
            colour = devices().Screen.PixelAt(position.X, position.Y);
        }
        catch (Exception)
        {
            // The pointer can sit on a second monitor the primary screen does not cover.
            Announce(Strings.Get("Editor.PickFailed"));
            return;
        }

        viewModel.HexColor = colour.ToHex();
        viewModel.ColorPositionX = position.X.ToString(CultureInfo.InvariantCulture);
        viewModel.ColorPositionY = position.Y.ToString(CultureInfo.InvariantCulture);
        Announce(Strings.Format("Editor.PickedColor", colour.ToHex()));
    }

    /// <summary>Shows what the last pick caught, clearing itself so the panel stays tidy.</summary>
    private void Announce(string message)
    {
        var status = host.FindControl<TextBlock>("PickStatus");
        if (status is null)
        {
            return;
        }

        status.Text = message;

        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _timer.Tick += (_, _) =>
        {
            _timer?.Stop();
            _timer = null;
            status.Text = string.Empty;
        };
        _timer.Start();
    }
}
