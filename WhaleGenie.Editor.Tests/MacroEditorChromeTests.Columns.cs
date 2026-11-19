using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The columns of the editor: scrollbars, docks and the widths the fields need.
public partial class MacroEditorChromeTests
{
    [Fact]
    public void A_full_variable_center_gets_a_narrow_vertical_scrollbar()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            for (var index = 0; index < 40; index++)
            {
                macro.Steps.Add(SetVariable($"item{index}", index.ToString()));
            }

            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var wall = window.FindControl<VariableWall>("VariableCenterWall")!;
            var list = wall.FindControl<ScrollViewer>("VariableList")!;
            var vertical = list.GetVisualDescendants().OfType<Control>()
                .Single(control => control.Name == "PART_VerticalScrollBar");

            Assert.Equal(ScrollBarVisibility.Disabled, list.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, list.VerticalScrollBarVisibility);
            Assert.True(list.Extent.Height > list.Viewport.Height);
            Assert.True(vertical.IsVisible);
            Assert.InRange(vertical.Bounds.Width, 1, 8);

            window.Close();
        });
    }

    [Fact]
    public void The_four_docks_fit_the_280_pixel_left_column_without_horizontal_scrolling()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var palette = window.FindControl<ActionPaletteView>("ActionPalette")!;
            var dock = palette.FindControl<ListBox>("SourceDock")!;
            var items = dock.GetVisualDescendants().OfType<ListBoxItem>().ToList();

            Assert.Equal(4, items.Count);
            Assert.True(dock.Bounds.Width <= 280);
            Assert.False(InsideAPageScroller(dock), "the source dock sits in a page scroller");

            var widths = items.Select(item => item.Bounds.Width).ToList();
            Assert.All(widths, width => Assert.True(width > 0));
            Assert.True(widths.Max() - widths.Min() < 1);
            Assert.All(items, item =>
            {
                var right = item.TranslatePoint(new Point(item.Bounds.Width, 0), dock);
                Assert.NotNull(right);
                Assert.True(right!.Value.X <= dock.Bounds.Width + 0.5);
            });

            window.Close();
        });
    }

    [Fact]
    public void The_variable_center_spends_its_height_on_variables_rather_than_on_its_own_header()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            for (var index = 0; index < 12; index++)
            {
                macro.Steps.Add(SetVariable($"item{index}", index.ToString()));
            }

            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var wall = window.FindControl<VariableWall>("VariableCenterWall")!;
            var groups = wall.FindControl<ListBox>("VariableGroups")!;
            var list = wall.FindControl<ScrollViewer>("VariableList")!;
            var locals = window.FindControl<Button>("ManageLocalsButton")!;
            var globals = window.FindControl<Button>("ManageGlobalsButton")!;

            // 标题和两个按钮占一行，不再是标题加两行说明的六十像素。
            Assert.InRange(wall.Bounds.Y, 28, 36);

            // 两个按钮要在面板里放得下：右边不出界，也不会把标题挤到折行。
            Assert.True(globals.Bounds.Right <= wall.Bounds.Width + 1,
                $"the buttons end at {globals.Bounds.Right:0.#}, the panel is "
                + $"{wall.Bounds.Width:0.#} wide");
            Assert.Equal(locals.Bounds.Height, globals.Bounds.Height);

            // 筛选条只占一行字的高度。
            Assert.InRange(groups.Bounds.Height, 18, 30);

            // 搜索框、筛选条和它们之间的间距之外，整块高度都是变量卡的。
            Assert.True(list.Bounds.Height >= wall.Bounds.Height - 80,
                $"the list is {list.Bounds.Height:0.#} of {wall.Bounds.Height:0.#}");

            window.Close();
        });
    }

    [Fact]
    public void The_right_splitters_cannot_hide_the_save_and_run_buttons()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var variables = window.FindControl<Border>("VariableCenterPanel")!;
            var settings = window.FindControl<Border>("MacroSettingsDrawer")!;
            var rows = Assert.IsType<Grid>(variables.Parent).RowDefinitions;
            var save = window.FindControl<Button>("SaveButton")!;
            var run = window.FindControl<Button>("RunDebugButton")!;

            Assert.Equal(180, rows[0].MinHeight);
            Assert.Equal(new GridLength(8), rows[1].Height);
            Assert.Equal(240, rows[2].MinHeight);
            Assert.True(variables.Bounds.Height >= 180);
            Assert.True(settings.Bounds.Height >= 240);
            Assert.True(save.Bounds.Height > 0);
            Assert.True(run.Bounds.Height > 0);

            window.Close();
        });
    }

    [Fact]
    public void The_shortest_macro_settings_column_keeps_inputs_inside_the_panel()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var columns = window.FindControl<Grid>("EditorColumns")!;
            columns.ColumnDefinitions[4].Width = new GridLength(360);
            Dispatcher.UIThread.RunJobs();

            var model = (MacroEditorViewModel)window.DataContext!;
            var drawer = window.FindControl<Border>("MacroSettingsDrawer")!;
            foreach (var kind in new[]
                     {
                         TriggerKind.Key,
                         TriggerKind.Color,
                         TriggerKind.Interface,
                         TriggerKind.Hotstring,
                         TriggerKind.Timer,
                         TriggerKind.File,
                         TriggerKind.Process,
                         TriggerKind.Window,
                         TriggerKind.Idle,
                     })
            {
                model.AddTriggerCommand.Execute(kind);
                Dispatcher.UIThread.RunJobs();

                var controls = drawer.GetVisualDescendants().OfType<Control>()
                    .Where(control => control.IsEffectivelyVisible
                        && control is ComboBox or TextBox or FANumberBox)
                    .ToList();
                Assert.NotEmpty(controls);
                Assert.All(controls, control =>
                {
                    var left = control.TranslatePoint(default, drawer);
                    var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), drawer);
                    Assert.NotNull(left);
                    Assert.NotNull(right);
                    Assert.True(control.Bounds.Width > 0);
                    Assert.True(left.Value.X >= -0.5);
                    Assert.True(right.Value.X <= drawer.Bounds.Width + 0.5);
                });
            }

            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void The_colour_match_column_leaves_room_for_the_hex_value()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var columns = window.FindControl<Grid>("EditorColumns")!;
            columns.ColumnDefinitions[4].Width = new GridLength(360);
            var model = (MacroEditorViewModel)window.DataContext!;
            model.AddTriggerCommand.Execute(TriggerKind.Color);
            Dispatcher.UIThread.RunJobs();

            var grid = window.FindControl<Grid>("ColorTriggerSettings")!;
            var match = window.FindControl<ComboBox>("ColorMatchSelect")!;
            var hex = window.FindControl<TextBox>("HexColorInput")!;
            var right = hex.TranslatePoint(new Point(hex.Bounds.Width, 0), grid);

            Assert.Equal(new GridLength(140), grid.ColumnDefinitions[0].Width);
            Assert.InRange(match.Bounds.Width, 130, 140.5);
            Assert.True(hex.Bounds.Width >= hex.MinWidth);
            Assert.NotNull(right);
            Assert.True(right.Value.X <= grid.Bounds.Width + 0.5);

            Ui.CloseAndAnswer(window);
        });
    }

    /// <summary>
    /// The four sources at the top of the action library and the five groups at the top of the
    /// variable center are one control with one look. They were two: the library's tabs were plain
    /// list rows with a two pixel bar, and the wall's chips wrote their own label colour into the
    /// template, so the chosen chip's background could change while its words stayed in the body
    /// colour. Reading the label back is what catches the second half — the style can be perfect and
    /// the local colour still wins.
    /// </summary>
    [Fact]
    public void The_action_library_and_the_variable_center_wear_the_same_dock()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                var palette = window.FindControl<ActionPaletteView>("ActionPalette")!;
                var wall = window.FindControl<VariableWall>("VariableCenterWall")!;
                var sources = palette.FindControl<ListBox>("SourceDock")!;
                var groups = wall.FindControl<ListBox>("VariableGroups")!;

                Assert.Contains("Dock", sources.Classes);
                Assert.Contains("Dock", groups.Classes);
                Assert.DoesNotContain("List", sources.Classes);

                // One chosen source, and its words are the accent — the same two colours the wall's
                // chosen group has below.
                var chosenSource = Assert.Single(
                    sources.GetVisualDescendants().OfType<ListBoxItem>(), item => item.IsSelected);
                Assert.Equal(Token("AccentText", window), Colour(Label(chosenSource)));

                // The icon is the row's second reader of that colour: it is a control in the
                // template whose stroke is bound to the row's foreground, so it has to be named
                // separately here or a style that only recolours the text leaves the icon behind.
                var chosenIcon = chosenSource.GetVisualDescendants().OfType<IconView>().Single();
                Assert.Equal(Token("AccentText", window), Colour(chosenIcon.Stroke));

                var otherSource = sources.GetVisualDescendants().OfType<ListBoxItem>()
                    .First(item => !item.IsSelected);
                Assert.Equal(Token("TextSecondary", window), Colour(Label(otherSource)));
                var otherIcon = otherSource.GetVisualDescendants().OfType<IconView>().Single();
                Assert.Equal(Token("TextSecondary", window), Colour(otherIcon.Stroke));

                var chosenGroup = Assert.Single(
                    groups.GetVisualDescendants().OfType<ListBoxItem>(), item => item.IsSelected);
                Assert.Equal(Token("AccentText", window), Colour(Label(chosenGroup)));

                var otherGroup = groups.GetVisualDescendants().OfType<ListBoxItem>()
                    .First(item => !item.IsSelected);
                Assert.Equal(Token("TextSecondary", window), Colour(Label(otherGroup)));
            }
            finally
            {
                Ui.CloseAndAnswer(window);
            }
        });
    }


    /// <summary>
    /// 默认 1024 宽时三列正好放得下，中间那一列不必横向滚动。最窄 720 时列不压窄，整条
    /// 内容横向滚动；右栏仍在滚动范围里，不会被窗口裁掉——「保存」「运行调试」还是找得到。
    /// </summary>
    [Fact]
    public void The_editor_opens_at_1024_and_scrolls_the_columns_instead_of_squeezing_them_at_720()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            Assert.Equal(1024, window.Width);
            Assert.Equal(720, window.MinWidth);

            window.Show();
            Dispatcher.UIThread.RunJobs();

            var scroll = window.FindControl<ScrollViewer>("EditorScroll")!;
            var columns = window.FindControl<Grid>("EditorColumns")!;
            var palette = window.FindControl<ActionPaletteView>("ActionPalette")!;
            var document = window.FindControl<MacroDocumentView>("MacroDocument")!;
            var drawer = window.FindControl<Border>("MacroSettingsDrawer")!;

            Assert.InRange(window.ClientSize.Width, 1023.5, 1024.5);
            Assert.InRange(palette.Bounds.Width, 279.5, 280.5);
            Assert.InRange(document.Bounds.Width, 343.5, 344.5);
            Assert.InRange(drawer.Bounds.Width, 359.5, 360.5);
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5,
                $"the editor scrolls at its default size: {scroll.Extent.Width} in {scroll.Viewport.Width}");

            window.Width = 720;
            Dispatcher.UIThread.RunJobs();

            Assert.InRange(window.ClientSize.Width, 719.5, 720.5);
            Assert.InRange(columns.Bounds.Width, 995.5, 996.5);
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width + 0.5,
                "the columns are wider than the window and have to scroll");

            var right = drawer.TranslatePoint(new Point(drawer.Bounds.Width, 0), columns);
            Assert.NotNull(right);
            Assert.True(right!.Value.X <= columns.Bounds.Width + 0.5,
                $"the settings rail ends at {right.Value.X:0.#} outside {columns.Bounds.Width:0.#}");

            // And wider than the three columns need, the middle one takes the spare room.
            window.Width = 1486;
            Dispatcher.UIThread.RunJobs();

            Assert.InRange(columns.Bounds.Width, 1457.5, 1458.5);
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5,
                "a wide editor has nothing to scroll");

            Ui.CloseAndAnswer(window);
        });
    }


    /// <summary>
    /// Whether a control sits inside a scrolling area of its own. The editor's three columns share
    /// one horizontal strip, and every pane is inside it by design; what these checks guard is a
    /// pane being put inside a second, vertical scroller that would carry its heading away with
    /// the page.
    /// </summary>
    private static bool InsideAPageScroller(Visual visual) =>
        visual.GetVisualAncestors().OfType<ScrollViewer>()
            .Any(view => view.Name != "EditorScroll");

    private static IBrush? Label(ListBoxItem item)
        => item.GetVisualDescendants().OfType<TextBlock>().First().Foreground;

    private static Color? Colour(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    private static Color Token(string key, StyledElement owner)
    {
        Assert.True(Application.Current!.TryGetResource(key, owner.ActualThemeVariant, out var value), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }
}
