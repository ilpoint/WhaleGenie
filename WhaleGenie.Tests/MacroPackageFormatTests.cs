using System;
using System.IO;
using System.IO.Compression;
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
    [Fact]
    public void A_package_from_an_older_format_is_refused()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "old.wgmacro");
            MacroPackage.Save(path, [new MacroItem { Name = "old" }], [], "test");
            RewriteVersion(path, 1);

            var error = Assert.Throws<InvalidDataException>(() => MacroPackage.Load(path));
            Assert.Contains("1", error.Message, StringComparison.Ordinal);
            Assert.Contains(MacroPackage.FormatVersion.ToString(), error.Message,
                StringComparison.Ordinal);
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
