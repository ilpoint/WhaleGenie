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

// Trigger cards, menu entries, and the submacro tabs that can close and come back.
public partial class MacroEditorChromeTests
{
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
            model.AddTriggerCommand.Execute(TriggerKind.Key);
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

            Ui.CloseAndAnswer(window);
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
            model.AddTriggerCommand.Execute(TriggerKind.Key);
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

            // 这份文档被改过（加了触发器、切过页签），关窗会问要不要保存。问题必须在这里答完，
            // 否则它会在下一个检查里才弹出来，把那个检查的调度循环留在运行中。
            Ui.CloseAndDiscard(window);
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

            Ui.CloseAndAnswer(window);
        });
    }
}
