using System;
using System.IO;
using System.Text.Json;
using Viktor.Execution;

namespace Viktor.Localization;

/// <summary>Small JSON file that remembers the choices made in the settings window between runs.</summary>
public static class LocalSettings
{
    /// <summary>
    /// The file the choices live in: beside the program, so a copy of Viktor carries its own
    /// choices with it. It can be moved so a check runs against a file of its own instead of the
    /// one whoever is using Viktor keeps.
    /// </summary>
    internal static string FilePath { get; set; } = AppPaths.Settings;

    /// <summary>Reads the stored language, falling back to English.</summary>
    public static Language Load()
        => Enum.TryParse<Language>(Read().Language, out var language)
            ? language
            : Language.English;

    /// <summary>Stores the chosen language. Failures are ignored on purpose.</summary>
    public static void Store(Language language) => Write(model => model.Language = language.ToString());

    /// <summary>
    /// Whether a run that stops on a failure leaves a picture of the screen behind. On by default:
    /// a failure nobody can see is the one a picture helps with most.
    /// </summary>
    public static bool LoadFailureScreenshot() => Read().FailureScreenshot;

    /// <summary>Stores that choice. Failures are ignored on purpose.</summary>
    public static void StoreFailureScreenshot(bool value)
        => Write(model => model.FailureScreenshot = value);

    private static Model Read()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath)) ?? new Model()
                : new Model();
        }
        catch
        {
            return new Model();
        }
    }

    /// <summary>
    /// Changes one choice and writes the file back. The whole file is written every time, so the
    /// choice being changed has to start from what was already stored or it would drop the rest.
    /// </summary>
    private static void Write(Action<Model> change)
    {
        try
        {
            var model = Read();
            change(model);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(model));
        }
        catch
        {
            // A settings file that cannot be written must never break the app.
        }
    }

    private sealed class Model
    {
        public string Language { get; set; } = nameof(Localization.Language.English);

        public bool FailureScreenshot { get; set; } = true;
    }
}
