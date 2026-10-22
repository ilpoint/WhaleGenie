using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class MenuIconPickerTests
{
    [Fact]
    public void The_icon_picker_builds_one_bounded_page_at_a_time()
    {
        Ui.Run(() =>
        {
            var window = new MenuIconPickerWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(MenuIconCatalog.Options.Count, window.MatchCountForCheck);
            Assert.Equal(MenuIconPickerWindow.MaximumIconsPerPage,
                window.PageOptionsForCheck.Count);
            Assert.Equal(
                (MenuIconCatalog.Options.Count + MenuIconPickerWindow.MaximumIconsPerPage - 1)
                / MenuIconPickerWindow.MaximumIconsPerPage,
                window.PageCountForCheck);
            Assert.Contains(window.GetVisualDescendants(),
                visual => visual is VirtualizingStackPanel);
            var realized = window.GetVisualDescendants().OfType<Button>()
                .Count(button => button.Classes.Contains("MenuIconChoice"));
            Assert.InRange(realized, 1, MenuIconPickerWindow.MaximumIconsPerPage);

            var list = window.GetVisualDescendants().OfType<ListBox>()
                .Single(control => control.Name == "MenuIconList");
            var next = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "MenuIconNextPage");
            next.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, window.PageForCheck);
            Assert.Equal(MenuIconCatalog.Options[MenuIconPickerWindow.MaximumIconsPerPage].Key,
                window.PageOptionsForCheck[0].Key);
            Assert.True(list.ItemCount <= MenuIconPickerWindow.MaximumIconsPerPage
                / MenuIconPickerWindow.IconsPerRow);
            window.Close();
        });
    }

    [Fact]
    public void Searching_returns_to_the_first_bounded_result_page()
    {
        Ui.Run(() =>
        {
            var window = new MenuIconPickerWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var search = window.GetVisualDescendants().OfType<TextBox>().Single();

            search.Text = "AlarmClock";
            window.ApplySearchForCheck();

            Assert.Equal(1, window.PageForCheck);
            Assert.InRange(window.MatchCountForCheck, 1,
                MenuIconPickerWindow.MaximumIconsPerPage);
            Assert.All(window.PageOptionsForCheck,
                option => Assert.Contains("AlarmClock", option.Label,
                    StringComparison.OrdinalIgnoreCase));
            window.Close();
        });
    }
}
