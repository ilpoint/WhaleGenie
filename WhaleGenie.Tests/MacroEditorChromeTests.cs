using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The controls around the macro editor's working area: what stays reachable when a panel fills
/// up, what the splitters may not hide, and how document tabs and window buttons behave.
/// </summary>
public class MacroEditorChromeTests
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
            Assert.DoesNotContain(dock.GetVisualAncestors(), visual => visual is ScrollViewer);

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

            window.Close();
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

            window.Close();
        });
    }

    [Fact]
    public void Removing_the_last_entry_hides_every_type_parameter()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (MacroEditorViewModel)window.DataContext!;
            model.RemoveTriggerCommand.Execute(model.SelectedTrigger);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(model.TriggerEntries);
            Assert.False(window.FindControl<StackPanel>("SelectedTriggerDetailsPanel")!
                .IsEffectivelyVisible);
            Assert.False(window.FindControl<StackPanel>("KeyTriggerSettingsPanel")!
                .IsEffectivelyVisible);
            Assert.True(window.FindControl<Button>("AddTriggerButton")!.IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible
                    && text.Text == Strings.Get("Editor.NoTriggerSelected"));

            window.Close();
        });
    }

    [Fact]
    public void Macro_settings_has_one_heading_with_identity_above_the_trigger_entries()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe", Description = "summary" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var drawer = window.FindControl<Border>("MacroSettingsDrawer")!;
            var title = window.FindControl<TextBlock>("MacroSettingsTitle")!;
            var name = window.FindControl<TextBox>("NameInput")!;
            var description = window.FindControl<TextBox>("DescriptionInput")!;
            var group = window.FindControl<Border>("TriggerEntriesGroup")!;
            var save = window.FindControl<Button>("SaveButton")!;
            var run = window.FindControl<Button>("RunDebugButton")!;

            var headings = drawer.GetVisualDescendants().OfType<TextBlock>()
                .Count(text => text.IsEffectivelyVisible
                    && text.Text == Strings.Get("Editor.MacroSettings"));
            Assert.Equal(1, headings);
            Assert.True(title.TranslatePoint(default, drawer)!.Value.Y
                < name.TranslatePoint(default, drawer)!.Value.Y);
            Assert.True(name.TranslatePoint(default, drawer)!.Value.Y
                < description.TranslatePoint(default, drawer)!.Value.Y);
            Assert.True(description.TranslatePoint(default, drawer)!.Value.Y
                < group.TranslatePoint(default, drawer)!.Value.Y);
            Assert.True(save.IsEffectivelyVisible);
            Assert.True(run.IsEffectivelyVisible);
            Assert.DoesNotContain(save.GetVisualAncestors(), visual => visual is ScrollViewer);
            Assert.DoesNotContain(run.GetVisualAncestors(), visual => visual is ScrollViewer);

            window.Close();
        });
    }

    [Fact]
    public void Trigger_cards_select_the_entry_and_show_only_its_shared_fields()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (MacroEditorViewModel)window.DataContext!;
            var key = model.SelectedTrigger!;
            model.AddTriggerCommand.Execute(TriggerKind.Color);
            Dispatcher.UIThread.RunJobs();

            var entries = window.FindControl<ListBox>("TriggerEntries")!;
            var loop = window.FindControl<StackPanel>("LoopModePanel")!;
            var once = window.FindControl<CheckBox>("TriggerOnceCheck")!;
            var add = window.FindControl<Button>("AddTriggerButton")!;
            var kindItems = Assert.IsType<MenuFlyout>(add.Flyout).Items
                .OfType<MenuItem>().ToList();

            Assert.Equal(2, entries.ItemCount);
            Assert.Same(model.SelectedTrigger, entries.SelectedItem);
            Assert.Equal(TriggerKind.Color, model.TriggerMode);
            Assert.False(loop.IsEffectivelyVisible);
            Assert.True(once.IsEffectivelyVisible);
            Assert.Equal(9, kindItems.Count);
            Assert.Equal(
                [
                    TriggerKind.Key,
                    TriggerKind.Color,
                    TriggerKind.Interface,
                    TriggerKind.Hotstring,
                    TriggerKind.Timer,
                    TriggerKind.File,
                    TriggerKind.Process,
                    TriggerKind.Window,
                    TriggerKind.Idle,
                ],
                kindItems.Select(item => Assert.IsType<TriggerKind>(item.Tag)));

            entries.SelectedItem = key;
            Dispatcher.UIThread.RunJobs();

            Assert.Null(window.FindControl<TextBlock>("SelectedTriggerSettingsHeading"));
            Assert.Null(window.FindControl<ComboBox>("TriggerKindSelect"));
            Assert.Equal(TriggerKind.Key, model.TriggerMode);
            Assert.True(loop.IsEffectivelyVisible);
            Assert.True(once.IsEffectivelyVisible);
            Assert.True(entries.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => ReferenceEquals(item.DataContext, key)).IsSelected);

            window.Close();
        });
    }

    [Fact]
    public void Menu_entries_show_no_remove_button_and_submacro_pages_hide_triggers()
    {
        Ui.Run(() =>
        {
            var parent = ParentWithChild();
            var window = new MacroEditorWindow(parent, [parent]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (MacroEditorViewModel)window.DataContext!;
            var menu = new TriggerEntry { Kind = TriggerKind.Menu };
            model.TriggerEntries.Add(menu);
            Dispatcher.UIThread.RunJobs();

            var entries = window.FindControl<ListBox>("TriggerEntries")!;
            var removeButtons = entries.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("RemoveButton"))
                .ToList();
            Assert.Contains(removeButtons, button => ReferenceEquals(button.DataContext, menu)
                && !button.IsEffectivelyVisible);
            Assert.Contains(removeButtons, button =>
                button.DataContext is TriggerEntry { Kind: not TriggerKind.Menu }
                && button.IsEffectivelyVisible);

            var tabs = window.FindControl<ListBox>("DocumentTabs")!;
            tabs.SelectedItem = model.Documents.Single(page => !page.IsCurrent);
            Dispatcher.UIThread.RunJobs();

            Assert.True(((MacroEditorViewModel)window.DataContext!).IsSubMacro);
            Assert.False(window.FindControl<Border>("TriggerEntriesGroup")!.IsEffectivelyVisible);
            Assert.True(window.FindControl<TextBlock>("MacroSettingsTitle")!.IsVisible);
            Assert.NotNull(window.FindControl<TextBox>("NameInput"));
            Assert.NotNull(window.FindControl<TextBox>("DescriptionInput"));

            window.Close(null);
        });
    }

    [Fact]
    public void A_submacro_tab_can_close_without_losing_its_definition()
    {
        Ui.Run(() =>
        {
            var parent = ParentWithChild();
            var window = new MacroEditorWindow(parent, [parent]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (MacroEditorViewModel)window.DataContext!;
            var tabs = window.FindControl<ListBox>("DocumentTabs")!;
            var closeButtons = tabs.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "CloseDocumentButton")
                .ToList();

            Assert.Equal(2, closeButtons.Count);
            Assert.False(closeButtons.Single(button =>
                button.DataContext is MacroDocumentPageViewModel { IsCurrent: true }).IsVisible);
            Assert.True(closeButtons.Single(button =>
                button.DataContext is MacroDocumentPageViewModel { IsCurrent: false }).IsVisible);

            var childPage = model.Documents.Single(page => !page.IsCurrent);
            tabs.SelectedItem = childPage;
            Dispatcher.UIThread.RunJobs();

            var childButton = closeButtons.Single(button =>
                button.DataContext is MacroDocumentPageViewModel { IsCurrent: false });
            Click(window, childButton);

            Assert.Single(model.Documents);
            Assert.False(((MacroEditorViewModel)window.DataContext!).IsSubMacro);

            model.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.Result);
            Assert.Contains(window.Result!.SubMacros, submacro => submacro.Name == "child");
        });
    }

    [Fact]
    public void A_closed_submacro_tab_opens_again_from_the_submacro_dock()
    {
        Ui.Run(() =>
        {
            var parent = ParentWithChild();
            var window = new MacroEditorWindow(parent, [parent]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (MacroEditorViewModel)window.DataContext!;
            var tabs = window.FindControl<ListBox>("DocumentTabs")!;
            var close = tabs.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "CloseDocumentButton"
                    && button.DataContext is MacroDocumentPageViewModel { IsCurrent: false });
            Click(window, close);
            Assert.Single(model.Documents);
            Assert.Equal("parent", ((MacroEditorViewModel)window.DataContext!).Name);

            var palette = window.FindControl<ActionPaletteView>("ActionPalette")!;
            var paletteModel = (ActionPaletteViewModel)palette.DataContext!;
            paletteModel.SelectDock(ActionPaletteDock.SubMacros);
            Dispatcher.UIThread.RunJobs();

            var card = palette.GetVisualDescendants().OfType<Button>().Single(button =>
                button.DataContext is ActionPaletteCardViewModel
                {
                    Kind: ActionPaletteCardKind.SubMacro,
                    Title: "child",
                });
            var center = card.TranslatePoint(
                new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), window);
            Assert.NotNull(center);
            window.MouseDown(center!.Value, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(center.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, model.Documents.Count);
            Assert.Equal("child", ((MacroEditorViewModel)window.DataContext!).Name);

            window.Close(null);
        });
    }

    [Fact]
    public void The_title_bar_has_pin_minimize_maximize_and_close_buttons()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var pin = window.FindControl<Button>("PinButton")!;
            var maximize = window.FindControl<Button>("MaximizeButton")!;
            Assert.NotNull(window.FindControl<Button>("MinimizeButton"));
            Assert.NotNull(window.FindControl<Button>("CloseButton"));

            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Topmost);
            Assert.Contains("pinned", pin.Classes);
            Assert.Equal(Strings.Get("Common.Unpin"), ToolTip.GetTip(pin));
            Assert.Equal(AppIcons.PinOff, window.FindControl<IconView>("PinIcon")!.Data);

            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.Topmost);
            Assert.DoesNotContain("pinned", pin.Classes);
            Assert.Equal(Strings.Get("Common.Pin"), ToolTip.GetTip(pin));
            Assert.Equal(AppIcons.Pin, window.FindControl<IconView>("PinIcon")!.Data);

            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.Equal(Strings.Get("Common.Restore"), ToolTip.GetTip(maximize));
            Assert.Equal(AppIcons.Restore, window.FindControl<IconView>("MaximizeIcon")!.Data);

            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(Strings.Get("Common.Maximize"), ToolTip.GetTip(maximize));
            Assert.Equal(AppIcons.Maximize, window.FindControl<IconView>("MaximizeIcon")!.Data);

            window.Close();
        });
    }

    private static MacroItem ParentWithChild()
    {
        var child = new MacroItem { Name = "child" };
        child.Steps.Add(SetVariable("inside", "1"));

        var parent = new MacroItem { Name = "parent" };
        parent.Steps.Add(new MacroStep
        {
            Type = "control.runMacro",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter
                {
                    Name = "macro",
                    Kind = ActionParameterKind.Macro,
                    Value = "child",
                },
            ],
        });
        parent.SubMacros.Add(child);
        return parent;
    }

    private static void Click(Window window, Button button)
    {
        var center = button.TranslatePoint(
            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        Assert.NotNull(center);
        window.MouseDown(center!.Value, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(center.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static MacroStep SetVariable(string name, string value)
        => new()
        {
            Type = "control.setVariable",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = name },
                new StepParameter { Name = "scope", Kind = ActionParameterKind.Choice, Value = "local" },
                new StepParameter { Name = "value", Kind = ActionParameterKind.Expression, Value = value },
            ],
        };
}
