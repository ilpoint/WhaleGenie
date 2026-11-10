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

// Describing what was found, the window above it, and letting the automation go.
public sealed partial class FlaUiDevice
{
    /// <summary>
    /// What UI Automation says about an element. An element nobody can name is one a macro could
    /// never look up again, so it answers with null the way the picker wants, rather than with a
    /// description nothing can match.
    /// </summary>
    private static UiElementInfo? Info(AutomationElement element)
    {
        var controlType = Read(() => element.ControlType.ToString());
        var name = Read(() => element.Name);
        var automationId = Read(() => element.AutomationId);

        if (controlType.Length == 0 && name.Length == 0 && automationId.Length == 0)
        {
            return null;
        }

        // UI Automation reports the rectangle in screen pixels, which is the unit a selector
        // taken here has to come back in.
        var bounds = Bounds(element);
        return new UiElementInfo(
            name,
            automationId,
            controlType,
            Read(() => element.ClassName),
            new ScreenPoint(bounds.X, bounds.Y),
            new ScreenSize(bounds.Width, bounds.Height),
            WindowTitleOf(element));
    }

    /// <summary>
    /// Where an element sits. A provider that refuses to say is not worth losing the element over,
    /// so an empty rectangle comes back instead.
    /// </summary>
    private static System.Drawing.Rectangle Bounds(AutomationElement element)
    {
        try
        {
            return element.BoundingRectangle;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return System.Drawing.Rectangle.Empty;
        }
    }

    /// <summary>
    /// The title of the window an element sits in, which is the filter a macro writes next to its
    /// selector. Walking up stops at the first window, so a dialog is named rather than the
    /// program behind it.
    /// </summary>
    private static string WindowTitleOf(AutomationElement element)
    {
        for (var hop = 0; hop < WindowHops; hop++)
        {
            if (Read(() => element.ControlType.ToString()) == nameof(ControlType.Window))
            {
                return Read(() => element.Name);
            }

            if (ParentOf(element) is not { } parent)
            {
                break;
            }

            element = parent;
        }

        return string.Empty;
    }

    private static AutomationElement? ParentOf(AutomationElement element)
    {
        try
        {
            return element.Parent;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return null;
        }
    }

    /// <summary>
    /// Reads one property. A provider is allowed to refuse a property it does not support, and
    /// that is worth an empty string rather than losing the whole element.
    /// </summary>
    private static string Read(Func<string?> value)
    {
        try
        {
            return value() ?? string.Empty;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        if (_automation.IsValueCreated)
        {
            _automation.Value.Dispose();
        }
    }

    /// <summary>
    /// The window to search in: the whole desktop unless the query names one. The window is
    /// recognised by the same rule the rest of the program recognises one with, so a step that aims
    /// at a window by its program, by its class or by a pattern finds the window it means.
    /// </summary>
    private AutomationElement? Root(UiQuery query)
    {
        var desktop = _automation.Value.GetDesktop();
        if (string.IsNullOrWhiteSpace(query.WindowTitle))
        {
            return desktop;
        }

        foreach (var window in desktop.FindAllChildren(candidate => candidate.ByControlType(ControlType.Window)))
        {
            if (WindowHow.Matches(Part(window, query.WindowMatch), query.WindowTitle,
                    query.WindowCompare, query.WindowOptions))
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>
    /// What a window is held up against: what it is called, the program that owns it, or the class
    /// that program registered. Reading the program costs a look in the process table, so it is only
    /// done for a rule that asks about it.
    /// </summary>
    private static string Part(AutomationElement window, WindowMatch match) => match switch
    {
        WindowMatch.Process => ProcessOf(window),
        WindowMatch.ClassName => Read(() => window.ClassName),
        WindowMatch.Handle => Read(() => window.Properties.NativeWindowHandle.Value.ToString(
            System.Globalization.CultureInfo.InvariantCulture)),
        _ => Read(() => window.Name),
    };

    /// <summary>
    /// The name of the program that owns a window, without the ".exe", or an empty string when the
    /// program is gone or the provider will not say which one it is.
    /// </summary>
    private static string ProcessOf(AutomationElement window)
    {
        try
        {
            var id = window.Properties.ProcessId.Value;
            return id > 0 ? Process.GetProcessById(id).ProcessName : string.Empty;
        }
        catch (Exception error) when (Recoverable(error) || error is ArgumentException)
        {
            return string.Empty;
        }
    }
}
