using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Where the marks of a look land on the picture it came from. A mark is in screen pixels and the
/// picture has a corner of its own, so the window has to subtract one and scale by the zoom — and
/// a hit found by a colour is one pixel of a whole screen, which has to stay visible anyway.
/// </summary>
public class LookWindowTests
{
    /// <summary>
    /// The dialog carries one button that runs the step being written, so a step can be tried
    /// without closing the macro and going into a run. It is there for the steps a run can do — a
    /// condition is not a step of the macro, so there is nothing to run — and it waits until the
    /// step is complete, because half a step is not the step the user means yet.
    /// </summary>
    [Fact]
    public void The_dialog_runs_the_step_it_is_editing()
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("match"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            Assert.False(viewModel.ShowsRun);

            viewModel.SelectAction("input.keyPress");
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.ShowsRun);

            // The key is still empty, which is a step that would send nothing.
            Assert.False(viewModel.CanRun);
            Assert.False(FindRunButton(window).IsEnabled);

            viewModel.Parameters.First(parameter => parameter.Definition.Name == "key").Text = "F5";
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.CanRun);
            Assert.True(FindRunButton(window).IsEnabled);
            Assert.Equal(Strings.Get("Add.RunStep"), FindRunButton(window).Content);

            // A condition is asked about rather than done, so there is nothing to run.
            viewModel.SelectAction("condition.imageExists");
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.ShowsRun);
            Assert.False(viewModel.CanRun);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible),
                button => Equals(button.Content, Strings.Get("Add.RunStep")));
        });
    }

    /// <summary>The dialog's run button, found the way a user finds it: by what it says.</summary>
    private static Button FindRunButton(Window window)
        => window.GetVisualDescendants().OfType<Button>().Single(button =>
            button.IsEffectivelyVisible
            && Equals(button.Content, Strings.Get("Add.RunStep")));

    [Fact]
    public void A_mark_is_placed_inside_the_picture_the_step_looked_at()
    {
        var place = LookGeometry.Place(
            new ScreenPoint(100, 200), new ScreenPoint(130, 250), new ScreenSize(20, 10), 1);

        Assert.Equal(new LookRect(30, 50, 20, 10), place);
    }

    /// <summary>
    /// Two things are worth saving off this window and they are not the same thing: the picture
    /// with the marks on it, to show somebody what was seen, and the picture without them, to cut
    /// a fresh reference picture out of — marks drawn on a template come along into it and make it
    /// match only itself.
    /// </summary>
    [Fact]
    public void The_picture_can_be_saved_with_or_without_the_marks_on_it()
    {
        Ui.Run(() =>
        {
            var window = new LookWindow(Found());

            Assert.Equal(Strings.Get("Look.Save"), window.FindControl<Button>("SaveMarked")?.Content);
            Assert.Equal(Strings.Get("Look.SaveRaw"), window.FindControl<Button>("SaveRaw")?.Content);

            // The picture itself, and the one mark the step went with.
            Assert.Equal(2, window.SavedBoard(1, marks: true).Children.Count);
            Assert.Single(window.SavedBoard(1, marks: false).Children);
        });
    }

    /// <summary>A step that found its picture, so the window has a mark to draw over it.</summary>
    private static StepLook Found() => new()
    {
        StepId = "k3f9",
        StepType = "vision.findImage",
        Kind = LookKind.Template,
        Frame = new ImageFrame(20, 12, new byte[20 * 12 * 4]),
        Origin = new ScreenPoint(0, 0),
        Looking = "ok.png",
        Boxes =
        [
            new LookBox(new ImageMatch(0.97, new ScreenPoint(4, 3), new ScreenSize(6, 4)), LookRole.Hit),
        ],
        ChosenIndex = 1,
    };

    /// <summary>
    /// A wide picture is shown shrunk to fit, and goes back and forth between that and the size its
    /// pixels really are, which is what double-clicking it does.
    /// </summary>
    [Fact]
    public void Double_clicking_the_picture_goes_between_its_real_size_and_fitting()
    {
        Ui.Run(() =>
        {
            var wide = Found() with
            {
                Frame = new ImageFrame(4000, 8, new byte[4000 * 8 * 4]),
            };

            var window = new LookWindow(wide);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var fitted = window.FindControl<TextBlock>("ZoomText")!.Text;
            Assert.NotEqual("100%", fitted);

            window.ToggleRealSize();
            Assert.Equal("100%", window.FindControl<TextBlock>("ZoomText")!.Text);

            // Back to the size that fits. It is the fit worked out again rather than the number
            // from before, because the room the picture has is not the same once it is that big:
            // a scrollbar takes some of it.
            window.ToggleRealSize();
            Assert.NotEqual("100%", window.FindControl<TextBlock>("ZoomText")!.Text);
        });
    }

    /// <summary>
    /// A picture is read at its own size, and a screenful of hits does not fit in a window: the
    /// window fills the screen and comes back, by the button and by F11, and Esc goes back to the
    /// window before it closes it.
    /// </summary>
    [Fact]
    public void The_window_fills_the_screen_and_comes_back()
    {
        Ui.Run(() =>
        {
            var window = new LookWindow(Found());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var button = window.FindControl<Button>("FullScreen")!;
            Assert.Equal(Strings.Get("Look.FullScreen"), button.Content);

            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.Equal(Strings.Get("Look.FullScreenExit"), button.Content);

            // Esc is the way back before it is the way out: the full screen is what just happened.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.True(window.IsVisible);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void The_zoom_is_applied_to_the_mark_as_well_as_to_the_picture()
    {
        var place = LookGeometry.Place(
            new ScreenPoint(0, 0), new ScreenPoint(10, 10), new ScreenSize(4, 4), 2.5);

        Assert.Equal(new LookRect(25, 25, 10, 10), place);
    }

    [Fact]
    public void A_one_pixel_hit_is_enlarged_so_that_it_can_be_seen_and_stays_where_it_was()
    {
        var visible = LookGeometry.Visible(new LookRect(40, 60, 1, 1), 9);

        Assert.Equal(9, visible.Width);
        Assert.Equal(9, visible.Height);

        // The middle is still the middle: the mark grew around the pixel, not off to one side.
        Assert.Equal(40.5, visible.X + (visible.Width / 2));
        Assert.Equal(60.5, visible.Y + (visible.Height / 2));
    }

    [Fact]
    public void A_mark_bigger_than_the_smallest_one_is_left_as_it_is()
    {
        var visible = LookGeometry.Visible(new LookRect(5, 6, 20, 10), 9);

        Assert.Equal(new LookRect(5, 6, 20, 10), visible);
    }

    [Fact]
    public void Fitting_shows_the_whole_picture_without_enlarging_a_small_one()
    {
        Assert.Equal(0.5, LookGeometry.Fit(1000, 500, 500, 250));
        Assert.Equal(1, LookGeometry.Fit(100, 50, 500, 250));
        Assert.Equal(1, LookGeometry.Fit(0, 0, 500, 250));
    }

    [Fact]
    public void Zooming_in_and_out_stays_inside_what_the_view_allows()
    {
        Assert.True(LookGeometry.Step(1, true) > 1);
        Assert.True(LookGeometry.Step(1, false) < 1);
        Assert.Equal(LookGeometry.LargestZoom, LookGeometry.Step(LookGeometry.LargestZoom, true));
        Assert.Equal(LookGeometry.SmallestZoom, LookGeometry.Step(LookGeometry.SmallestZoom, false));
    }

    /// <summary>
    /// The click point a step uses is measured from the middle of the picture: that is where a
    /// click lands when the step asks for no offset, and where a picked point has to be counted
    /// from for the numbers to mean what the field says.
    /// </summary>
    [Theory]
    [InlineData(40, 20, 35, 5, 15, -5)]
    [InlineData(40, 20, 20, 10, 0, 0)]
    [InlineData(41, 21, 0, 0, -20, -10)]
    public void A_point_on_the_picture_becomes_an_offset_from_its_middle(
        int width, int height, int x, int y, int offsetX, int offsetY)
    {
        var offset = LookGeometry.Offset(width, height, new ScreenPoint(x, y));

        Assert.Equal(new ScreenPoint(offsetX, offsetY), offset);
    }

    [Fact]
    public void Clicking_outside_the_picture_is_a_click_at_its_edge()
    {
        // Drawn four times its size, a click on the far corner is still inside the picture.
        Assert.Equal(new ScreenPoint(9, 4), LookGeometry.Inside(10, 5, 4, 38, 18));
        Assert.Equal(new ScreenPoint(9, 4), LookGeometry.Inside(10, 5, 4, 400, 400));
        Assert.Equal(new ScreenPoint(0, 0), LookGeometry.Inside(10, 5, 4, -50, -50));
    }

    /// <summary>
    /// The offset is only worth picking on an action that looks for a picture: a step that clicks
    /// writing has an offset too, but there is nothing to point at.
    /// </summary>
    [Theory]
    [InlineData("vision.clickImage", true)]
    [InlineData("ocr.clickText", false)]
    [InlineData("input.mouseClick", false)]
    public void Picking_a_click_point_is_offered_where_there_is_a_picture_to_point_at(
        string key, bool offered)
    {
        Ui.Run(() =>
        {
            var window = new AddActionWindow(null, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("match"), []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.SelectAction(key);
            Dispatcher.UIThread.RunJobs();

            var pickers = viewModel.Rows.Concat(viewModel.AdvancedRows)
                .Where(row => row.ShowsOffsetPick)
                .ToList();

            Assert.Equal(offered ? 1 : 0, pickers.Count);
            if (offered)
            {
                // The two halves of the offset sit on the one line the button is under.
                Assert.Equal("offsetX", pickers[0].First.Definition.Name);
                Assert.Equal("offsetY", pickers[0].Second?.Definition.Name);
            }
        });
    }
}
