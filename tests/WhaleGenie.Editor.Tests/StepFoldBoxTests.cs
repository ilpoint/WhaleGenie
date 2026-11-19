using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Path = Avalonia.Controls.Shapes.Path;

namespace WhaleGenie.Tests;

/// <summary>
/// 步骤树里那枚折叠钮得先被读成一枚钮。它是虚线树线上的一格：方块原来只有一层几乎与背景同深的
/// 淡底，穿过中线的虚线又画在它上面，于是「这里能折叠」和「这里是一条线」在屏幕上没有分别。
///
/// 这些用例读的是控件模板里真正拿到颜色与层级的部件，而不是按钮自己的静息属性——只读属性的话，
/// 样式改坏了仍然是绿的。
/// </summary>
public class StepFoldBoxTests
{
    [Fact]
    public void The_fold_box_is_a_solid_chip_rather_than_another_stretch_of_the_guide_line()
    {
        Ui.Run(() =>
        {
            var (window, box) = Open();

            // 底、边与字形各取一个角色：实心底让方块从背后的行里立起来，描边压住穿过它的线，
            // 字形用主文字色，免得和旁边那条淡虚线读成一个亮度。
            Assert.Equal(Token("SurfaceControl", box), Face(box));
            Assert.Equal(Token("BorderStrong", box), Edge(box));
            Assert.Equal(Token("TextPrimary", box), Glyph(box));

            // 带 alpha 的颜色仍会让线透出来，而那正是原来那个问题；这里要的是不透明的底。
            Assert.Equal(byte.MaxValue, Face(box).A);

            window.Close();
        });
    }

    /// <summary>
    /// 方块画在引导线之上。树线在内容列里画，方块在它左边那一格：两列同属一个 Grid，谁在上面由
    /// 层级决定，而层级一旦掉回默认值，虚线又会盖住方块的中线。
    /// </summary>
    [Fact]
    public void The_fold_box_paints_over_the_guide_line_it_sits_on()
    {
        Ui.Run(() =>
        {
            var (window, box) = Open();

            // 这一行的两个格子：方块所在的那一格与它右边的内容列。向上找而不是向下找——
            // 块里的行各有自己的模板，向下找会先撞见子行的那一套。
            var container = Assert.IsType<Panel>(box.GetVisualParent());
            var header = Assert.IsType<Grid>(container.GetVisualParent());
            var content = header.Children.OfType<ContentPresenter>().Single();

            Assert.True(container.ZIndex > content.ZIndex,
                $"the fold box is drawn at {container.ZIndex} and the guide lines at {content.ZIndex}");

            window.Close();
        });
    }

    [Fact]
    public void The_fold_box_answers_the_pointer()
    {
        Ui.Run(() =>
        {
            var (window, box) = Open();
            var resting = Face(box);
            var at = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)!;

            window.MouseMove(at.Value, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Token("SurfaceControlHover", box), Face(box));

            window.MouseDown(at.Value, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Token("SurfaceControlPressed", box), Face(box));

            // 松手并把指针挪开：底回到静息，而这一下真的按到了钮——块的折叠状态跟着变。
            var opened = box.IsChecked;
            window.MouseUp(at.Value, MouseButton.Left);
            window.MouseMove(new Point(1, 1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(resting, Face(box));
            Assert.NotEqual(opened, box.IsChecked);

            window.Close();
        });
    }

    /// <summary>树里一枚块的折叠钮，开着，块是空的：按下它不会让版面跳一下。</summary>
    private static (MacroEditorWindow Window, ToggleButton Box) Open()
    {
        var block = new MacroStep { Type = "control.repeat", IsExpanded = true };
        block.EnsureDeclaredLists();

        var macro = new MacroItem { Name = "m" };
        macro.Steps.Add(block);

        var window = new MacroEditorWindow(macro, [macro]);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var box = window.GetVisualDescendants().OfType<ToggleButton>()
            .Single(button => button.Name == "PART_ExpandCollapseChevron"
                && button.IsEffectivelyVisible);
        return (window, box);
    }

    /// <summary>模板里那层画底与边的部件：方块自己的根边框。</summary>
    private static Border Root(ToggleButton box)
        => box.GetVisualDescendants().OfType<Border>().Single();

    /// <summary>The arrow inside the chip, which is what says "opens downwards".</summary>
    private static Path GlyphPath(ToggleButton box)
        => box.GetVisualDescendants().OfType<Path>().Single(path => path.Name == "Glyph");

    private static Color Face(ToggleButton box) => Colour(Root(box).Background);

    private static Color Edge(ToggleButton box) => Colour(Root(box).BorderBrush);

    private static Color Glyph(ToggleButton box) => Colour(GlyphPath(box).Stroke);

    private static Color Colour(IBrush? brush)
        => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color Token(string key, Visual owner)
    {
        Assert.True(Application.Current!.TryGetResource(key, owner.ActualThemeVariant, out var value), key);
        return Colour(Assert.IsAssignableFrom<IBrush>(value));
    }
}
