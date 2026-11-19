using System;
using System.IO;
using WhaleGenie.Localization;

namespace WhaleGenie.Tests;

/// <summary>
/// Runs a check with the interface in Chinese, which is where the Chinese names of the expression
/// functions and the plain reading of a formula belong: an English interface shows the code names
/// and leaves those out. The check runs over a settings file of its own, because WhaleGenie's own
/// settings belong to whoever is using it.
/// </summary>
internal static class InterfaceLanguage
{
    public static void InChinese(Action check) => In(Language.Chinese, check);

    /// <summary>The same, the other way round, for what the interface leaves out in English.</summary>
    public static void InEnglish(Action check) => In(Language.English, check);

    private static void In(Language language, Action check)
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "whalegenie-language-" + Guid.NewGuid().ToString("N"));
        var storedFile = LocalSettings.FilePath;
        var stored = Strings.Current.Language;
        try
        {
            Directory.CreateDirectory(folder);
            LocalSettings.FilePath = Path.Combine(folder, "settings.json");
            Strings.Current.Language = language;
            check();
        }
        finally
        {
            Strings.Current.Language = stored;
            LocalSettings.FilePath = storedFile;
            Directory.Delete(folder, true);
        }
    }
}
