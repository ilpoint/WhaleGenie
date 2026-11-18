using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

// 颜色那一格与吸管：取到的颜色要真的进到这一步里，取第二个透明色时要添进去而不是顶掉。
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
    /// 一个图标可能带着不止一种底色，所以「透明色」那格要能收好几个：每取一次添一个，不顶掉，
    /// 同一个颜色取第二次也不会写两遍。
    /// </summary>
    [Fact]
    public void A_second_transparent_colour_is_added_to_the_first()
    {
        Ui.RunAsync(async () =>
        {
            var (window, viewModel, screen, hook) =
                ColourDialog("vision.findImage", "transparentColors", 0x11, 0x22, 0x33);
            var colours = viewModel.Parameters
                .First(parameter => parameter.Definition.Name == "transparentColors");

            Pick(window, colours, hook);
            Assert.True(await Settled(() => Plain(colours) == "#112233"),
                $"the first colour did not land: {Plain(colours)}");

            screen.Colour = new PixelColor(0x44, 0x55, 0x66);
            Pick(window, colours, hook);
            Assert.True(await Settled(() => Plain(colours) == "#112233; #445566"),
                $"the second colour was not added: {Plain(colours)}");

            // Picking a colour that is already there leaves the list as it was.
            screen.Colour = new PixelColor(0x11, 0x22, 0x33);
            Pick(window, colours, hook);
            Assert.True(await Settled(() => Plain(colours) == "#112233; #445566"),
                $"a repeated colour was written twice: {Plain(colours)}");

            window.Close();
            return true;
        });
    }

    /// <summary>Opens one action's form on the advanced page, with a screen that answers one colour.</summary>
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

    /// <summary>The value the step would keep, as it is written down rather than as it is drawn.</summary>
    private static string Plain(StepParameterViewModel parameter) => Formula.Plain(parameter.Written).Trim();
}
