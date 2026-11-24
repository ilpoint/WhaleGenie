using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// The recovery snapshot, pointed at a file of its own before any window check starts.
/// </summary>
/// <remarks>
/// A window opened for a check writes the snapshot as it goes; left where the program keeps it,
/// those writes would land in the file this machine offers back the next time it starts. Suites
/// that can reach <see cref="RecoveryStore"/> link this file; a suite that cannot does not need
/// it, because it never opens a window.
/// </remarks>
internal static class RecoveryPathForChecks
{
    [ModuleInitializer]
    internal static void UseOwnFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests");
        Directory.CreateDirectory(folder);
        var process = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        RecoveryStore.FilePath = Path.Combine(folder, "checks-recovery-" + process + ".json");
    }
}
