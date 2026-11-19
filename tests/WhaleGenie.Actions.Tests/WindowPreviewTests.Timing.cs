using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// A rule changed while typing, and where the list stands under its field.
public partial class WindowPreviewTests
{
    [Fact]
    public void A_rule_changed_while_typing_is_read_once_the_typing_pauses()
    {
        Ui.RunAsync(async () =>
        {
            var interval = WindowPreviewViewModel.Interval;
            WindowPreviewViewModel.Interval = TimeSpan.FromMilliseconds(30);
            try
            {
                var windows = Desk()
                    .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                    .Add(2, "Calculator", "calc", "CalcFrame");

                var (viewModel, preview) = Rule(windows, "notes");
                Assert.Equal("Notepad - notes.txt", Assert.Single(preview.Rows).Title);

                PutWindowRule(viewModel, "calc", WindowHow.TitleContains);

                // Reading the desktop once per keystroke is what waiting for the pause avoids.
                Assert.Equal("Notepad - notes.txt", Assert.Single(preview.Rows).Title);

                await Task.Delay(200);
                Assert.Equal("Calculator", Assert.Single(preview.Rows).Title);
            }
            finally
            {
                WindowPreviewViewModel.Interval = interval;
            }

            return true;
        });
    }

    [Fact]
    public void The_list_stands_under_the_field_the_rule_is_about()
    {
        Ui.Run(() =>
        {
            var window = Shell("window.exists");
            var viewModel = (AddActionViewModel)window.DataContext!;
            viewModel.Windows = Desk().Add(1, "Notepad - notes.txt", "notepad", "Notepad");
            PutWindowRule(viewModel, "Notepad", WindowHow.TitleContains);

            var preview = viewModel.WindowPreview!;
            preview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            double Top(Control control) => control.TranslatePoint(default, window)!.Value.Y;

            var field = Parameter(viewModel, "window");
            var box = window.GetVisualDescendants().OfType<Border>()
                .First(one => one.IsEffectivelyVisible && ReferenceEquals(one.DataContext, field));
            var list = window.GetVisualDescendants().OfType<TextBlock>()
                .First(one => one.IsEffectivelyVisible && one.Text == preview.Summary);

            // Under the captured-rule field it is about.
            Assert.True(Top(list) > Top(box), "the list is not under the window rule");
        });
    }
}
