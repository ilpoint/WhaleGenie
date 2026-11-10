using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.UIA3;


namespace WhaleGenie.Core.Devices.Platform;

// Making the device: the automation it keeps, whether an element is there, and pressing one.
public sealed partial class FlaUiDevice
{
    public FlaUiDevice(IInputDevice? input = null)
    {
        // A device made without one of its own borrows the mouse that is already on this machine;
        // the pointer it reads is the screen's, the same answer every other device gets.
        _input = new Lazy<IInputDevice>(() =>
            input ?? new SharpHookInputDevice(new WindowsScreenDevice()));
    }

    public bool Exists(UiQuery query, int timeoutMs)
    {
        Require();
        if (query.IsEmpty)
        {
            throw new DeviceActionException(RunKeys.EmptySelector);
        }

        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (Find(query) is not null)
            {
                return true;
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= Math.Max(0, timeoutMs))
            {
                return false;
            }

            Thread.Sleep(100);
        }
    }

    public bool Click(UiQuery query, string button)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException(RunKeys.ElementNotFound, Describe(query));

        try
        {
            switch (button.Trim().ToLowerInvariant())
            {
                case "right":
                    element.RightClick(true);
                    break;
                case "middle":
                    ClickAt(element, "middle", query);
                    break;
                default:
                    // The invoke pattern works even when the window is not in front, so it
                    // is worth trying before falling back to a real mouse click.
                    if (element.Patterns.Invoke.IsSupported)
                    {
                        element.Patterns.Invoke.Pattern.Invoke();
                    }
                    else
                    {
                        element.Click(true);
                    }

                    break;
            }

            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return false;
        }
    }

    public bool FocusWindow(UiQuery query)
    {
        Require();
        if (string.IsNullOrWhiteSpace(query.WindowTitle))
        {
            throw new DeviceActionException(RunKeys.MissingWindow, query.WindowTitle ?? string.Empty);
        }

        var window = Root(query);
        if (window is null)
        {
            return false;
        }

        try
        {
            window.SetForeground();
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return false;
        }
    }
}
