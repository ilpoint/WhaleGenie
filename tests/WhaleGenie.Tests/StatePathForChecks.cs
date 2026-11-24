using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

/// <summary>
/// The settings file and the runtime log, pointed at a folder of their own before any test in the
/// assembly starts.
/// </summary>
/// <remarks>
/// A settings change writes the person's <c>settings.json</c> and a failure writes the runtime log —
/// exactly as the real program does. Left where the program keeps them, those writes would change
/// what this machine offers back the next time it starts, and suites running side by side would
/// write the same files. Every test assembly links this file, so the redirect runs before its first
/// check: one file set per process, and a check that is about one of these files sets its own path
/// on top.
/// </remarks>
internal static class StatePathForChecks
{
    [ModuleInitializer]
    internal static void UseOwnFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests");
        Directory.CreateDirectory(folder);
        var process = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        AppSettings.Shared.FilePath = Path.Combine(folder, "checks-settings-" + process + ".json");
        AppDiagnostics.FilePathOverride = Path.Combine(folder, "checks-runtime-" + process + ".log");
    }
}
