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

// The windows a rule finds: the pattern it reads and what the list says about it.
public partial class WindowPreviewTests
{
    [Fact]
    public void The_windows_a_rule_recognises_are_listed()
    {
        Ui.Run(() =>
        {
            var windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Calculator", "calc", "CalcFrame")
                .Add(3, "Paint", "mspaint", "MSPaintClass");

            var (_, preview) = Rule(windows, "notes");

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            var row = Assert.Single(preview.Rows);
            Assert.Equal("Notepad - notes.txt", row.Title);

            // What is compared is the title, so the line does not say the title twice.
            Assert.False(row.HasPart);
            Assert.Empty(preview.Note);
            Assert.Empty(preview.More);
        });
    }

    [Fact]
    public void A_rule_that_finds_nothing_says_so()
    {
        Ui.Run(() =>
        {
            var (_, preview) = Rule(Desk().Add(1, "Notepad", "notepad", "Notepad"), "nothing like it");

            Assert.Equal(Strings.Get("Add.WindowPreview.None"), preview.Summary);
            Assert.Empty(preview.Rows);
        });
    }

    [Fact]
    public void A_window_is_recognised_through_a_pattern_the_way_a_run_would()
    {
        Ui.Run(() =>
        {
            var windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Notepad - other.txt", "notepad", "Notepad");

            var (_, preview) = Rule(windows, @"^Notepad - notes", how: "titleRegex");

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            Assert.Equal("Notepad - notes.txt", Assert.Single(preview.Rows).Title);
        });
    }

    [Fact]
    public void A_pattern_that_will_not_read_is_said_in_the_words_a_run_uses()
    {
        Ui.Run(() =>
        {
            var (_, preview) = Rule(Desk().Add(1, "Notepad", "notepad", "Notepad"), "[",
                how: "titleRegex");

            // The same words the run reports it with, because it is the same mistake.
            Assert.Equal(Strings.Format("Run.BadPattern", "["), preview.Summary);
            Assert.Empty(preview.Rows);
        });
    }

    [Fact]
    public void Comparing_a_part_other_than_the_title_shows_that_part()
    {
        Ui.Run(() =>
        {
            var windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Calculator", "calc", "CalcFrame");

            var (_, preview) = Rule(windows, "calc", how: "process");

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            var row = Assert.Single(preview.Rows);
            Assert.Equal("Calculator", row.Title);
            Assert.Equal(Strings.Get("WindowPart.Process"), row.PartLabel);
            Assert.Equal("calc", row.Part);
        });
    }

    [Fact]
    public void A_text_left_empty_is_the_window_in_front()
    {
        Ui.Run(() =>
        {
            var windows = Desk()
                .Add(1, "Notepad - notes.txt", "notepad", "Notepad")
                .Add(2, "Calculator", "calc", "CalcFrame");

            var (_, preview) = Rule(windows, "");

            // Nothing said means the frontmost window, the same answer the run gives.
            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);
            Assert.Equal("Notepad - notes.txt", Assert.Single(preview.Rows).Title);
            Assert.Equal(Strings.Get("Add.WindowPreview.Front"), preview.Note);
        });
    }
    [Fact]
    public void Only_the_first_few_are_listed_and_the_rest_counted()
    {
        Ui.Run(() =>
        {
            var windows = Desk();
            for (var handle = 1; handle <= 7; handle++)
            {
                windows.Add(handle, $"Notepad - document {handle}.txt", "notepad", "Notepad");
            }

            var (_, preview) = Rule(windows, "Notepad");

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 7), preview.Summary);
            Assert.Equal(5, preview.Rows.Count);
            Assert.Equal(Strings.Format("Add.WindowPreview.More", 2), preview.More);
        });
    }

    [Fact]
    public void Refresh_reads_the_windows_again()
    {
        Ui.Run(() =>
        {
            var windows = Desk().Add(1, "Notepad - notes.txt", "notepad", "Notepad");
            var (_, preview) = Rule(windows, "Notepad");
            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 1), preview.Summary);

            // A window opened since the last look is what the button beside the list is for.
            windows.Add(2, "Notepad - second.txt", "notepad", "Notepad");
            preview.RefreshCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Strings.Format("Add.WindowPreview.Count", 2), preview.Summary);
            Assert.Equal(2, preview.Rows.Count);
        });
    }
}
