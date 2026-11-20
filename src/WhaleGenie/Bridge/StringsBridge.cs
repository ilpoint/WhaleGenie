using System;
using System.Threading.Tasks;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

/// <summary>
/// Hands the page the whole word table and keeps it current. The page draws only from what this
/// bridge sends: which table a key belongs to and what the fallback is are decided once, in
/// <see cref="Strings.Snapshot"/>, and the page never guesses.
/// </summary>
public sealed class StringsBridge : IBridgeHandler, IDisposable
{
    private BridgeServer? _server;

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("strings.bundle", _ => Task.FromResult<object?>(Bundle()));
        Strings.Current.LanguageChanged += OnLanguageChanged;
    }

    public void Dispose() => Strings.Current.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged()
        => _ = _server?.PushAsync("strings.changed", Bundle());

    private static object Bundle() => new
    {
        language = LanguageName(),
        entries = Strings.Snapshot(),
    };

    private static string LanguageName() => Strings.Current.Language switch
    {
        Language.Chinese => "zh-Hans",
        _ => "en",
    };
}
