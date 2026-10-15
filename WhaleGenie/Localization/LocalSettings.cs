using System;
using System.IO;
using System.Text.Json;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Localization;

/// <summary>Small JSON file that remembers the choices made in the settings window between runs.</summary>
public static class LocalSettings
{
    /// <summary>
    /// The file the choices live in: beside the program, so a copy of WhaleGenie carries its own
    /// choices with it. It can be moved so a check runs against a file of its own instead of the
    /// one whoever is using WhaleGenie keeps.
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
    /// Whether the text recognition models are loaded in the background as the program starts. Off
    /// by default: loading them holds memory for the whole run, and a machine that never reads text
    /// should not pay for a machine that does.
    /// </summary>
    public static bool LoadPreloadOcr() => Read().PreloadOcr;

    /// <summary>Stores that choice. Failures are ignored on purpose.</summary>
    public static void StorePreloadOcr(bool value) => Write(model => model.PreloadOcr = value);

    /// <summary>
    /// Whether the virtual keyboard and mouse are put on the machine as the program starts. Off by
    /// default: a machine that never uses driver-level input should not have virtual devices on it,
    /// and putting them on is a wait only the first driver-level step of a macro would pay for.
    /// </summary>
    public static bool LoadDriverAttachAtStart() => Read().DriverAttachAtStart;

    /// <summary>Stores that choice. Failures are ignored on purpose.</summary>
    public static void StoreDriverAttachAtStart(bool value)
        => Write(model => model.DriverAttachAtStart = value);

    /// <summary>
    /// How this machine reads a picture of a window. Automatic by default, which is the order the
    /// machine has always tried the ways in.
    /// </summary>
    public static CaptureWay LoadCaptureWindow()
        => CaptureWayNames.Read(Read().CaptureWindow);

    /// <summary>Stores that choice, as the way is written in a file. Failures are ignored on purpose.</summary>
    public static void StoreCaptureWindow(CaptureWay way)
        => Write(model => model.CaptureWindow = CaptureWayNames.Written(way));

    /// <summary>
    /// How this machine reads a picture of the desktop. Automatic by default, which is the order the
    /// machine has always tried the ways in.
    /// </summary>
    public static CaptureWay LoadCaptureScreen()
        => CaptureWayNames.Read(Read().CaptureScreen);

    /// <summary>Stores that choice, as the way is written in a file. Failures are ignored on purpose.</summary>
    public static void StoreCaptureScreen(CaptureWay way)
        => Write(model => model.CaptureScreen = CaptureWayNames.Written(way));

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

        public bool PreloadOcr { get; set; }

        public bool DriverAttachAtStart { get; set; }

        public string CaptureWindow { get; set; } = string.Empty;

        public string CaptureScreen { get; set; } = string.Empty;
    }
}
