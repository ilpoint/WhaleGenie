using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

// 颜色那一格与色块条：取到的颜色要进到这一步里；透明色一行一个，每行带着自己的容差。
public partial class ActionDialogTests
{
    /// <summary>
    /// 吸管取到的颜色必须写进这一步的值里，而不只是画在那一行字上：输入框是只读的，值从形状进，
    /// 只改那一行就等于什么都没选到——保存下来还是原来那个值。
    /// </summary>
    [Fact]
    public void A_colour_taken_with_the_magnifier_becomes_the_steps_value()
    {
        Ui.RunAsync(async () =>
        {
            var (window, viewModel, _, hook) = ColourDialog("vision.waitColor", "color", 0x11, 0x22, 0x33);
            var colour = viewModel.Parameters.First(parameter => parameter.Definition.Name == "color");

            Pick(window, colour, hook);

            Assert.True(await Settled(() => Plain(colour) == "#112233"),
                $"the step holds {Plain(colour)} rather than the colour that was picked");
            window.Close();
            return true;
        });
    }

    /// <summary>
    /// 透明色一行一个颜色：吸管每取一次添一行，不顶掉前面的，同一个颜色取第二次不会添第二行；
    /// 输入框那行按顺序把它们读出来；每行自己的容差写在它旁边那格，并原样进到这一步里。
    /// </summary>
    [Fact]
    public void A_second_transparent_colour_is_added_as_another_row()
    {
        Ui.RunAsync(async () =>
        {
            var (window, viewModel, screen, hook) =
                ColourDialog("vision.findImage", "transparentColors", 0x11, 0x22, 0x33);
            var colours = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "transparentColors");

            Pick(window, colours, hook);
            Assert.True(await Settled(() => colours.ColourRows.Count == 1),
                $"the first colour did not land: {colours.ColourRows.Count} rows");
            Assert.Equal("#112233", colours.ColourRows[0].Colour);

            screen.Colour = new PixelColor(0x44, 0x55, 0x66);
            Pick(window, colours, hook);
            Assert.True(await Settled(() => colours.ColourRows.Count == 2),
                $"the second colour replaced the first: {colours.ColourRows.Count} rows");
            Assert.Equal(["#112233", "#445566"], colours.ColourRows.Select(row => row.Colour));

            // The box reads the list in the order the rows stand in.
            Assert.Contains("#112233", colours.DisplayLine);
            Assert.Contains("#445566", colours.DisplayLine);

            // A colour that is already listed adds no second row.
            screen.Colour = new PixelColor(0x11, 0x22, 0x33);
            Pick(window, colours, hook);
            Assert.True(await Settled(() => colours.ColourRows.Count == 2),
                $"a repeated colour was added: {colours.ColourRows.Count} rows");

            // The strip sits under the box and lays the colours out in list order, left to right.
            var box = window.GetVisualDescendants().OfType<TextBox>()
                .First(item => item.IsReadOnly && ReferenceEquals(item.DataContext, colours));
            var chips = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible && button.Name == "RemoveColourButton")
                .ToList();
            Assert.Equal(2, chips.Count);
            var boxAt = box.TranslatePoint(new Point(0, 0), window)!.Value;
            var first = chips[0].TranslatePoint(new Point(0, 0), window)!.Value;
            var second = chips[1].TranslatePoint(new Point(0, 0), window)!.Value;
            Assert.True(first.Y > boxAt.Y, "the swatches are not drawn under the box");
            Assert.True(first.X < second.X, "the swatches are not laid out in list order");

            // Each colour carries its own tolerance, and an empty one follows the field's.
            colours.ColourRows[1].Tolerance = "7";
            var rows = viewModel.BuildStep().Parameters
                .First(parameter => parameter.Name == "transparentColors").Rows;
            Assert.Equal(["#112233", "#445566"], rows.Select(row => row.Text("colour")));
            Assert.Equal(["", "7"], rows.Select(row => row.Text("tolerance")));

            // The strip draws a chip per row, and its × takes that one out of the step.
            var remove = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible && button.Name == "RemoveColourButton")
                .First(button => ReferenceEquals(button.DataContext, colours.ColourRows[1]));

            // The × is bound to the row it stands on; pressing it runs that row's own command.
            Assert.Same(colours.ColourRows[1].RemoveCommand, remove.Command);
            remove.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(await Settled(() => colours.ColourRows.Count == 1),
                $"the row was not taken out: {colours.ColourRows.Count} rows");
            Assert.Equal("#112233", colours.ColourRows[0].Colour);
            window.Close();
            return true;
        });
    }

    /// <summary>Opens one action's form on the page its field stands on, with a screen that answers one colour.</summary>
    private static (Window Window, AddActionViewModel ViewModel, ColourScreen Screen,
        ClickingHook Hook) ColourDialog(string action, string field, byte r, byte g, byte b)
    {
        var (window, viewModel) = OpenWindow(action);
        var parameter = viewModel.Parameters.First(item => item.Definition.Name == field);
        var screen = new ColourScreen { Colour = new PixelColor(r, g, b) };
        var hook = new ClickingHook();
        window.Devices = new ChecksDevices { Screen = screen, Hook = hook };

        // 透明色在「高级设置」那一页上，先翻到那一页，像用户点一下那样；基础页上的字段本来就在眼前。
        if (parameter.IsAdvanced)
        {
            viewModel.Page = viewModel.Pages.First(page => page.Key == AddActionViewModel.AdvancedPage);
            Dispatcher.UIThread.RunJobs();
        }

        return (window, viewModel, screen, hook);
    }

    /// <summary>Presses the field's magnifier and answers with one click on the fake hook.</summary>
    private static void Pick(Window window, StepParameterViewModel parameter, ClickingHook hook)
    {
        var dropper = window.GetVisualDescendants().OfType<Button>()
            .Where(item => item.IsEffectivelyVisible && item.Name == "PickColourButton")
            .First(item => ReferenceEquals(item.DataContext, parameter));

        dropper.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        hook.Click(700, 500);
    }

    /// <summary>The value the step would keep for a field that holds one colour.</summary>
    private static string Plain(StepParameterViewModel parameter) => Formula.Plain(parameter.Written).Trim();
}
