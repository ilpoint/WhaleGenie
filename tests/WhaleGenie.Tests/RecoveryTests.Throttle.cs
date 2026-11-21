using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// How often the snapshot is written, and what a still list does.
public partial class RecoveryTests
{
    [Fact]
    public void A_burst_of_changes_becomes_one_write_and_the_clock_then_stops()
    {
        Ui.RunAsync(async () =>
        {
            var writes = 0;
            var writer = new RecoveryWriter(() => writes++);

            await Briefly(20, async () =>
            {
                // A burst is one write once the changes pause, not one write each.
                writer.Changed();
                writer.Changed();
                writer.Changed();
                Assert.Equal(0, writes);

                await Task.Delay(300);
                Assert.Equal(1, writes);

                // Nothing has moved since, so the clock goes quiet instead of writing the same
                // snapshot out again: the file is only there to be found after a stop without
                // notice, and rewriting it over work that has not moved says it was taken just now
                // about work that may be hours old.
                await Task.Delay(300);
                Assert.Equal(1, writes);

                // The change that first makes work unsaved is written at once rather than by the
                // clock: a macro pasted in and lost to a crash a moment later must not be waiting
                // on the interval to be kept.
                writer.Now();
                Assert.Equal(2, writes);

                writer.Stop();
            });

            return true;
        });
    }

    [Fact]
    public void Writing_the_same_work_twice_leaves_the_file_as_it_was()
    {
        InOwnFile(path =>
        {
            RecoveryStore.SaveProject([Named("login")], new ProjectTriggers(), [], null);
            var takenAt = TakenAt(path);

            RecoveryStore.SaveProject([Named("login")], new ProjectTriggers(), [], null);

            // The file is only what a stop without notice is offered back, and the time it says it
            // was taken is the one thing in it a rewrite would move. Writing work that has not
            // moved would leave that time claiming a snapshot seconds old about work hours old.
            Assert.Equal(takenAt, TakenAt(path));
        });
    }

    [Fact]
    public void The_draft_follows_an_edit_made_after_it_was_first_written()
    {
        InOwnFile(_ =>
        {
            Ui.RunAsync(async () =>
            {
                await Briefly(20, async () =>
                {
                    var window = new MacroEditorWindow();
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    var editor = (MacroEditorViewModel)window.DataContext!;

                    // The first step is what makes the draft worth keeping, and it is written at
                    // once: a macro is only in this window until it is handed to the list.
                    editor.AddStep(new MacroStep { Type = "control.log" });
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(["control.log"],
                        RecoveryStore.Load()!.Editor!.Macro.Steps.Select(step => step.Type));

                    // The edit arrives while the draft is already unsaved, which the dirty flag
                    // says nothing about. The store writes on its own clock, so the check waits for
                    // what it is about to assert rather than for a fixed time: a machine busy with
                    // other checks used to run out of that time and fail a case that was working.
                    editor.AddStep(new MacroStep { Type = "control.delay" });
                    var written = await Until(() =>
                        RecoveryStore.Load()?.Editor?.Macro.Steps.Select(step => step.Type).ToArray()
                            is ["control.log", "control.delay"]);

                    Assert.True(written, "the draft never took the second step");

                    Ui.CloseAndAnswer(window);
                    Dispatcher.UIThread.RunJobs();
                });

                return true;
            });
        });
    }

    private static async Task<bool> Until(Func<bool> holds, int milliseconds = 5000)
    {
        var started = Environment.TickCount64;
        while (!holds())
        {
            if (Environment.TickCount64 - started > milliseconds)
            {
                return false;
            }

            await Task.Delay(20);
        }

        return true;
    }

    private static async Task Briefly(int milliseconds, Func<Task> body)
    {
        var interval = RecoveryWriter.Interval;
        RecoveryWriter.Interval = TimeSpan.FromMilliseconds(milliseconds);
        try
        {
            await body();
        }
        finally
        {
            RecoveryWriter.Interval = interval;
        }
    }
}
