using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

/// <summary>
/// The two questions every surface that names a window asks: which windows are open right now, and
/// which one the desktop picker took. The settings rule form and the editor's window title field
/// both read the same list, so the projection lives here instead of in either page's bridge.
/// </summary>
internal sealed class WindowListBridge : IBridgeHandler
{
    private readonly IWindowDevice _windows;
    private readonly IWindowPickHost _picks;

    internal WindowListBridge(IWindowDevice windows, IWindowPickHost picks)
    {
        _windows = windows;
        _picks = picks;
    }

    public void Register(BridgeServer server)
    {
        server.Register("window.list", _ => Task.FromResult<object?>(List()));
        server.Register("window.pick", _ => Pick());
    }

    /// <summary>
    /// What is open, minus this program's own windows: picking the program itself as a macro's
    /// target is never what was meant. A desktop that will not hand its windows over simply has
    /// nothing to offer, which reads as an empty list rather than a failure to open the picker.
    /// </summary>
    private WindowRulePickerState List()
    {
        IReadOnlyList<WindowInfo> open;
        try
        {
            open = _windows.List();
        }
        catch (Exception)
        {
            open = [];
        }

        var own = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        return new WindowRulePickerState(
            [.. open
                .Where(window => !string.Equals(Read(() => _windows.ProcessOf(window.Handle)), own,
                    StringComparison.OrdinalIgnoreCase))
                .Select(Entry)],
            [.. WindowHow.Values.Select((value, index) => new SettingsChoice(
                value, Strings.Get($"WindowRule.How.{value}", WindowHow.Labels[index])))]);
    }

    /// <summary>The one desktop pick, still handed to a native overlay; null when it was dismissed.</summary>
    private async Task<object?> Pick()
    {
        var window = await _picks.PickWindowAsync();
        return window is null ? null : Entry(window);
    }

    /// <summary>
    /// One row, whether it came from the list or from the picker: the fields a rule can be built
    /// from, plus the line underneath the title so the parts can be weighed against each other.
    /// </summary>
    private WindowEntryState Entry(WindowInfo window)
    {
        var process = Read(() => _windows.ProcessOf(window.Handle));
        var className = Read(() => _windows.ClassOf(window.Handle));
        var detail = Describe(window);
        return new WindowEntryState(
            window.Handle,
            window.Title,
            process,
            className,
            detail,
            string.Join(" · ", new[] { process, className }
                .Where(part => part.Length > 0)
                .Append(detail)));
    }

    /// <summary>
    /// A window that stopped existing between the list and the read still has its title and
    /// handle; an empty program or class just means the rule would have to use the title.
    /// </summary>
    private static string Read(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>How big the window is and whether it is minimised, in the wording the page shows.</summary>
    private static string Describe(WindowInfo window)
    {
        var state = window.Minimized
            ? "WindowPicker.Minimized"
            : window.Maximized
                ? "WindowPicker.Maximized"
                : "WindowPicker.Normal";

        return $"{Strings.Format("WindowPicker.Size", window.Size.Width, window.Size.Height)}" +
               $" · {Strings.Get(state)}";
    }
}
