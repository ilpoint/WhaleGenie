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


namespace WhaleGenie.Domain.Tests;

// What a package refuses, and the child macros it carries.
public partial class MacroPackageFormatTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(9)]
    public void A_package_from_an_older_format_is_refused(int version)
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "old.wgmacro");
            MacroPackage.Save(path, [new MacroItem { Name = "old" }], new ProjectTriggers(), [], "test");
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
    public void Child_macros_and_their_public_origin_survive_the_package()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "children.wgmacro");
            var parent = new MacroItem
            {
                Name = "parent",
                Description = "Parent description",
            };
            var child = new MacroItem
            {
                Name = "child",
                Description = "Child description",
                Origin = new MacroOrigin("public-id", "Public child", 6),
            };
            child.Steps.Add(new MacroStep { Type = "control.log" });
            parent.SubMacros.Add(child);

            MacroPackage.Save(path, [parent], new ProjectTriggers(), [], "test");
            var loaded = Assert.Single(MacroPackage.Load(path).Macros);
            var loadedChild = Assert.Single(loaded.SubMacros);

            Assert.Equal("Parent description", loaded.Description);
            Assert.Equal("Child description", loadedChild.Description);
            Assert.Equal("public-id", loadedChild.Origin!.Id);
            Assert.Equal(6, loadedChild.Origin.Version);
            Assert.Equal("control.log", Assert.Single(loadedChild.Steps).Type);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
