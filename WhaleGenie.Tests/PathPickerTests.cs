using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The button beside a path field. A path is typed like any other value, but it is also the one
/// value this machine can hand over from a list of what is actually on it, and the list is a
/// different one depending on which way the file is going.
/// </summary>
public class PathPickerTests
{
    private static AddActionViewModel Open(string key, out AddActionWindow window)
    {
        window = new AddActionWindow(null, ActionCatalog.Definitions, [], []);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction(key);
        Dispatcher.UIThread.RunJobs();
        return viewModel;
    }

    [Fact]
    public void Every_kind_of_path_field_says_which_dialog_it_opens()
    {
        Ui.Run(() =>
        {
            var reading = Open("file.readText", out var readWindow);
            var writing = Open("file.writeText", out var writeWindow);
            var folder = Open("file.createFolder", out var folderWindow);

            Assert.Equal(Strings.Get("Add.BrowseOpen"),
                reading.Parameters.First(p => p.Definition.Name == "path").PathButton);
            Assert.Equal(Strings.Get("Add.BrowseSave"),
                writing.Parameters.First(p => p.Definition.Name == "path").PathButton);
            Assert.Equal(Strings.Get("Add.BrowseFolder"),
                folder.Parameters.First(p => p.Definition.Name == "path").PathButton);

            // A value that is not a path gets no button at all.
            Assert.DoesNotContain(reading.Parameters, p => p.Definition.Name == "encoding" && p.IsPath);

            readWindow.Close();
            writeWindow.Close();
            folderWindow.Close();
        });
    }

    [Fact]
    public void The_button_is_on_the_line_beside_the_field()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("file.readText", out var window);
            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Select(button => button.Content as string)
                .ToList();

            Assert.Contains(Strings.Get("Add.BrowseOpen"), buttons);
            Assert.NotNull(viewModel);

            window.Close();
        });
    }

    [Fact]
    public void A_program_and_a_working_folder_are_picked_the_same_way()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("process.start", out var window);
            var program = viewModel.Parameters.First(p => p.Definition.Name == "file");
            var folder = viewModel.Parameters.First(p => p.Definition.Name == "workingDirectory");

            Assert.Equal(Strings.Get("Add.BrowseOpen"), program.PathButton);
            Assert.Contains("*.exe", program.Definition.PathFilter, StringComparison.Ordinal);
            Assert.Equal(Strings.Get("Add.BrowseFolder"), folder.PathButton);

            window.Close();
        });
    }

    [Fact]
    public void A_file_inside_the_macros_folder_is_stored_by_its_place_in_it()
    {
        var inside = Path.Combine(MacroPaths.Folder, "data", "report.csv");

        Assert.Equal(Path.Combine("data", "report.csv"), MacroPaths.ForMacro(inside));
        Assert.Equal(@"D:\somewhere else\report.csv", MacroPaths.ForMacro(@"D:\somewhere else\report.csv"));
        Assert.Equal(string.Empty, MacroPaths.ForMacro("   "));

        // Above the folders a relative path only reads as dots, so the full one is kept.
        var above = Path.Combine(MacroPaths.Folder, "..", "elsewhere", "report.csv");
        Assert.True(Path.IsPathRooted(MacroPaths.ForMacro(above)));
    }
}
