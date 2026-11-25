using System;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>
/// The system switch and the key that flips it, as the side bar draws them. The switch itself is
/// the one object the trigger host, the hotkey and the page all write; this handler only reads it
/// and tells the page when it moved.
/// </summary>
public sealed class SystemBridge : IBridgeHandler, IDisposable
{
    private readonly SystemState _system;
    private readonly AppSettings _settings;
    private BridgeServer? _server;

    public SystemBridge(SystemState system, AppSettings settings)
    {
        _system = system;
        _settings = settings;
    }

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("system.state", _ => Task.FromResult<object?>(State()));
        _system.Changed += Publish;

        // "No key" is a word-table sentence, so a language switch changes what the side bar draws
        // even though neither the switch nor the binding moved.
        Strings.Current.LanguageChanged += Publish;
    }

    public void Dispose()
    {
        _system.Changed -= Publish;
        Strings.Current.LanguageChanged -= Publish;
    }

    /// <summary>The state the side bar reads; the same answer after a setting changed the key.</summary>
    public SystemSwitchState State()
    {
        var key = _settings.LoadSystemHotkey();
        return new SystemSwitchState(_system.IsEnabled,
            key.Length > 0 ? key : Strings.Get("Common.None"));
    }

    /// <summary>
    /// Tells the page the switch or its key moved. Called by the switch itself and by the settings
    /// page after it stored a new hotkey; both are facts the side bar shows at all times.
    /// </summary>
    public void Publish() => _ = _server?.PushAsync("system.changed", State());
}
