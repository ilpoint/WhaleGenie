using System;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

/// <summary>
/// Hands every page the whole word table and keeps it current. A page draws only from what this
/// bridge sends: which table a key belongs to and what the fallback is are decided once, in
/// <see cref="Strings.Snapshot"/>, and no page ever guesses.
/// </summary>
public sealed class StringsBridge : IBridgeHandler, IDisposable
{
    private BridgeServer? _server;

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("strings.bundle", _ =>
        {
            // The answer is current; the shell's buffered language change from before it was
            // ready would at best repeat it and must not land after it. An overlay's copy stays:
            // it asked before this answer existed and may still have a newer change to apply.
            server.DropPending("strings.changed");
            return Task.FromResult<object?>(Bundle());
        });
        Strings.Current.LanguageChanged += OnLanguageChanged;
    }

    public void Dispose() => Strings.Current.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged()
        => _ = _server?.PushAllAsync("strings.changed", Bundle());

    private static StringsBundle Bundle()
        => new(LanguageName(), Strings.Snapshot());

    private static string LanguageName() => Strings.Current.Language switch
    {
        Language.Chinese => "zh-Hans",
        _ => "en",
    };
}
