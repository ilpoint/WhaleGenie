using System;
using System.IO;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

public class PublicMacroLibraryTests
{
    [Fact]
    public void A_public_macro_round_trips_with_its_version_and_submacros()
    {
        var folder = TemporaryFolder();
        try
        {
            var path = Path.Combine(folder, "public-macros.wgmacro");
            var child = new MacroItem
            {
                Name = "helper",
                Description = "Child",
                Origin = new MacroOrigin("source-child", "helper", 4),
            };
            child.Steps.Add(Step("control.log"));
            var macro = new MacroItem
            {
                Name = "shared",
                Description = "Shared work",
            };
            macro.Parameters.Add(new MacroParameter { Name = "count", Required = true });
            macro.Results.Add(new MacroResult { Name = "answer" });
            macro.Steps.Add(Step("control.runMacro",
                new StepParameter
                {
                    Name = "macro",
                    Kind = ActionParameterKind.Macro,
                    Value = "helper",
                }));
            macro.SubMacros.Add(child);

            PublicMacroLibrary.Save(path,
                [new PublicMacroEntry("public-id", 7, macro)], "test");
            var loaded = Assert.Single(PublicMacroLibrary.Load(path));

            Assert.Equal("public-id", loaded.Id);
            Assert.Equal(7, loaded.Version);
            Assert.Equal("shared", loaded.Macro.Name);
            Assert.Equal("Shared work", loaded.Macro.Description);
            Assert.Single(loaded.Macro.Parameters);
            Assert.Single(loaded.Macro.Results);
            var copiedChild = Assert.Single(loaded.Macro.SubMacros);
            Assert.Equal("source-child", copiedChild.Origin!.Id);
            Assert.Equal(4, copiedChild.Origin.Version);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_copy_records_the_exact_public_version()
    {
        var folder = TemporaryFolder();
        try
        {
            var source = new MacroItem { Name = "shared", Description = "From library" };
            var copy = PublicMacroLibrary.CopyToProject(
                new PublicMacroEntry("public-id", 3, source),
                Path.Combine(folder, "project.assets"));

            Assert.Equal("shared", copy.Name);
            Assert.Equal("From library", copy.Description);
            Assert.Equal(new MacroOrigin("public-id", "shared", 3), copy.Origin);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_missing_library_is_empty_but_a_broken_one_is_not_overwritten()
    {
        var folder = TemporaryFolder();
        try
        {
            var path = Path.Combine(folder, "public-macros.wgmacro");
            Assert.Empty(PublicMacroLibrary.Load(path));

            File.WriteAllText(path, "not a package");
            var before = File.ReadAllBytes(path);
            Assert.ThrowsAny<Exception>(() => PublicMacroLibrary.Load(path));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Copying_the_definition_copies_the_pictures_the_steps_use()
    {
        var folder = TemporaryFolder();
        try
        {
            var sourceAssets = Path.Combine(folder, "library.assets");
            Directory.CreateDirectory(sourceAssets);
            File.WriteAllBytes(Path.Combine(sourceAssets, "button.png"), [1, 2, 3, 4]);
            var macro = new MacroItem { Name = "shared" };
            macro.Steps.Add(Step("vision.findImage",
                new StepParameter
                {
                    Name = "image",
                    Kind = ActionParameterKind.Image,
                    Value = "button.png",
                }));
            var projectAssets = Path.Combine(folder, "project.assets");

            var copy = PublicMacroLibrary.CopyToProject(
                new PublicMacroEntry("public-id", 1, macro), projectAssets, sourceAssets);

            var image = Assert.Single(copy.Steps).Parameters
                .Single(parameter => parameter.Name == "image").Value;
            Assert.True(Path.IsPathRooted(image));
            Assert.True(File.Exists(image));
            Assert.StartsWith(projectAssets, image, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    private static string TemporaryFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
