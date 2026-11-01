using System.IO.Compression;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Pointing an action at a picture or a window. A picture has to end up somewhere the macro
/// package can carry it, and a window has to come back as a title the engine will match.
/// </summary>
public class PickerTests
{
    [Fact]
    public void Every_picture_parameter_is_edited_with_the_picture_picker()
    {
        var kinds = Ui.Run(() => ActionCatalog.Definitions
            .SelectMany(definition => definition.Parameters)
            .Where(parameter => parameter.Name is "images" or "imageSource")
            .Select(parameter => parameter.Kind)
            .Distinct()
            .ToList());

        // The actions that look for a reference picture on screen list several of them, so a step
        // covers a thing that is drawn differently from one screen to the next; the one action that
        // puts a picture on the clipboard has a single picture, and nothing to try a second one for.
        Assert.Equal(
            [ActionParameterKind.Image, ActionParameterKind.Images],
            kinds.OrderBy(parameter => parameter).ToList());
    }

    [Fact]
    public void Window_parameters_are_edited_with_the_window_picker()
    {
        var parameters = Ui.Run(() => ActionCatalog.Definitions
            .SelectMany(definition => definition.Parameters)
            .Where(parameter => parameter.Name == "window")
            .ToList());

        Assert.NotEmpty(parameters);
        Assert.All(parameters, parameter =>
            Assert.Equal(ActionParameterKind.WindowRule, parameter.Kind));
        Assert.Contains(parameters, parameter => parameter.Name == "window");
    }

    [Fact]
    public void The_macro_package_carries_the_programs_own_extension()
    {
        // One name for the program means one extension, and the save dialog builds its filter from
        // this rather than repeating it, so a rename cannot leave the dialog offering the old one.
        Assert.Equal(".wgmacro", MacroPackage.Extension);
    }

    /// <summary>
    /// 截图落在**程序自己的数据目录**里、这个包专属的那个文件夹，不落在包旁边：一个项目文件夹里
    /// 每个 .wgmacro 旁边再挂一个 .assets，读起来就全是目录；而且包放在只读位置时也没处解包。
    /// 同一个包再来一次还是同一个文件夹，两个同名的包互不覆盖。
    /// </summary>
    [Fact]
    public void A_taken_picture_lands_in_the_folder_that_package_owns()
    {
        var package = Path.Combine(TempFolder(), "macros", "demo.wgmacro");
        var folder = ImageAssets.FolderFor(package);

        Assert.Contains(Path.Combine("WhaleGenie", "projects"), folder, StringComparison.Ordinal);
        Assert.EndsWith("assets", folder, StringComparison.Ordinal);
        Assert.Equal(folder, ImageAssets.FolderFor(package));

        Assert.NotEqual(folder,
            ImageAssets.FolderFor(Path.Combine(TempFolder(), "other", "demo.wgmacro")));

        // 还没存过的项目没有包可归属，留在程序自己的图片目录里。
        Assert.EndsWith(Path.Combine("WhaleGenie", "images"), ImageAssets.FolderFor(null));
        Assert.EndsWith(Path.Combine("WhaleGenie", "images"), ImageAssets.FolderFor("   "));
    }

    [Fact]
    public void A_picture_value_is_looked_up_beside_the_package()
    {
        var folder = TempFolder();
        try
        {
            var picture = Path.Combine(folder, "ok.png");
            File.WriteAllBytes(picture, [1, 2, 3, 4]);

            Assert.Equal(picture, ImageAssets.Resolve("ok.png", folder));
            Assert.Equal(picture, ImageAssets.Resolve(picture, folder));

            Assert.Null(ImageAssets.Resolve("missing.png", folder));
            Assert.Null(ImageAssets.Resolve(string.Empty, folder));
            Assert.Null(ImageAssets.Resolve(null, folder));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_picture_field_previews_only_what_is_really_there()
    {
        Ui.Run(() =>
        {
            var parameter = Picture();

            // Nothing is shown until the field names a file that exists, so a half typed path
            // never draws a broken preview.
            Assert.True(parameter.IsImageList);
            Assert.False(parameter.HasPictures);

            parameter.AddPicture();
            Assert.True(parameter.HasPictures);
            Assert.False(parameter.Pictures[0].HasThumbnail);

            parameter.Pictures[0].Text = "no-such-picture.png";
            Assert.False(parameter.Pictures[0].HasThumbnail);

            parameter.Pictures[0].Text = string.Empty;
            Assert.False(parameter.Pictures[0].HasThumbnail);
        });
    }

    [Fact]
    public void The_add_action_dialog_knows_where_pictures_go()
    {
        Ui.Run(() =>
        {
            var folder = @"C:\macros\demo.assets";
            var dialog = new ActionEditorWindow(null, ActionCatalog.RunnableActions, null, null,
                "vision.findImage", folder);
            try
            {
                var model = Assert.IsType<AddActionViewModel>(dialog.DataContext);

                Assert.Equal(folder, model.AssetFolder);
                Assert.All(model.Parameters.Where(parameter => parameter.IsImage || parameter.IsImageList),
                    parameter => Assert.Equal(folder, parameter.AssetFolder));

                // A picture added to the list after the dialog opened is looked up beside the
                // package too, not in whatever folder the program happens to start in.
                var pictures = model.Parameters.First(parameter => parameter.IsImageList);
                pictures.AddPicture();
                Assert.Equal(folder, pictures.Pictures[0].AssetFolder);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void The_filter_narrows_the_open_windows_down()
    {
        var model = new WindowPickerViewModel(
        [
            new WindowEntry(1, "Untitled - Notepad", "notepad", "Notepad", "800 × 600 · normal"),
            new WindowEntry(2, "Report.docx - Word", "WINWORD", "OpusApp", "1024 × 768 · maximized"),
        ]);

        Assert.Equal(2, model.Windows.Count);
        Assert.Equal(1, model.Selected!.Handle);

        // The program behind the window narrows it down just as the title does.
        model.Filter = "word";
        Assert.Equal(2, model.Selected!.Handle);

        model.Filter = "notepad";
        Assert.Equal(1, model.Selected!.Handle);

        model.Filter = "nothing at all";
        Assert.Empty(model.Windows);
        Assert.Null(model.Selected);
        Assert.False(model.HasWindows);
        Assert.Equal(Strings.Get("WindowPicker.None"), model.EmptyMessage);
    }

    [Fact]
    public void The_dialog_opens_the_picker_for_the_part_the_step_compares_with()
    {
        Ui.Run(() =>
        {
            var viewModel = new AddActionViewModel(ActionCatalog.Definitions, [], []);
            viewModel.SelectAction("window.exists");
            var text = viewModel.Parameters.First(parameter => parameter.Definition.Name == "window");

            // A step that says nothing about it reads titles, and the picker takes a title.
            Assert.Equal(WindowMatch.Title, viewModel.WindowMatchFor(text));

            text.ApplyWindowRule(new WindowRule(Process: "notepad", How: WindowHow.Process));
            Assert.Equal(WindowMatch.Process, viewModel.WindowMatchFor(text));

            text.ApplyWindowRule(new WindowRule(ClassName: "Notepad", How: WindowHow.Class));
            Assert.Equal(WindowMatch.ClassName, viewModel.WindowMatchFor(text));
        });
    }

    [Fact]
    public void Filling_in_a_picked_title_puts_the_rule_back_on_the_title()
    {
        Ui.Run(() =>
        {
            var viewModel = new AddActionViewModel(ActionCatalog.Definitions, [], []);
            viewModel.SelectAction("uia.click");
            var window = viewModel.Parameters.First(parameter => parameter.Definition.Name == "window");

            window.ApplyWindowRule(new WindowRule(Process: "notepad", How: WindowHow.Process));
            Assert.Equal(WindowMatch.Process, viewModel.WindowMatchFor(window));

            // What the picker reads is a title, so the rule goes back to holding the text up
            // against the title. Left as it was, it would compare a title with a process name and
            // recognise no window at all.
            window.ClearWindowRule();
            Assert.True(viewModel.FillWindowFromPick("window", "Untitled - Notepad"));
            Assert.Equal(new WindowRule(Title: "Untitled - Notepad").ToJson().ToJsonString(),
                window.Text);
            Assert.Equal(WindowMatch.Title, viewModel.WindowMatchFor(window));

            // A field that already says which window this is keeps what it says: nothing was picked
            // into it, so nothing about it changes.
            window.ApplyWindowRule(new WindowRule(Process: "notepad", How: WindowHow.Process));
            Assert.False(viewModel.FillWindowFromPick("window", "Report.docx - Word"));
            Assert.Equal(WindowMatch.Process, viewModel.WindowMatchFor(window));
        });
    }

    [Fact]
    public void The_picker_takes_the_part_of_the_window_it_was_opened_for()
    {
        Ui.Run(() =>
        {
            IReadOnlyList<WindowEntry> open =
                [new WindowEntry(1, "Untitled - Notepad", "notepad", "Notepad", "800 × 600 · normal")];

            // The field the picker is opened from decides which part is taken, so what comes back
            // can be written straight into it and compared the same way at run time.
            foreach (var (match, wanted) in new (WindowMatch, string)[]
                     {
                         (WindowMatch.Title, "Untitled - Notepad"),
                         (WindowMatch.Process, "notepad"),
                         (WindowMatch.ClassName, "Notepad"),
                     })
            {
                var window = new WindowPickerWindow(new WindowPickerViewModel(open, match));
                window.Show();
                Dispatcher.UIThread.RunJobs();

                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(wanted, window.Chosen);
            }
        });
    }

    [Fact]
    public void An_empty_desktop_says_it_has_nothing_to_offer()
    {
        var model = new WindowPickerViewModel([]);

        Assert.False(model.HasWindows);
        Assert.Equal(Strings.Get("WindowPicker.Empty"), model.EmptyMessage);
    }

    [Fact]
    public void Enter_takes_the_window_that_is_selected()
    {
        Ui.Run(() =>
        {
            var model = new WindowPickerViewModel(
                [new WindowEntry(1, "Untitled - Notepad", "notepad", "Notepad", "800 × 600 · normal")]);
            var window = new WindowPickerWindow(model);

            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Untitled - Notepad", window.Chosen);
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void Escape_leaves_without_taking_a_window()
    {
        Ui.Run(() =>
        {
            var model = new WindowPickerViewModel(
                [new WindowEntry(1, "Untitled - Notepad", "notepad", "Notepad", "800 × 600 · normal")]);
            var window = new WindowPickerWindow(model);

            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(window.Chosen);
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void A_package_carries_its_pictures_and_hands_them_back()
    {
        var folder = TempFolder();
        try
        {
            var package = Path.Combine(folder, "demo.wgmacro");
            var assets = ImageAssets.FolderFor(package);
            Directory.CreateDirectory(assets);

            var picture = Path.Combine(assets, "ok.png");
            File.WriteAllBytes(picture, [137, 80, 78, 71, 13, 10, 26, 10]);

            var macro = new MacroItem { Name = "Looks for a picture" };
            macro.Steps.Add(new MacroStep
            {
                Type = "vision.findImage",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "images",
                        Kind = ActionParameterKind.Images,
                        Rows =
                        [
                            StepParameterRow.Of("image", picture),
                        ],
                    },
                ],
            });

            MacroPackage.Save(package, [macro], [], "test");

            // The picture travels inside the package, under the folder the format reserves for it.
            using (var archive = ZipFile.OpenRead(package))
            {
                Assert.Contains("assets/ok.png", archive.Entries.Select(entry => entry.FullName));
            }

            // Reading it back unpacks the picture again and points the step at the file.
            Directory.Delete(assets, true);
            var loaded = MacroPackage.Load(package);
            var step = Assert.Single(Assert.Single(loaded.Macros).Steps);
            var image = Assert.Single(step.Parameters, parameter => parameter.Name == "images");

            Assert.Equal(picture, ImageAssets.Resolve(image.Rows[0].Text("image"), assets));
            Assert.True(File.Exists(picture));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>The image parameter of the action that looks for a picture on screen.</summary>
    private static StepParameterViewModel Picture()
    {
        var definition = ActionCatalog.Find("vision.findImage")
            ?? throw new InvalidOperationException("vision.findImage is missing from the catalogue.");

        return new StepParameterViewModel(
            definition.Parameters.First(parameter => parameter.Name == "images"));
    }

    /// <summary>A folder of its own under the temporary directory, removed by the caller.</summary>
    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return Path.GetFullPath(folder);
    }
}
