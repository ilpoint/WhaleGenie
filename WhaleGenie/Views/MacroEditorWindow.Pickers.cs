using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

public partial class MacroEditorWindow : AppWindow
{
    // The colour and window pickers the trigger pages use, and where the pointer was when the
    // editor asked. They are kept apart because they step the whole program aside to look outside.
    private async void OnPickColorClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MacroEditorViewModel viewModel)
        {
            return;
        }

        var pick = await ColorPickerWindow.PickAsync(this, Layer.Screen, Layer.Input, Layer.Hook);
        Activate();

        if (pick is not { } picked)
        {
            return;
        }

        // The place comes back with the colour rather than being read off the pointer afterwards:
        // what the magnifier was aimed at is where the colour is, and the pointer may have moved.
        viewModel.HexColor = picked.Colour.ToHex();
        viewModel.ColorPositionX = picked.At.X.ToString(CultureInfo.InvariantCulture);
        viewModel.ColorPositionY = picked.At.Y.ToString(CultureInfo.InvariantCulture);
        ShowPickStatus(Strings.Format("Editor.PickedColor", picked.Colour.ToHex()));
    }

    /// <summary>Captures the window a window trigger should watch.</summary>
    private async void OnPickTriggerWindow(object? sender, RoutedEventArgs e)
    {
        if (await WindowRuleWindow.PickAsync(this, Layer.Windows, Layer.Hook, _viewModel.WindowTarget) is { } rule)
        {
            _viewModel.UseWindowTarget(rule);
        }
    }

    /// <summary>Alt + X: the pointer's current screen position becomes the watched position.</summary>
    private void CaptureCursorPosition(MacroEditorViewModel viewModel)
    {
        var position = Layer.Screen.Cursor;
        viewModel.ColorPositionX = position.X.ToString(CultureInfo.InvariantCulture);
        viewModel.ColorPositionY = position.Y.ToString(CultureInfo.InvariantCulture);
        ShowPickStatus(Strings.Format("Editor.PickedPosition", position.X, position.Y));
    }

    /// <summary>
    /// Alt + C: the colour of the pixel under the pointer becomes the watched colour, and the
    /// position it was read from is kept alongside it, so the trigger can be checked straight away.
    /// </summary>
    private void CaptureCursorColour(MacroEditorViewModel viewModel)
    {
        var position = Layer.Screen.Cursor;
        PixelColor colour;
        try
        {
            colour = Layer.Screen.PixelAt(position.X, position.Y);
        }
        catch (Exception)
        {
            // The pointer can sit on a second monitor the primary screen does not cover.
            ShowPickStatus(Strings.Get("Editor.PickFailed"));
            return;
        }

        viewModel.HexColor = colour.ToHex();
        viewModel.ColorPositionX = position.X.ToString(CultureInfo.InvariantCulture);
        viewModel.ColorPositionY = position.Y.ToString(CultureInfo.InvariantCulture);
        ShowPickStatus(Strings.Format("Editor.PickedColor", colour.ToHex()));
    }

    /// <summary>Shows what the last pick caught, clearing itself so the panel stays tidy.</summary>
    private void ShowPickStatus(string message)
    {
        var status = this.FindControl<TextBlock>("PickStatus");
        if (status is null)
        {
            return;
        }

        status.Text = message;

        _pickTimer?.Stop();
        _pickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _pickTimer.Tick += (_, _) =>
        {
            _pickTimer?.Stop();
            _pickTimer = null;
            status.Text = string.Empty;
        };
        _pickTimer.Start();
    }
}
