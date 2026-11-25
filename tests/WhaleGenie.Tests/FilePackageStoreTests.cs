using System;
using System.IO;
using System.Threading.Tasks;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// The package store boundary: a missing file, a broken archive and a path the file system refuses
/// come back as package problems rather than as raw exceptions; a write leaves the old package
/// untouched when the new one cannot be made.
/// </summary>
public class FilePackageStoreTests
{
    private readonly FilePackageStore _store = new();

    [Fact]
    public async Task A_package_round_trips_through_the_store()
    {
        var folder = Folder();
        try
        {
            var path = Path.Combine(folder, "round.wgmacro");
            var macro = new MacroItem { Name = "round" };
            macro.Steps.Add(new MacroStep { Type = "control.log" });
            var write = await _store.WriteAsync(new PackageWriteRequest(
                path, [macro], new ProjectTriggers(), [], "test", null));
            Assert.True(write.Ok, write.Message);

            var read = await _store.ReadAsync(path, Path.Combine(folder, "assets"));
            Assert.True(read.Ok, read.Message);
            Assert.Equal("round", Assert.Single(read.Value!.Macros).Name);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task A_missing_file_is_reported_as_not_found()
    {
        var folder = Folder();
        try
        {
            var read = await _store.ReadAsync(Path.Combine(folder, "missing.wgmacro"),
                Path.Combine(folder, "assets"));
            Assert.False(read.Ok);
            Assert.Equal(PackageProblem.NotFound, read.Problem);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task A_library_package_is_reported_as_a_project_kind_mismatch()
    {
        var folder = Folder();
        try
        {
            var path = Path.Combine(folder, "library.wgmacro");
            PublicMacroLibrary.Save(path,
                [new PublicMacroEntry("public-id", 1, new MacroItem { Name = "shared" })], "test");

            var read = await _store.ReadAsync(path, Path.Combine(folder, "assets"));

            Assert.False(read.Ok);
            Assert.Equal(PackageProblem.LibraryNotProject, read.Problem);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task A_broken_archive_is_reported_as_an_invalid_format()
    {
        var folder = Folder();
        try
        {
            var path = Path.Combine(folder, "broken.wgmacro");
            await File.WriteAllTextAsync(path, "not a zip");
            var read = await _store.ReadAsync(path, Path.Combine(folder, "assets"));
            Assert.False(read.Ok);
            Assert.Equal(PackageProblem.InvalidFormat, read.Problem);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task A_write_the_file_system_refuses_does_not_damage_the_existing_package()
    {
        var folder = Folder();
        try
        {
            var path = Path.Combine(folder, "good.wgmacro");
            var first = await _store.WriteAsync(new PackageWriteRequest(
                path, [new MacroItem { Name = "good" }], new ProjectTriggers(), [], "test", null));
            Assert.True(first.Ok, first.Message);
            var before = await File.ReadAllBytesAsync(path);

            // A NUL in a Windows path is refused before the temporary file can replace the target.
            var invalid = await _store.WriteAsync(new PackageWriteRequest(
                path.Insert(path.Length - 8, "\0"), [new MacroItem { Name = "bad" }],
                new ProjectTriggers(), [], "test", null));
            Assert.False(invalid.Ok);
            Assert.Equal(PackageProblem.WriteFailed, invalid.Problem);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-package-store-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
