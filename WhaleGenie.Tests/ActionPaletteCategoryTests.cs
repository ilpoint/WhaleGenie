using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class ActionPaletteCategoryTests
{
    [Fact]
    public void Basic_categories_keep_the_old_order_without_flow_or_conditions()
    {
        var palette = new ActionPaletteViewModel();

        Assert.Equal(
        [
            null,
            ActionCategory.Input,
            ActionCategory.Vision,
            ActionCategory.Uia,
            ActionCategory.Browser,
            ActionCategory.Script,
            ActionCategory.Process,
            ActionCategory.Data,
            ActionCategory.File,
            ActionCategory.Clipboard,
            ActionCategory.System,
        ], palette.Categories.Select(category => category.Value));
        Assert.Null(palette.SelectedCategory!.Value);
    }

    [Fact]
    public void Choosing_a_category_filters_actions_and_keeps_the_choice_across_docks()
    {
        var palette = new ActionPaletteViewModel();
        var input = palette.Categories.Single(category =>
            category.Value == ActionCategory.Input);

        palette.SelectedCategory = input;

        Assert.NotEmpty(palette.Cards);
        Assert.All(palette.Cards, card =>
        {
            Assert.Equal(ActionCategory.Input,
                ActionPaletteCategory(ActionCatalog.Find(card.Key)!.Category));
            Assert.Equal(Strings.Get("Add.Category.Input"), card.CategoryLabel);
            Assert.True(card.HasCategoryLabel);
            Assert.IsType<SolidColorBrush>(card.Accent);
            Assert.False(string.IsNullOrWhiteSpace(card.Description));
        });

        palette.SelectDock(ActionPaletteDock.Flow);
        palette.SelectDock(ActionPaletteDock.Basic);
        Assert.Same(input, palette.SelectedCategory);
    }

    [Fact]
    public void Search_stays_inside_the_selected_category()
    {
        var palette = new ActionPaletteViewModel();
        palette.SelectedCategory = palette.Categories.Single(category =>
            category.Value == ActionCategory.Input);

        palette.Search = "findImage";
        Assert.Empty(palette.Cards);

        palette.Search = "keyPress";
        Assert.Contains(palette.Cards, card => card.Key == "input.keyPress");
        Assert.All(palette.Cards, card => Assert.Equal(ActionCategory.Input,
            ActionPaletteCategory(ActionCatalog.Find(card.Key)!.Category)));
    }

    [Fact]
    public void Ocr_and_window_actions_join_their_reader_categories()
    {
        var palette = new ActionPaletteViewModel();
        var visionKey = ActionCatalog.RunnableActions
            .First(action => action.Category == ActionCategory.Ocr).Key;
        var windowKey = ActionCatalog.RunnableActions
            .First(action => action.Category == ActionCategory.Window).Key;

        palette.SelectedCategory = palette.Categories.Single(category =>
            category.Value == ActionCategory.Vision);
        Assert.Contains(palette.Cards, card => card.Key == visionKey);

        palette.SelectedCategory = palette.Categories.Single(category =>
            category.Value == ActionCategory.Process);
        Assert.Contains(palette.Cards, card => card.Key == windowKey);
    }

    [Fact]
    public void The_category_rail_belongs_only_to_the_basic_dock()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var palette = window.FindControl<ActionPaletteView>("ActionPalette")!;
            var model = (ActionPaletteViewModel)palette.DataContext!;
            var rail = palette.FindControl<Border>("BasicCategoryDock");

            Assert.NotNull(rail);
            Assert.True(rail!.IsVisible);
            var buttons = rail.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("Category"))
                .ToList();
            Assert.Equal(model.Categories.Count, buttons.Count);
            var allButton = buttons.First(button =>
                button.DataContext is ActionCategoryChoice { Value: null });
            Assert.Equal(Strings.Get("Add.AllActions"), ToolTip.GetTip(allButton));

            model.SelectDock(ActionPaletteDock.Flow);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsBasicDock);
            Assert.False(rail.IsEffectivelyVisible);

            model.SelectDock(ActionPaletteDock.Basic);
            Dispatcher.UIThread.RunJobs();
            Assert.True(rail.IsEffectivelyVisible);
            window.Close();
        });
    }

    [Fact]
    public void Actions_are_the_original_compact_wrapping_buttons()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var palette = window.FindControl<ActionPaletteView>("ActionPalette")!;

            var button = palette.GetVisualDescendants().OfType<Button>()
                .First(candidate => candidate.Classes.Contains("ActionCard"));
            Assert.Equal(32, button.Height);
            Assert.NotNull(button.GetVisualAncestors().OfType<WrapPanel>().FirstOrDefault());
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(button) as string));

            var point = button.TranslatePoint(
                new Avalonia.Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
                ?? default;
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.True(palette.HasArmedDrag);
            window.MouseUp(point, MouseButton.Left);
            window.Close();
        });
    }

    private static ActionCategory ActionPaletteCategory(ActionCategory category)
        => ActionCategoryPresentation.PickerCategory(category);
}
