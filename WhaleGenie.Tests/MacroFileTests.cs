using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// One macro in one file: what a person hands somebody who does not need the rest of the project.
/// The file holds the macro, its own submacros and the pictures it looks for, and says which of the
/// two kinds it is so an import can put it back where it belongs.
/// </summary>
public class MacroFileTests
{
    /// <summary>A one pixel PNG: enough to be a real picture, small enough to write in a test.</summary>
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public void A_macro_file_keeps_the_macro_its_pictures_and_what_it_is()
    {
        var folder = TempFolder();
        try
        {
            var pictures = Path.Combine(folder, "pictures");
            Directory.CreateDirectory(pictures);
            var picture = Path.Combine(pictures, "按钮.png");
            File.WriteAllBytes(picture, Convert.FromBase64String(OnePixelPng));

            var macro = new MacroItem { Name = "登录", Description = "打开登录页" };
            macro.Steps.Add(new MacroStep
            {
                Type = "look.findPicture",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "image",
                        Kind = ActionParameterKind.Image,
                        Value = picture,
                    },
                ],
            });

            var path = Path.Combine(folder, "登录" + MacroFile.Extension);
            MacroFile.Save(path, macro, MacroFileKind.Macro, "0.0.0");

            // What the file says about itself, before anything reads it back.
            using (var archive = ZipFile.OpenRead(path))
            {
                Assert.NotNull(archive.GetEntry("manifest.json"));
                Assert.NotNull(archive.GetEntry("macro.json"));
                Assert.NotNull(archive.GetEntry("assets/按钮.png"));

                var manifest = ReadEntry(archive, "manifest.json");
                Assert.Contains("\"whalegenie.macro\"", manifest, StringComparison.Ordinal);
                Assert.Contains("\"kind\": \"macro\"", manifest, StringComparison.Ordinal);
            }

            var target = Path.Combine(folder, "arrived");
            var contents = MacroFile.Load(path, target);

            Assert.Equal("登录", contents.Macro.Name);
            Assert.Equal("打开登录页", contents.Macro.Description);
            Assert.Equal(MacroFileKind.Macro, contents.Kind);
            Assert.Single(contents.Macro.Steps);

            // The picture is beside the macro now, under a name of its own content, and the step
            // points at where it actually is.
            var stored = contents.Macro.Steps[0].Parameters.Single(parameter => parameter.Name == "image");
            Assert.True(Path.IsPathRooted(stored.Value), stored.Value);
            Assert.StartsWith(target, stored.Value, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(stored.Value), stored.Value);
            Assert.Equal(Convert.FromBase64String(OnePixelPng), File.ReadAllBytes(stored.Value));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// A submacro travels with the macro that holds it: a call to it inside the macro would mean
    /// nothing in the project the file lands in if the callee had been left behind.
    /// </summary>
    [Fact]
    public void A_macro_file_carries_its_submacros_and_says_it_is_a_submacro()
    {
        var folder = TempFolder();
        try
        {
            var inner = new MacroItem { Name = "读表" };
            inner.Steps.Add(new MacroStep { Type = "control.delay" });
            var macro = new MacroItem { Name = "初始化" };
            macro.SubMacros.Add(inner);

            var path = Path.Combine(folder, "初始化" + MacroFile.Extension);
            MacroFile.Save(path, macro, MacroFileKind.SubMacro, "0.0.0");

            var contents = MacroFile.Load(path, Path.Combine(folder, "arrived"));

            Assert.Equal(MacroFileKind.SubMacro, contents.Kind);
            Assert.Equal("初始化", contents.Macro.Name);
            var sub = Assert.Single(contents.Macro.SubMacros);
            Assert.Equal("读表", sub.Name);
            Assert.Single(sub.Steps);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>A package is a different file, and reading it as one macro would only half work.</summary>
    [Fact]
    public void A_project_package_is_not_a_macro_file()
    {
        var folder = TempFolder();
        try
        {
            var path = Path.Combine(folder, "macros" + MacroPackage.Extension);
            MacroPackage.Save(path, [new MacroItem { Name = "整包" }], [], "0.0.0");

            var failure = Assert.Throws<InvalidDataException>(
                () => MacroFile.Load(path, folder));
            Assert.Contains("whalegenie.macro.package", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>A file written by a later version is refused rather than half-read.</summary>
    [Fact]
    public void A_newer_format_is_refused_and_says_which_one()
    {
        var folder = TempFolder();
        try
        {
            var path = Path.Combine(folder, "later" + MacroFile.Extension);
            using (var stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Write(archive, "manifest.json",
                    """{"format":"whalegenie.macro","version":9}""");
                Write(archive, "macro.json", """{"name":"来自以后"}""");
            }

            var failure = Assert.Throws<InvalidDataException>(() => MacroFile.Load(path, folder));
            Assert.Contains("9", failure.Message, StringComparison.Ordinal);
            Assert.Contains(MacroFile.FormatVersion.ToString(), failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>A zip that says nothing about itself is not a macro file either.</summary>
    [Fact]
    public void A_zip_with_no_manifest_is_refused()
    {
        var folder = TempFolder();
        try
        {
            var path = Path.Combine(folder, "empty" + MacroFile.Extension);
            using (var stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Write(archive, "readme.txt", "nothing here");
            }

            Assert.Throws<InvalidDataException>(() => MacroFile.Load(path, folder));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// A file is imported into the project that is open, and a name the project already uses is
    /// not a reason to refuse it: the arriving macro is put under a name of its own, and the answer
    /// says which name it arrived under so the window can tell the person.
    /// </summary>
    [Fact]
    public void Importing_a_macro_file_puts_it_in_the_project_under_a_free_name()
    {
        Ui.RunAsync(async () =>
        {
            var folder = TempFolder();
            try
            {
                var path = Path.Combine(folder, "登录" + MacroFile.Extension);
                MacroFile.Save(path, new MacroItem { Name = "登录" }, MacroFileKind.Macro, "0.0.0");

                var viewModel = new MainViewModel();
                viewModel.AddMacro(new MacroItem { Name = "登录" });

                var (macro, arrivedAs) = viewModel.ImportMacro(path);

                Assert.Equal("登录", arrivedAs);
                Assert.NotEqual("登录", macro.Name);
                Assert.Contains(viewModel.Macros, item => ReferenceEquals(item, macro));
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
