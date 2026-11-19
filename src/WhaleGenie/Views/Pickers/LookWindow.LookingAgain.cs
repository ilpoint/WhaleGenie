using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// Looking again at the machine the window was opened on, and what the strip reports while it does.
public partial class LookWindow : AppWindow
{
    /// <summary>
    /// Takes a fresh look at the screen and fills this window in with it, in place rather than as
    /// another window: what is being tried over and over is one step's own reading.
    /// </summary>
    private async void OnLookAgain(object? sender, RoutedEventArgs e)
    {
        if (_looking || _step is not { } step || _devices is not { } devices)
        {
            return;
        }

        _looking = true;
        SetStatus(Strings.Get("Look.AgainBusy"));
        try
        {
            // Out of the way first: this window covers the very screen the step is looking at, and a
            // look taken with it standing finds the window rather than what is behind it. Same
            // entrance as the dialog's own run — looking and nothing else: no click, no key, no
            // variable — and the window comes back when the look is done.
            LookOutcome outcome;
            using (var aside = AppWindows.StepAside(this))
            {
                // Waited for, not assumed: hiding is one call, but the desktop still holds the frame
                // that had these windows in it until it has drawn again.
                await AppWindows.SettleAsync();
                outcome = await Task.Run(() => MacroRunner.LookOnce(step, devices));
            }

            if (outcome.Look is { } look)
            {
                Refill(look);
                SetStatus(string.Empty);
            }
            else
            {
                SetStatus(Strings.Format("Look.AgainFailed", Strings.Get(outcome.Key)));
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            SetStatus(Strings.Format("Look.AgainFailed", error.Message));
        }
        finally
        {
            _looking = false;
        }
    }
}
