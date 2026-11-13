using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Tests;

// The pictures a package carries, wherever it was written.
public partial class MacroPackageFormatTests
{
    /// <summary>
    /// 包写的图片有两种写法：一种是绝对路径（编辑器里刚截的图就是），一种是相对包自己的
    /// `assets/x.png`（从别的包读进来之后）。两种都要装进 zip —— 只把文件留在旁边的
    /// `X.assets/` 里，包就带不走它。
    /// </summary>
    [Fact]
    public void A_package_carries_the_pictures_it_refers_to()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var assets = Path.Combine(folder, "proj.assets");
            Directory.CreateDirectory(assets);
            var bytes = new byte[] { 137, 80, 78, 71, 9, 9 };
            File.WriteAllBytes(Path.Combine(assets, "ok.png"), bytes);

            var relative = Picture("assets/ok.png");
            var absolute = Picture(Path.Combine(assets, "ok.png"));
            var path = Path.Combine(folder, "proj.wgmacro");

            MacroPackage.Save(path, [relative, absolute],
                ProjectTriggers.FromMacros([relative, absolute]), [], "test");

            using var archive = ZipFile.OpenRead(path);
            var entry = Assert.Single(archive.Entries,
                item => item.FullName == "assets/ok.png");
            using var read = entry.Open();
            using var buffer = new MemoryStream();
            read.CopyTo(buffer);
            Assert.Equal(bytes, buffer.ToArray());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// 导出到别处时，图片还在项目那个目录里：不把那个目录交过去，`assets/x.png` 就解析不到，
    /// 图被悄悄漏掉，包到另一台机器上只认得一个不存在的文件。
    /// </summary>
    [Fact]
    public void A_package_written_somewhere_else_still_carries_the_pictures()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        var project = Path.Combine(folder, "project");
        var exported = Path.Combine(folder, "export");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(exported);
        try
        {
            var projectAssets = Path.Combine(project, "proj.assets");
            Directory.CreateDirectory(projectAssets);
            var bytes = new byte[] { 137, 80, 78, 71, 7, 7 };
            File.WriteAllBytes(Path.Combine(projectAssets, "ok.png"), bytes);

            var macro = Picture("assets/ok.png");
            var path = Path.Combine(exported, "out.wgmacro");

            MacroPackage.Save(path, [macro], ProjectTriggers.FromMacros([macro]), [], "test",
                projectAssets);

            using (var archive = ZipFile.OpenRead(path))
            {
                Assert.Contains(archive.Entries, item => item.FullName == "assets/ok.png");
            }

            // 读回来之后，这一格指的必须是解出来的那个文件，而且内容一样。
            var loaded = MacroPackage.Load(path, Path.Combine(exported, "out.assets"));
            var value = loaded.Macros[0].Steps[0].Parameters[0].Rows[0].Text("image");
            var file = ImageAssets.Resolve(value, Path.Combine(exported, "out.assets"));
            Assert.NotNull(file);
            Assert.Equal(bytes, File.ReadAllBytes(file!));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>A macro whose one step looks for the picture at <paramref name="picture"/>.</summary>
    private static MacroItem Picture(string picture)
    {
        var macro = new MacroItem { Name = "look-" + Guid.NewGuid().ToString("N")[..6] };
        macro.Steps.Add(new MacroStep
        {
            Type = "vision.findImage",
            Parameters =
            [
                new StepParameter
                {
                    Name = "images",
                    Kind = ActionParameterKind.Images,
                    Rows = [StepParameterRow.Of("image", Formula.Text(picture))],
                },
            ],
        });
        return macro;
    }

    [Fact]
    public void A_menu_image_is_carried_in_the_package_assets()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var icon = Path.Combine(folder, "menu.png");
            File.WriteAllBytes(icon, [137, 80, 78, 71]);
            var macro = new MacroItem { Name = "menu" };
            var triggers = new ProjectTriggers();
            triggers.Entries.Add(new TriggerEntry
            {
                Kind = TriggerKind.Menu,
                MacroName = macro.Name,
                MenuPageId = triggers.CommonMenuPages()[0].Id,
                MenuRow = 0,
                MenuColumn = 0,
                MenuIcon = new MenuIconReference(MenuIconKind.Image, icon),
            });
            var path = Path.Combine(folder, "menu.wgmacro");

            MacroPackage.Save(path, [macro], triggers, [], "test");

            using (var archive = ZipFile.OpenRead(path))
            {
                Assert.Contains(archive.Entries,
                    entry => entry.FullName.StartsWith("assets/", StringComparison.Ordinal)
                        && entry.Name == "menu.png");
            }

            var loaded = MacroPackage.Load(path);
            var loadedIcon = loaded.Triggers.Entries.Single(entry =>
                entry.Kind == TriggerKind.Menu).MenuIcon;
            Assert.Equal(MenuIconKind.Image, loadedIcon?.Kind);
            Assert.True(File.Exists(loadedIcon!.Value));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
