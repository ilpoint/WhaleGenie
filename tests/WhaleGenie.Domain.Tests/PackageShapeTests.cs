using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// The pure package shape and README take no archive and no clock. The shell hands them the manifest,
/// two booleans and the saved time, so the rules can be checked without a zip.
/// </summary>
public class PackageShapeTests
{
    [Fact]
    public void A_manifest_with_both_sections_keeps_them_separate()
    {
        var manifest = Manifest(MacroPackage.FormatVersion, "1.2.3");

        var shape = PackageShapeReader.Read(manifest, hasTriggers: true,
            hasLibraryFolder: true, libraryNode: new JsonObject());

        Assert.True(shape.HasProject);
        Assert.True(shape.HasLibrary);
        Assert.Equal(MacroPackage.FormatVersion, shape.FormatVersion);
        Assert.Equal("1.2.3", shape.AppVersion);
    }

    [Fact]
    public void An_old_version_is_refused_before_the_nodes_are_read()
    {
        var error = Assert.Throws<System.IO.InvalidDataException>(() =>
            PackageShapeReader.Read(Manifest(MacroPackage.FormatVersion - 1, "old"),
                hasTriggers: true, hasLibraryFolder: false, libraryNode: null));

        Assert.Contains((MacroPackage.FormatVersion - 1).ToString(), error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_library_folder_without_entries_is_broken()
    {
        Assert.Throws<System.IO.InvalidDataException>(() =>
            PackageShapeReader.Read(Manifest(MacroPackage.FormatVersion, "test"),
                hasTriggers: false, hasLibraryFolder: true, libraryNode: null));
    }

    [Fact]
    public void The_manifest_builder_is_a_pure_function_of_collected_parts()
    {
        var saved = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var manifest = PackageManifestBuilder.Build("1.0", saved,
            [new PackageManifestMacro("one", true, "nodes/01-one.json", 2)],
            [new PackageManifestVariable("count", "Number", JsonValue.Create(1), "")],
            ["assets/a.png"]);

        Assert.Equal(MacroPackage.FormatId, manifest["format"]!.GetValue<string>());
        Assert.Equal(MacroPackage.FormatVersion, manifest["version"]!.GetValue<int>());
        Assert.Equal("2026-01-02T03:04:05.0000000Z", manifest["savedUtc"]!.GetValue<string>());
        Assert.Equal("nodes/01-one.json", manifest["macros"]![0]!["document"]!.GetValue<string>());
        Assert.Equal("assets/a.png", manifest["assets"]![0]!.GetValue<string>());

        var library = PackageManifestBuilder.BuildLibrary(
            [new PackageManifestLibraryEntry("id", 0, "name", "description", "nodes/01.json")]);
        Assert.Equal(1, library["entries"]![0]!["version"]!.GetValue<int>());
    }

    [Fact]
    public void The_readme_is_a_function_of_its_arguments()
    {
        var saved = new DateTime(2026, 1, 2, 3, 4, 5);

        var first = PackageReadme.Build("1.0", [], [], [], projectSection: true,
            librarySection: false, saved);
        var second = PackageReadme.Build("1.0", [], [], [], projectSection: true,
            librarySection: false, saved);

        Assert.Equal(first, second);
        Assert.Contains("2026-01-02 03:04:05", first, StringComparison.Ordinal);
        Assert.Contains("`triggers.json`", first, StringComparison.Ordinal);
        Assert.DoesNotContain("library/entries.json", first, StringComparison.Ordinal);
    }

    private static JsonObject Manifest(int version, string appVersion) => new()
    {
        ["format"] = MacroPackage.FormatId,
        ["version"] = version,
        ["app"] = "WhaleGenie",
        ["appVersion"] = appVersion,
        ["savedUtc"] = "2026-01-01T00:00:00Z",
        ["macros"] = new JsonArray(),
        ["variables"] = new JsonArray(),
        ["assets"] = new JsonArray(),
    };
}
