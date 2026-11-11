using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Tests;

// Two macros of one name, and a suffixed name that is already taken.
public partial class MacroPackageFormatTests
{
    /// <summary>
    /// A package can carry two macros of one name — the editor refuses to save a second one, so this
    /// is a file somebody edited by hand. Both macros are still work, so the later one is renamed
    /// with the day it was read rather than dropped by a table keyed on the name.
    /// </summary>
    [Fact]
    public void A_package_with_two_macros_of_one_name_keeps_both()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "duplicate.wgmacro");
            var first = new MacroItem { Name = "整理" };
            first.Steps.Add(Step("先读到的"));
            var second = new MacroItem { Name = "整理" };
            second.Steps.Add(Step("后读到的"));
            MacroPackage.Save(path, [first, second], [], "test");

            var loaded = MacroPackage.Load(path).Macros;

            Assert.Equal(2, loaded.Count);
            Assert.Equal("整理", loaded[0].Name);
            Assert.StartsWith("整理 (", loaded[1].Name, StringComparison.Ordinal);
            Assert.EndsWith(")", loaded[1].Name, StringComparison.Ordinal);

            // The steps say which macro is which: a suffix that had swallowed the body would leave
            // two macros and one of them empty.
            Assert.Equal("先读到的", loaded[0].Steps[0].Text("notificationText"));
            Assert.Equal("后读到的", loaded[1].Steps[0].Text("notificationText"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// The name a suffix would produce may itself be taken — by a macro the file really calls that,
    /// or by a third copy. A count is added rather than pushing that macro aside or giving up.
    /// </summary>
    [Fact]
    public void A_suffixed_name_that_is_already_taken_gets_a_count()
    {
        var day = new DateTime(2026, 10, 26);
        var macros = new List<MacroItem>
        {
            new() { Name = "整理" },
            new() { Name = "整理" },
            new() { Name = "整理 (2026-10-26)" },
            new() { Name = "整理" },
        };

        MacroPackage.Disambiguate(macros, day);

        Assert.Equal(["整理", "整理 (2026-10-26) 2", "整理 (2026-10-26)", "整理 (2026-10-26) 3"],
            macros.Select(macro => macro.Name));
    }

    /// <summary>A notification step, so a macro read back has something in it to tell it apart by.</summary>
    private static MacroStep Step(string text)
        => new()
        {
            Type = "system.notify",
            Parameters =
            [
                new StepParameter
                {
                    Name = "notificationText",
                    Kind = ActionParameterKind.Text,
                    Value = text,
                },
            ],
        };

    /// <summary>One row of a list parameter, written the way the editor writes it.</summary>
    private static StepParameterRow Row(params (string Column, string? Value)[] columns)
    {
        var row = new StepParameterRow();
        foreach (var (column, value) in columns)
        {
            if (value is not null)
            {
                row.Set(column, value);
            }
        }

        return row;
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
