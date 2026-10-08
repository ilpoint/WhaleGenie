using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Headless;
using WhaleGenie.Storage;

// The headless session builds the application from WhaleGenie.App, so the tests work on a machine
// with no desktop and never touch the real screen, keyboard or mouse.
[assembly: AvaloniaTestApplication(typeof(WhaleGenie.App))]

namespace WhaleGenie.Tests;

/// <summary>
/// A window opened for a check writes the recovery snapshot as it goes, exactly as the real one
/// does. Left where the program keeps it, those writes would land in the file this machine would
/// offer back the next time it started; the checks point it at a file of their own instead. A check
/// that is about the snapshot sets its own path on top of this.
/// </summary>
internal static class RecoveryPathForChecks
{
    [ModuleInitializer]
    internal static void UseOwnFile()
        => RecoveryStore.FilePath = Path.Combine(
            Path.GetTempPath(), "whalegenie-tests", "checks-recovery.json");
}
