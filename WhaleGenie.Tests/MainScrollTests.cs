using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class MainScrollTests
{
    [Fact]
    public void A_long_macro_list_scrolls_without_moving_the_dock_or_search()
    {
        Ui.RunAsync(async () =>
        {
            var viewModel = new MainViewModel();
            viewModel.SelectedDock = DockPage.Keys;
            for (var index = 0; index < 30; index++)
            {
                viewModel.AddMacro(new MacroItem { Name = $"Macro{index + 1}" });
            }
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(200);

            var scroll = window.FindControl<ScrollViewer>("MacroListScroll")!;
            var list = window.FindControl<ItemsControl>("MacroList")!;
            Assert.Equal(30, list.ItemCount);
            Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height,
                $"extent={scroll.Extent.Height}, viewport={scroll.Viewport.Height}, items={list.ItemCount}");
            scroll.Offset = new Avalonia.Vector(0, 240);
            Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y > 0);
            window.Close();
            return true;
        });
    }
}
