using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Closing with unsaved work, and saving from the prompt.
public partial class GlobalVariablesTests
{
    [Fact]
    public void Closing_dirty_work_can_be_cancelled_or_discarded()
    {
        Ui.RunAsync(async () =>
        {
            var window = new GlobalVariablesWindow([], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var manager = (GlobalVariablesViewModel)window.DataContext!;
            manager.NewCommand.Execute(null);
            manager.Selected!.Name = "kept";
            Dispatcher.UIThread.RunJobs();

            window.Close(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(),
                "closing a window with unsaved work did not ask anything");

            var first = Assert.Single(window.OwnedWindows.OfType<ConfirmDialog>());
            var firstPrompt = (ConfirmDialogViewModel)first.DataContext!;
            Assert.Equal(Strings.Get("GlobalVariables.UnsavedTitle"), firstPrompt.Header);
            firstPrompt.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => !window.OwnedWindows.Any(), "the prompt did not close");
            Assert.True(window.IsVisible);

            window.Close(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(),
                "closing it a second time did not ask again");

            var second = Assert.Single(window.OwnedWindows.OfType<ConfirmDialog>());
            var secondPrompt = (ConfirmDialogViewModel)second.DataContext!;
            secondPrompt.SecondaryCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => !window.IsVisible, "discarding did not close the window");
            return true;
        });
    }

    [Fact]
    public void Saving_from_the_close_prompt_saves_the_draft()
    {
        Ui.RunAsync(async () =>
        {
            var window = new GlobalVariablesWindow([], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var manager = (GlobalVariablesViewModel)window.DataContext!;
            IReadOnlyList<VariableDefinitionDraft>? saved = null;
            manager.Saved += rows => saved = rows;
            manager.NewCommand.Execute(null);
            manager.Selected!.Name = "fromClose";
            Dispatcher.UIThread.RunJobs();

            window.Close(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(),
                "closing a window with unsaved work did not ask anything");
            var confirm = Assert.Single(window.OwnedWindows.OfType<ConfirmDialog>());
            var prompt = (ConfirmDialogViewModel)confirm.DataContext!;
            prompt.PrimaryCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await Ui.Until(() => !window.IsVisible, "saving from the prompt did not close the window");

            Assert.Equal("fromClose", Assert.Single(saved!).Name);
            Assert.False(window.IsVisible);
            return true;
        });
    }

    [Fact]
    public void The_window_uses_the_project_dialog_shell_and_one_status_area()
    {
        var xaml = File.ReadAllText(ViewSource.PathOf("GlobalVariablesWindow.axaml"));

        // 外壳是库的 `FAAppWindow`（`views:AppWindow`）：标题栏与关闭键由它画，窗口自己不画。
        Assert.Contains("<views:AppWindow", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"CloseButton\"", xaml, System.StringComparison.Ordinal);
        Assert.Contains("Name=\"SearchBox\"", xaml, System.StringComparison.Ordinal);
        Assert.Contains("Name=\"ProblemStatus\"", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding NamePrompt}\"", xaml, System.StringComparison.Ordinal);
        Assert.Contains("Gesture=\"Ctrl+S\"", xaml, System.StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{Binding CanSave}\"", xaml,
            System.StringComparison.Ordinal);
    }
}
