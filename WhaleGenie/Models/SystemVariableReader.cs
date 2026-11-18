using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// Reads the value of a built-in variable at the moment it is asked for, which is what makes one of
/// these a value rather than a number that was true when the run started: the pointer moves, the
/// clipboard changes, the clock goes on, and a macro that reads them twice means to see that.
/// </summary>
/// <remarks>
/// What it reads, it reads through the device layer, which is where looking at the machine belongs:
/// this class knows which variable means what, and the devices know how to look. A machine that
/// cannot answer — no session, a stand-in layer in a check, a window list that has gone — gives the
/// variable its declared starting value instead of failing the step that read it.
/// </remarks>
public sealed class SystemVariableReader : ILiveVariables
{
    /// <summary>The names this knows, so a name that is not one of them is left alone.</summary>
    private static readonly Dictionary<string, VariableDefinition> Known =
        VariableCatalog.SystemVariables.ToDictionary(variable => variable.Name,
            StringComparer.OrdinalIgnoreCase);

    private readonly IDeviceLayer _devices;

    public SystemVariableReader(IDeviceLayer devices) => _devices = devices;

    /// <summary>The value right now, as text.</summary>
    public string Read(string name)
        => Known.TryGetValue(name, out var definition) ? ReadValue(definition).AsText() : string.Empty;

    /// <summary>
    /// The value right now, or false for a name that is not one of WhaleGenie's own: a name it does
    /// not know has to keep travelling, so a macro that misspelled one is told about it rather than
    /// reading nothing.
    /// </summary>
    public bool TryGet(string name, out Value value)
    {
        // The round number is written by the runner for the loop that is running. The machine has
        // no answer for it, and a made-up zero outside a loop would look like a real round.
        if (!Known.TryGetValue(name, out var definition)
            || string.Equals(name, VariableStore.LoopIndexName, StringComparison.OrdinalIgnoreCase))
        {
            value = Value.Null;
            return false;
        }

        value = ReadValue(definition);
        return true;
    }

    private Value ReadValue(VariableDefinition definition)
    {
        try
        {
            return Convert(definition.Type, Reading(definition.Name));
        }
        catch (Exception)
        {
            return Convert(definition.Type, definition.DefaultValue);
        }
    }

    /// <summary>One value, read off the machine. Each name here is one question with one answer.</summary>
    private string Reading(string name)
    {
        var now = DateTime.Now;
        var culture = Culture();

        return name switch
        {
            "sys.macroFolder" => _devices.Files.BaseFolder,
            "sys.activeWindow" => Foreground()?.Title ?? string.Empty,
            "sys.activeProcess" => Foreground() is { } window
                ? _devices.Windows.ProcessOf(window.Handle)
                : string.Empty,
            "sys.date" => now.ToString("yyyy-MM-dd", culture),
            "sys.dateLong" => now.ToString("yyyy-MM-dd dddd", culture),
            "sys.time" => now.ToString("HH:mm:ss", culture),
            "sys.dateTime" => now.ToString("yyyy-MM-dd HH:mm:ss", culture),
            "sys.year" => now.Year.ToString(CultureInfo.InvariantCulture),
            "sys.month" => now.Month.ToString(CultureInfo.InvariantCulture),
            "sys.day" => now.Day.ToString(CultureInfo.InvariantCulture),
            "sys.weekday" => now.ToString("dddd", culture),
            "sys.hour" => now.Hour.ToString(CultureInfo.InvariantCulture),
            "sys.minute" => now.Minute.ToString(CultureInfo.InvariantCulture),
            "sys.second" => now.Second.ToString(CultureInfo.InvariantCulture),
            "sys.timestamp" => DateTimeOffset.Now.ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture),
            "sys.mouseX" => _devices.Input.Cursor.X.ToString(CultureInfo.InvariantCulture),
            "sys.mouseY" => _devices.Input.Cursor.Y.ToString(CultureInfo.InvariantCulture),
            "sys.mouseColor" => Colour(_devices.Input.Cursor),
            "sys.screenWidth" => _devices.Screen.Desktop.Bounds.Width
                .ToString(CultureInfo.InvariantCulture),
            "sys.screenHeight" => _devices.Screen.Desktop.Bounds.Height
                .ToString(CultureInfo.InvariantCulture),
            "sys.machineName" => Environment.MachineName,
            "sys.userName" => Environment.UserName,
            "sys.clipboard" => _devices.Clipboard.ReadText(),
            _ => string.Empty,
        };
    }

    /// <summary>Colour of the pixel the pointer is over, as #RRGGBB.</summary>
    private string Colour(ScreenPoint point)
        => _devices.Screen.PixelAt(point.X, point.Y).ToHex();

    /// <summary>
    /// The window in front right now. An empty match is the frontmost window, which is what the
    /// window device means by it — the same answer a step that names no window gets.
    /// </summary>
    private WindowInfo? Foreground()
        => _devices.Windows.Filter(string.Empty, WindowMatch.Title).FirstOrDefault();

    /// <summary>Date text follows the interface language, so the weekday name matches it.</summary>
    private static CultureInfo Culture()
        => Strings.Current.Language == Language.Chinese
            ? CultureInfo.GetCultureInfo("zh-CN")
            : CultureInfo.GetCultureInfo("en-US");

    private static Value Convert(string type, string text) => type switch
    {
        "number" => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture,
            out var number)
            ? Value.FromNumber(number)
            : Value.FromNumber(0),
        "bool" => Value.FromBool(string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)),
        "list" => ListText.TryParse(text, out var items, out _)
            ? Value.FromList(items)
            : Value.FromList([]),
        _ => Value.FromText(text),
    };
}
