using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

// 颜色那一格与吸管：取到的颜色要真的进到这一步里，透明色那一列取第二个时要添一行而不是顶掉。
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

            PickFieldColour(window, colour, hook);

            Assert.True(await Settled(() => Formula.Plain(colour.Written) == "#112233"),
                $"the step holds {Formula.Plain(colour.Written)} rather than the colour that was picked");
            window.Close();
            return true;
        });
    }

    /// <summary>
    /// 透明色是一列：一个图标可能带着不止一种底色，每取一次添一行，不顶掉前面的；同一个颜色取第二次
    /// 也不会添第二行。
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

            TakeColour(window, colours, hook);
            Assert.True(await Settled(() => colours.ColourRows.Count == 1),
                $"the first colour did not land: {colours.ColourRows.Count} rows");
            Assert.Equal("#112233", colours.ColourRows[0].Colour);

            screen.Colour = new PixelColor(0x44, 0x55, 0x66);
            TakeColour(window, colours, hook);
            Assert.True(await Settled(() => colours.ColourRows.Count == 2),
                $"the second colour replaced the first: {colours.ColourRows.Count} rows");
            Assert.Equal(["#112233", "#445566"], colours.ColourRows.Select(row => row.Colour));

            // A colour that is already listed adds no second row.
            screen.Colour = new PixelColor(0x11, 0x22, 0x33);
            TakeColour(window, colours, hook);
            Assert.True(await Settled(() => colours.ColourRows.Count == 2),
                $"a repeated colour was added: {colours.ColourRows.Count} rows");

            window.Close();
            return true;
        });
    }

    /// <summary>
    /// 一行可以是一个装着颜色的变量：写成名字，颜色在跑到这一步时才取，所以换底色只要改变量。
    /// </summary>
    [Fact]
    public void A_colour_row_keeps_a_name_as_a_name()
    {
        // A written colour is text: it is compared as it stands.
        var written = new ColourRowViewModel("#FFFFFF").ToRow();
        Assert.Equal(new TextNode("#FFFFFF"), written.Cell("colour"));

        // A name is a name: the colour it holds is read when the step runs, so changing the
        // variable changes what the picture is found regardless of.
        var named = new ColourRowViewModel("bg").ToRow();
        Assert.Equal("bg", Assert.IsType<VariableNode>(named.Cell("colour")).Name);
    }

    /// <summary>Opens one action's form on the page its field stands on, with a screen answering one colour.</summary>
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

    /// <summary>Presses the magnifier that sits on a field of its own, then clicks the fake screen.</summary>
    private static void PickFieldColour(Window window, StepParameterViewModel parameter,
        ClickingHook hook)
    {
        var dropper = window.GetVisualDescendants().OfType<Button>()
            .Where(item => item.IsEffectivelyVisible && item.Name == "PickColourButton")
            .First(item => ReferenceEquals(item.DataContext, parameter));

        dropper.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        hook.Click(700, 500);
    }

    /// <summary>The same for the colour list, whose button adds a row rather than filling one in.</summary>
    private static void TakeColour(Window window, StepParameterViewModel parameter, ClickingHook hook)
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .Where(item => item.IsEffectivelyVisible
                && Equals(item.Content, Strings.Get("Add.ColourRowPick")))
            .First(item => ReferenceEquals(item.DataContext, parameter));

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        hook.Click(700, 500);
    }
}
