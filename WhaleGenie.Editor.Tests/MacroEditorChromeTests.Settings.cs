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

// The settings drawer: its pages, the fields they hold, and a save blocked by one.
public partial class MacroEditorChromeTests
{
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

            Ui.CloseAndAnswer(window);
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

    /// <summary>
    /// 宏设置分成两页，标题与这排按钮固定在滚动区外：翻页时它们不动，滚动只发生在当前页里。
    /// </summary>
    [Fact]
    public void The_settings_drawer_switches_between_two_pages_under_its_title()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var drawer = window.FindControl<Border>("MacroSettingsDrawer")!;
            var title = window.FindControl<TextBlock>("MacroSettingsTitle")!;
            var tabs = drawer.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("Tab") && button.Classes.Contains("Root"))
                .ToList();

            Assert.Equal(2, tabs.Count);
            Assert.Equal(["General", "Specific"],
                tabs.Select(tab => (string)tab.Tag!).ToArray());
            Assert.DoesNotContain(title.GetVisualAncestors(), visual => visual is ScrollViewer);
            Assert.All(tabs, tab =>
                Assert.DoesNotContain(tab.GetVisualAncestors(), visual => visual is ScrollViewer));

            var name = window.FindControl<TextBox>("NameInput")!;
            Assert.True(title.TranslatePoint(default, drawer)!.Value.Y
                < tabs[0].TranslatePoint(default, drawer)!.Value.Y);
            Assert.True(tabs[0].TranslatePoint(default, drawer)!.Value.Y
                < name.TranslatePoint(default, drawer)!.Value.Y);

            // 选中的那一页由类表达，和设置窗口顶部同一套做法。
            Assert.Contains("selected", tabs[0].Classes);
            var model = (MacroEditorViewModel)window.DataContext!;
            model.ShowSettingsPage(MacroSettingsPage.Specific);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("selected", tabs[0].Classes);
            Assert.Contains("selected", tabs[1].Classes);

            Ui.CloseAndAnswer(window);
        });
    }

    /// <summary>
    /// 专属设置页放着这个宏自己的限速、启用/停用键与宏接口；跟随程序设置时限速那一格是灰的。
    /// </summary>
    [Fact]
    public void The_specific_page_holds_the_limit_and_the_switch_key_beside_the_interface()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (MacroEditorViewModel)window.DataContext!;
            model.ShowSettingsPage(MacroSettingsPage.Specific);
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.FindControl<StackPanel>("GeneralSettingsPage")!
                .IsEffectivelyVisible);
            Assert.True(window.FindControl<StackPanel>("SpecificSettingsPage")!
                .IsEffectivelyVisible);
            Assert.True(window.FindControl<ContentControl>("MacroInterfaceHost")!
                .IsEffectivelyVisible);

            var box = window.FindControl<FANumberBox>("MacroInputLimitBox")!;
            Assert.False(box.IsEnabled);

            model.FollowProgramInputRate = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsEnabled);

            Ui.CloseAndAnswer(window);
        });
    }

    /// <summary>专属设置页在最短的 360 像素列里也不许有格子出界。</summary>
    [Fact]
    public void The_specific_page_keeps_its_inputs_inside_the_360_pixel_column()
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
            model.ShowSettingsPage(MacroSettingsPage.Specific);
            Dispatcher.UIThread.RunJobs();

            var drawer = window.FindControl<Border>("MacroSettingsDrawer")!;
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

            Ui.CloseAndAnswer(window);
        });
    }

    /// <summary>保存被拦住时，抽屉翻到写着原因的那一页：名字在第一页，声明在第二页。</summary>
    [Fact]
    public void A_blocked_save_opens_the_page_that_holds_the_reason()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (MacroEditorViewModel)window.DataContext!;
            model.ShowSettingsPage(MacroSettingsPage.Specific);
            model.Name = string.Empty;
            Dispatcher.UIThread.RunJobs();

            model.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsGeneralSettingsPage);

            model.Name = "probe";
            model.DeclaredParameters.Add(new MacroParameter { Name = "same" });
            model.DeclaredParameters.Add(new MacroParameter { Name = "same" });
            Assert.Equal(MacroSettingsPage.Specific, model.PageThatBlocksSaving);

            Ui.CloseAndAnswer(window);
        });
    }

    /// <summary>
    /// 这两格写进宏里；新宏写的是「跟随程序设置」与「没有切换键」；翻页与开始录键都不算编辑。
    /// </summary>
    [Fact]
    public void The_specific_settings_are_written_onto_the_macro_and_kept_out_of_a_new_one()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (MacroEditorViewModel)window.DataContext!;
            Assert.False(model.IsDirty);

            model.ShowSettingsPage(MacroSettingsPage.Specific);
            model.IsCapturingToggleKey = true;
            Assert.False(model.IsDirty);
            model.IsCapturingToggleKey = false;

            Assert.Null(model.BuildMacro().InputRate);
            Assert.Equal(string.Empty, model.BuildMacro().ToggleKey);

            model.FollowProgramInputRate = false;
            model.InputLimitValue = 300;
            Assert.True(model.SetToggleKey("F9"));

            var saved = model.BuildMacro();
            Assert.Equal(300, saved.InputRate);
            Assert.Equal("F9", saved.ToggleKey);

            var fresh = new MacroEditorWindow(null, [macro]);
            fresh.Show();
            Dispatcher.UIThread.RunJobs();
            var freshModel = (MacroEditorViewModel)fresh.DataContext!;
            Assert.Null(freshModel.BuildMacro().InputRate);
            Assert.Equal(string.Empty, freshModel.BuildMacro().ToggleKey);

            Ui.CloseAndAnswer(window);
            Ui.CloseAndAnswer(fresh);
        });
    }
}
