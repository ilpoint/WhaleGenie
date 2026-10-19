using System;
using System.IO;
using System.IO.Compression;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// A package is the macro file, so a package written with the old field names is refused rather
/// than read as if its parameters still meant something. There is no migration branch: the version
/// has to be the one this build writes.
/// </summary>
public class MacroPackageFormatTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void A_package_from_an_older_format_is_refused(int version)
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "old.wgmacro");
            MacroPackage.Save(path, [new MacroItem { Name = "old" }], [], "test");
            RewriteVersion(path, version);

            var error = Assert.Throws<InvalidDataException>(() => MacroPackage.Load(path));
            Assert.Contains(version.ToString(), error.Message, StringComparison.Ordinal);
            Assert.Contains(MacroPackage.FormatVersion.ToString(), error.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_captured_window_rule_survives_the_package()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "rule.wgmacro");
            var rule = new WhaleGenie.Core.Devices.WindowRule(
                Title: "Game",
                Process: "game",
                ClassName: "GameClass",
                Handle: 42,
                How: WhaleGenie.Core.Devices.WindowHow.Process,
                CaseSensitive: true,
                Trim: false,
                Multiline: true);
            var macro = new MacroItem
            {
                Name = "rule",
                TriggerMode = MacroTrigger.Window,
                WindowTarget = rule,
                WindowChange = WindowChangeKind.Disappeared,
            };

            MacroPackage.Save(path, [macro], [], "test");
            var loaded = Assert.Single(MacroPackage.Load(path).Macros);

            Assert.Equal(rule, loaded.WindowTarget);
            Assert.Equal(WindowChangeKind.Disappeared, loaded.WindowChange);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_background_input_step_keeps_its_rule_through_save_load_and_conversion()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "input.wgmacro");
            var rule = new WindowRule(Title: "Game", Process: "game", ClassName: "GameClass",
                Handle: 42, How: WindowHow.Process);
            var macro = new MacroItem { Name = "input" };
            macro.Steps.Add(new MacroStep
            {
                Type = "input.keyPress",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "inputMode",
                        Kind = ActionParameterKind.Choice,
                        Value = "background",
                    },
                    new StepParameter
                    {
                        Name = "window",
                        Kind = ActionParameterKind.WindowRule,
                        Value = rule.ToJson().ToJsonString(),
                    },
                    new StepParameter
                    {
                        Name = "key",
                        Kind = ActionParameterKind.Key,
                        Value = "F5",
                    },
                ],
            });

            MacroPackage.Save(path, [macro], [], "test");
            var loaded = Assert.Single(MacroPackage.Load(path).Macros);
            var step = Assert.Single(loaded.Steps);
            var window = Assert.Single(step.Parameters, parameter => parameter.Name == "window");
            Assert.Equal(ActionParameterKind.WindowRule, window.Kind);
            Assert.True(WindowRule.TryParse(window.Value, out var parsed));
            Assert.Equal(rule, parsed);

            var executable = Assert.Single(new[] { step }.ToExecutable());
            var executableWindow = Assert.Single(executable.Parameters,
                parameter => parameter.Name == "window");
            Assert.True(WindowRule.TryParse(executableWindow.Text, out var executableRule));
            Assert.Equal(rule, executableRule);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static void RewriteVersion(string path, int version)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var manifest = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("the package has no manifest.json");

        string text;
        using (var reader = new StreamReader(manifest.Open()))
        {
            text = reader.ReadToEnd();
        }

        manifest.Delete();
        var replacement = archive.CreateEntry("manifest.json");
        using var writer = new StreamWriter(replacement.Open());
        writer.Write(text.Replace($"\"version\": {MacroPackage.FormatVersion}",
            $"\"version\": {version}", StringComparison.Ordinal));
    }
}
