using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// The one package format: a project and a public library are the same outer archive, and the
/// <c>library/entries.json</c> section is what makes one a library. Neither entry point may read
/// the other's kind.
/// </summary>
public class PackageKindTests
{
    [Fact]
    public void A_project_and_a_library_share_the_outer_format_and_differ_by_section()
    {
        var folder = Folder();
        try
        {
            var project = Path.Combine(folder, "project.wgmacro");
            var library = Path.Combine(folder, "public-macros.wgmacro");
            MacroPackage.Save(project, [new MacroItem { Name = "project" }],
                new ProjectTriggers(), [], "test");
            PublicMacroLibrary.Save(library,
                [new PublicMacroEntry("public-id", 4, new MacroItem { Name = "shared" })], "test");

            Assert.Equal(MacroPackage.FormatId, FormatOf(project));
            Assert.Equal(MacroPackage.FormatId, FormatOf(library));
            Assert.Equal(MacroPackage.FormatVersion, VersionOf(project));
            Assert.Equal(MacroPackage.FormatVersion, VersionOf(library));
            Assert.False(HasEntry(project, "library/entries.json"));
            Assert.True(HasEntry(project, "triggers.json"));
            Assert.True(HasEntry(library, "library/entries.json"));
            Assert.False(HasEntry(library, "triggers.json"));

            var loaded = Assert.Single(MacroPackage.LoadLibrary(library).Entries);
            Assert.Equal("public-id", loaded.Id);
            Assert.Equal(4, loaded.Version);
            Assert.Equal("shared", loaded.Macro.Name);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Each_entry_point_refuses_the_other_package_kind()
    {
        var folder = Folder();
        try
        {
            var project = Path.Combine(folder, "project.wgmacro");
            var library = Path.Combine(folder, "public-macros.wgmacro");
            MacroPackage.Save(project, [new MacroItem { Name = "project" }],
                new ProjectTriggers(), [], "test");
            PublicMacroLibrary.Save(library,
                [new PublicMacroEntry("public-id", 1, new MacroItem { Name = "shared" })], "test");

            var asProject = Assert.Throws<PackageKindMismatchException>(
                () => MacroPackage.Load(library));
            Assert.Equal(PackageKind.Library, asProject.Actual);

            var asLibrary = Assert.Throws<PackageKindMismatchException>(
                () => MacroPackage.LoadLibrary(project));
            Assert.Equal(PackageKind.Project, asLibrary.Actual);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_library_section_without_entries_is_a_broken_package()
    {
        var folder = Folder();
        try
        {
            var path = Path.Combine(folder, "broken.wgmacro");
            MacroPackage.Save(path, [new MacroItem { Name = "project" }],
                new ProjectTriggers(), [], "test");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                using var writer = new StreamWriter(archive.CreateEntry("library/other.json").Open());
                writer.Write("{}");
            }

            var error = Assert.Throws<InvalidDataException>(() => MacroPackage.LoadLibrary(path));
            Assert.Contains("entries.json", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_library_package_cannot_carry_project_triggers()
    {
        var folder = Folder();
        try
        {
            var path = Path.Combine(folder, "public-macros.wgmacro");
            PublicMacroLibrary.Save(path,
                [new PublicMacroEntry("public-id", 1, new MacroItem { Name = "shared" })], "test");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                using var writer = new StreamWriter(archive.CreateEntry("triggers.json").Open());
                writer.Write("{}");
            }

            var error = Assert.Throws<InvalidDataException>(() => MacroPackage.LoadLibrary(path));
            Assert.Contains("triggers", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void The_outer_version_did_not_change_for_the_library_section()
    {
        Assert.Equal(33, MacroPackage.FormatVersion);
        Assert.Equal(1, MacroPackage.LibraryEntriesVersion);
        Assert.Equal(".wgmacro", MacroPackage.Extension);
    }

    private static string FormatOf(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var manifest = Manifest(archive);
        return manifest["format"]!.GetValue<string>();
    }

    private static int VersionOf(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var manifest = Manifest(archive);
        return manifest["version"]!.GetValue<int>();
    }

    private static JsonObject Manifest(ZipArchive archive)
    {
        using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
        return (JsonObject)JsonNode.Parse(reader.ReadToEnd())!;
    }

    private static bool HasEntry(string path, string name)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.GetEntry(name) is not null;
    }

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
