using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The size the dialog opens at, and where it stands against the window that asked.
public partial class ActionPageTests
{

    /// <summary>
    /// A step always has something to show: an action whose own fields are all on one page cannot
    /// open on a page that is not there.
    /// </summary>
    [Fact]
    public void A_step_always_opens_on_a_page_that_is_there()
    {
        var empty = new List<string>();

        Ui.Run(() =>
        {
            foreach (var action in ActionCatalog.RunnableActions)
            {
                var viewModel = Open(action.Key);
                if (viewModel.Page is not { IsPresent: true })
                {
                    empty.Add(action.Key);
                }
            }
        });

        Assert.Empty(empty);
    }

    /// <summary>
    /// The dialog opens at the size it wants when the desktop has the room for it, and at what is
    /// left of the desktop when it has not — never off the edge, and never under the size the four
    /// pages stop being readable in.
    /// </summary>
    [Theory]
    // 1080p：桌面有的是地方，它也只取最窄那一档——这是一张表，不是一块画布。
    [InlineData(1920, 1040, 800, 800)]
    // 1366×768 的笔记本：宽度还是那一档，高度按桌面剩下的来。
    [InlineData(1366, 768, 800, 688)]
    // 连最窄都摆不下的桌面，宽度仍守在最窄一档。
    [InlineData(800, 600, 800, 560)]
    public void The_dialog_opens_at_a_size_the_desktop_has_room_for(
        double roomWidth, double roomHeight, double width, double height)
    {
        Assert.Equal((width, height), DialogSize.Fit(roomWidth, roomHeight));
    }

    /// <summary>
    /// A step is written with the window it is going into in sight, so the dialog stands beside it
    /// whenever the desktop has room on either side — on the roomier one, level with the top of the
    /// window, and no wider than that room.
    /// </summary>
    [Theory]
    // A wide desktop with the editor in the middle: the form stands on its right, at the narrow size.
    [InlineData(0, 0, 2560, 1040, 490, 100, 940, 780, 1430, 100, 800)]
    // The editor pushed to the right: the room is now on its left.
    [InlineData(0, 0, 2560, 1040, 1500, 100, 940, 780, 700, 100, 800)]
    // The same room on both sides: it takes the right.
    [InlineData(0, 0, 2600, 1040, 830, 100, 940, 780, 1770, 100, 800)]
    // Room on one side, but not the width the form would like: it takes what there is.
    [InlineData(0, 0, 1920, 1040, 0, 0, 940, 780, 940, 0, 800)]
    // The editor sits low: the form is pulled up to stay on the desktop.
    [InlineData(0, 0, 2560, 1040, 400, 700, 940, 780, 1340, 240, 800)]
    // A second monitor beside the first: the room is measured from that desktop's own edge.
    [InlineData(1920, 0, 1920, 1040, 2000, 100, 940, 780, 2940, 100, 800)]
    public void The_dialog_stands_beside_the_window_it_was_opened_from_where_there_is_room(
        double desktopX, double desktopY, double desktopWidth, double desktopHeight,
        double windowX, double windowY, double windowWidth, double windowHeight,
        double x, double y, double width)
    {
        var beside = DialogSize.Beside(
            (windowX, windowY, windowWidth, windowHeight),
            (desktopX, desktopY, desktopWidth, desktopHeight),
            height: 800);

        Assert.Equal(x, beside!.Value.X);
        Assert.Equal(y, beside.Value.Y);
        Assert.Equal(width, beside.Value.Width);
    }

    /// <summary>
    /// With no room on either side there is nowhere to stand, and the dialog stays over the window
    /// it was opened from: two windows about a thousand pixels wide do not fit side by side on a
    /// 1080p desktop while the editor sits in the middle of it.
    /// </summary>
    [Fact]
    public void The_dialog_stays_over_the_window_when_neither_side_has_room()
    {
        Assert.Null(DialogSize.Beside((490, 100, 940, 780), (0, 0, 1920, 1040), height: 800));

        // And the 1366-wide laptop, where what is left over is narrower still.
        Assert.Null(DialogSize.Beside((213, 100, 940, 780), (0, 0, 1366, 768), height: 688));
    }

    /// <summary>
    /// The dialog stands beside the window it was opened from rather than over it, so the steps
    /// already written stay in sight while the next one is filled in.
    /// </summary>
    [Fact]
    public void The_dialog_opens_beside_the_window_that_asked_for_it()
    {
        Ui.Run(() =>
        {
            var owner = new Window { Width = 940, Height = 780 };
            owner.Show();
            Dispatcher.UIThread.RunJobs();

            var dialog = new ActionEditorWindow(null, ActionCatalog.PlaceableActions);
            dialog.ShowOver(owner);
            Dispatcher.UIThread.RunJobs();

            var screen = dialog.Screens.ScreenFromWindow(owner) ?? dialog.Screens.Primary!;
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            var room = (screen.WorkingArea.Width / scale) - owner.Bounds.Width;

            // Standing beside the window takes a desktop with room on that side; one without is
            // what the check above covers.
            Assert.True(room >= DialogSize.SmallestBesideWidth,
                "the desktop this runs on has no room beside the window");

            // It starts where the window ends, on the same line, and takes what is left over.
            Assert.Equal(owner.Bounds.Width, dialog.Position.X);
            Assert.Equal(owner.Position.Y, dialog.Position.Y);
            Assert.Equal(Math.Min(DialogSize.WantedWidth, room), dialog.Width);

            dialog.Close();
            owner.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    /// <summary>
    /// The tabs across the top of the form are the pages, and picking one is a selection rather than
    /// a click: the strip and `Page` are the same choice said twice. A page with nothing to show is
    /// not a tab at all — an empty one would look like a page that failed to load.
    /// </summary>
    [Fact]
    public void The_strip_of_tabs_is_the_pages_and_one_of_them_is_chosen()
    {
        Ui.Run(() =>
        {
            var view = new ActionEditorView { DataContext = Open("vision.findImage") };
            var window = new Window { Width = 1024, Height = 800, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                var viewModel = (AddActionViewModel)view.DataContext!;
                var strip = view.GetVisualDescendants().OfType<TabStrip>().Single();
                var shown = strip.GetVisualDescendants().OfType<TabStripItem>()
                    .Count(item => item.IsEffectivelyVisible);

                Assert.Equal(viewModel.Pages.Count(page => page.IsPresent), shown);
                Assert.Same(viewModel.Page, strip.SelectedItem);

                // Choosing another page moves the strip, and choosing on the strip moves the page.
                var other = viewModel.Pages.First(page =>
                    page.IsPresent && !ReferenceEquals(page, viewModel.Page));
                viewModel.Select(other);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(other, strip.SelectedItem);

                var first = viewModel.Pages.First();
                strip.SelectedItem = first;
                Dispatcher.UIThread.RunJobs();
                Assert.Same(first, viewModel.Page);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
