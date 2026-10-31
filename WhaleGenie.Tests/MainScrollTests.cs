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

            // Thirty macros nobody saved make this a project with unsaved work, so closing asks.
            // "Discard" is the answer this check is after: what it looks at is the scroll, and the
            // window has to really go — otherwise the draft it keeps in the recovery snapshot
            // outlives the check and turns up in another check's project.
            window.AskToSaveProject = () => Task.FromResult(ConfirmChoice.Secondary);
            Ui.CloseAndAnswer(window);
            return true;
        });
    }
}
