using System;
using System.IO;
using System.Text.Json;

namespace Viktor.Localization;

/// <summary>Small JSON file that remembers the chosen language between runs.</summary>
public static class LocalSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Viktor",
        "settings.json");

    /// <summary>Reads the stored language, falling back to English.</summary>
    public static Language Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return Language.English;
            }

            var stored = JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath));
            return Enum.TryParse<Language>(stored?.Language, out var language)
                ? language
                : Language.English;
        }
        catch
        {
            return Language.English;
        }
    }

    /// <summary>Stores the chosen language. Failures are ignored on purpose.</summary>
    public static void Store(Language language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(new Model { Language = language.ToString() }));
        }
        catch
        {
            // A settings file that cannot be written must never break the app.
        }
    }

    private sealed class Model
    {
        public string Language { get; set; } = nameof(Localization.Language.English);
    }
}
