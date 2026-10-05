using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.UIA3;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Finds and drives windows and controls through Windows UI Automation. Elements are found
/// by what the user sees, so a macro reads the way the screen reads.
/// </summary>
public sealed class FlaUiDevice : IUiDevice, IDisposable
{
    private readonly Lazy<UIA3Automation> _automation = new(() => new UIA3Automation());

    /// <summary>
    /// Used for the button UI Automation has no click of its own for, by clicking the
    /// element's own clickable point.
    /// </summary>
    private readonly Lazy<IInputDevice> _input;

    public FlaUiDevice(IInputDevice? input = null)
    {
        _input = new Lazy<IInputDevice>(() => input ?? new SharpHookInputDevice());
    }

    public bool Exists(UiQuery query, int timeoutMs)
    {
        Require();
        if (query.IsEmpty)
        {
            throw new DeviceActionException("Run.EmptySelector");
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
        var element = Find(query) ?? throw new DeviceActionException("Run.ElementNotFound", Describe(query));

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

    public bool FocusWindow(string title)
    {
        Require();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DeviceActionException("Run.MissingWindow", title);
        }

        var window = Root(new UiQuery(WindowTitle: title));
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

    public string? GetText(UiQuery query)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException("Run.ElementNotFound", Describe(query));

        try
        {
            var value = element.Patterns.Value;
            return value.IsSupported ? value.Pattern.Value.Value : element.Name;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return element.Name;
        }
    }

    public bool SetText(UiQuery query, string text, bool clearFirst)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException("Run.ElementNotFound", Describe(query));

        try
        {
            var value = element.Patterns.Value;
            if (!value.IsSupported)
            {
                // Nothing to write into, so put the caret there and type instead — but only
                // once the element really holds the keyboard, or the text lands somewhere else.
                element.Focus();
                if (element.Properties.HasKeyboardFocus.ValueOrDefault is not true)
                {
                    return false;
                }

                _input.Value.TypeText(text, 0);
                return true;
            }

            var current = value.Pattern.Value.Value ?? string.Empty;
            value.Pattern.SetValue(clearFirst ? text : current + text);
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_automation.IsValueCreated)
        {
            _automation.Value.Dispose();
        }
    }

    /// <summary>The window to search in: the whole desktop unless the query names one.</summary>
    private AutomationElement? Root(UiQuery query)
    {
        var desktop = _automation.Value.GetDesktop();
        if (string.IsNullOrWhiteSpace(query.WindowTitle))
        {
            return desktop;
        }

        foreach (var window in desktop.FindAllChildren(candidate => candidate.ByControlType(ControlType.Window)))
        {
            if (window.Name?.Contains(query.WindowTitle, StringComparison.OrdinalIgnoreCase) == true)
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>The first element the query describes, or null when nothing matches.</summary>
    private AutomationElement? Find(UiQuery query)
    {
        try
        {
            var root = Root(query);
            if (root is null)
            {
                return null;
            }

            var condition = Condition(query);
            if (condition is null)
            {
                // Only a window was named, so the window itself is the answer.
                return root;
            }

            return root.FindFirstDescendant(condition) ?? (Matches(root, query) ? root : null);
        }
        catch (Exception error) when (Recoverable(error))
        {
            return null;
        }
    }

    private ConditionBase? Condition(UiQuery query)
    {
        var factory = _automation.Value.ConditionFactory;
        ConditionBase? condition = null;

        void Add(ConditionBase part) => condition = condition is null ? part : condition.And(part);

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            Add(factory.ByName(query.Name!));
        }

        if (!string.IsNullOrWhiteSpace(query.AutomationId))
        {
            Add(factory.ByAutomationId(query.AutomationId!));
        }

        if (!string.IsNullOrWhiteSpace(query.ClassName))
        {
            Add(factory.ByClassName(query.ClassName!));
        }

        if (!string.IsNullOrWhiteSpace(query.ControlType) && ControlTypeOf(query.ControlType!) is { } type)
        {
            Add(factory.ByControlType(type));
        }

        return condition;
    }

    private static bool Matches(AutomationElement element, UiQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Name)
            && !string.Equals(element.Name, query.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.AutomationId)
            && !string.Equals(element.AutomationId, query.AutomationId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private void ClickAt(AutomationElement element, string button, UiQuery query)
    {
        if (!element.TryGetClickablePoint(out var point))
        {
            throw new DeviceActionException("Run.ElementNotClickable", Describe(query));
        }

        _input.Value.Click(button, (int)point.X, (int)point.Y, 1, 0);
    }

    /// <summary>Turns "Button" or "Edit" into the control type it names.</summary>
    private static ControlType? ControlTypeOf(string text)
        => Enum.TryParse<ControlType>(text.Trim(), ignoreCase: true, out var type) ? type : null;

    /// <summary>How an element is described in a failure message.</summary>
    private static string Describe(UiQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.AutomationId))
        {
            return string.IsNullOrWhiteSpace(query.ControlType)
                ? query.AutomationId!
                : $"{query.ControlType}[automationId='{query.AutomationId}']";
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            return string.IsNullOrWhiteSpace(query.ControlType)
                ? query.Name!
                : $"{query.ControlType}[name='{query.Name}']";
        }

        return query.ControlType ?? query.WindowTitle ?? string.Empty;
    }

    /// <summary>
    /// True for the failures that mean "that element is not there", which is an answer rather
    /// than a problem: anything the provider refuses, anything that went away, any timeout.
    /// Cancellation and the device's own refusals are left to travel on.
    /// </summary>
    private static bool Recoverable(Exception error)
        => error is not OperationCanceledException
            and not DeviceActionException
            and not DeviceUnavailableException
            and not OutOfMemoryException;

    private static void Require()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("UI Automation");
        }
    }
}
