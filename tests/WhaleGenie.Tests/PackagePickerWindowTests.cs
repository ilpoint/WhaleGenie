using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The package picker builds a line for every item, including the no-item the list asks for while
/// it is being emptied. That empty line used to be a NullReferenceException inside the template,
/// which took the whole program down when a project was opened.
/// </summary>
public class PackagePickerWindowTests
{
    [Fact]
    public void An_empty_list_item_draws_an_empty_line()
    {
        var line = PackagePickerWindow.Line(null);

        Assert.Equal(string.Empty, line.Text);
    }

    [Fact]
    public void A_folder_is_drawn_with_its_separator()
    {
        var line = PackagePickerWindow.Line(
            new PackagePickerWindow.PackageEntry(@"C:\projects\demo", "demo", true));

        Assert.Equal("demo" + System.IO.Path.DirectorySeparatorChar, line.Text);
    }

    [Fact]
    public void A_package_is_drawn_by_its_file_name()
    {
        var line = PackagePickerWindow.Line(
            new PackagePickerWindow.PackageEntry(@"C:\projects\demo.wgmacro", "demo.wgmacro", false));

        Assert.Equal("demo.wgmacro", line.Text);
    }
}
