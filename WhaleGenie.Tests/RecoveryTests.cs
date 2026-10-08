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

/// <summary>
/// The safety net under work that has not been saved. A crash or a power cut used to take the
/// macro list, and whatever the macro editor was holding, with it: neither reaches disk until the
/// user writes it out. The work is written to a snapshot beside the program as it changes, and the
/// next run offers it back instead of coming up empty.
/// </summary>
public class RecoveryTests
{
    [Fact]
    public void The_macro_list_survives_a_stop_in_the_snapshot()
    {
        // Reading a step back runs it past the action catalogue, which builds its icons with
        // Avalonia geometry, so the round trip belongs on the user-interface thread.
        InOwnFile(_ => Ui.Run(() =>
        {
            var macro = Sample();
            macro.IsEnabled = false;

            RecoveryStore.SaveProject([macro], [], @"C:\work\macros.wgmacro");

            var found = RecoveryStore.Load();

            Assert.NotNull(found);
            var only = Assert.Single(found!.Macros);
            Assert.Equal("login", only.Name);
            Assert.False(only.IsEnabled);
            Assert.Equal("input.keyPress", Assert.Single(only.Steps).Type);
            Assert.Equal(@"C:\work\macros.wgmacro", found.PackagePath);
            Assert.Null(found.Editor);
        }));
    }

    [Fact]
    public void The_shared_variables_come_back_with_the_list()
    {
        InOwnFile(_ =>
        {
            var variable = new VariableDefinition
            {
                Name = "notes",
                Scope = VariableScope.Global,
                Type = "text",
                DefaultValue = "hi",
                Description = "what to say",
            };

            // A package is what makes an empty list something worth recovering; without one there
            // would be nothing to lose.
            RecoveryStore.SaveProject([], [variable], @"C:\work\macros.wgmacro");

            var found = RecoveryStore.Load();

            Assert.NotNull(found);
            Assert.Empty(found!.Macros);
            var only = Assert.Single(found.Globals);
            Assert.Equal("notes", only.Name);
            Assert.Equal(VariableScope.Global, only.Scope);
            Assert.Equal("hi", only.DefaultValue);
            Assert.Equal("what to say", only.Description);
        });
    }

    [Fact]
    public void The_project_and_the_open_editor_are_kept_side_by_side()
    {
        InOwnFile(_ =>
        {
            RecoveryStore.SaveProject([Named("login")], [], null);
            RecoveryStore.SaveEditor(Named("draft"), "login");

            var both = RecoveryStore.Load();

            Assert.NotNull(both);
            Assert.Single(both!.Macros);
            Assert.NotNull(both.Editor);
            Assert.Equal("draft", both.Editor!.Macro.Name);
            Assert.Equal("login", both.Editor.Replaces);

            // The editor handing its macro over, or being let go, must leave the list alone.
            RecoveryStore.ClearEditor();

            var onlyList = RecoveryStore.Load();
            Assert.NotNull(onlyList);
            Assert.Single(onlyList!.Macros);
            Assert.Null(onlyList.Editor);

            // And the other way round: saving the list must not take the editor's draft with it.
            RecoveryStore.SaveEditor(Named("draft"), string.Empty);
            RecoveryStore.ClearProject();

            var onlyEditor = RecoveryStore.Load();
            Assert.NotNull(onlyEditor);
            Assert.Empty(onlyEditor!.Macros);
            Assert.Null(onlyEditor.PackagePath);
            Assert.NotNull(onlyEditor.Editor);
            Assert.Equal(string.Empty, onlyEditor.Editor!.Replaces);
        });
    }

    [Fact]
    public void A_snapshot_with_nothing_left_in_it_is_removed()
    {
        InOwnFile(path =>
        {
            RecoveryStore.SaveProject([Named("login")], [], null);
            Assert.True(File.Exists(path));

            // Saving the project, or letting the work go, takes the last thing out of the file.
            RecoveryStore.ClearProject();

            Assert.False(File.Exists(path));
            Assert.Null(RecoveryStore.Load());
        });
    }

    [Fact]
    public void A_snapshot_that_cannot_be_read_is_not_offered()
    {
        InOwnFile(path =>
        {
            File.WriteAllText(path, "this is not a snapshot");

            Assert.Null(RecoveryStore.Load());
        });
    }

    [Fact]
    public void Recovered_work_still_counts_as_unsaved()
    {
        var viewModel = new MainViewModel();

        viewModel.RestoreFrom(new RecoveryContents
        {
            Macros = [Named("login")],
            Globals = [],
            PackagePath = @"C:\work\macros.wgmacro",
        });

        Assert.Single(viewModel.Macros);
        Assert.Equal(@"C:\work\macros.wgmacro", viewModel.CurrentPath);

        // The snapshot is not the package: until the user writes it out, this is unsaved work.
        Assert.True(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public void The_window_offers_back_what_the_last_run_left()
    {
        InOwnFile(_ =>
        {
            Ui.RunAsync(async () =>
            {
                RecoveryStore.SaveProject([Named("login")], [], null);

                var asked = 0;
                var window = new MainWindow
                {
                    DataContext = new MainViewModel(),
                    AskToRecover = _ =>
                    {
                        asked++;
                        return Task.FromResult(ConfirmChoice.Primary);
                    },
                };
                window.Show();

                await window.OfferRecoveryAsync();

                var viewModel = (MainViewModel)window.DataContext!;
                Assert.Equal(1, asked);
                Assert.Single(viewModel.Macros);
                Assert.Equal("login", viewModel.Macros[0].Name);
                Assert.True(viewModel.HasUnsavedChanges);

                // The snapshot stays where it was: it is what keeps the recovered work safe until
                // the user finally saves it.
                Assert.NotNull(RecoveryStore.Load());

                CloseWithUnsavedWork(window);
                return true;
            });
        });
    }

    [Fact]
    public void Saying_the_offer_is_not_wanted_clears_the_snapshot()
    {
        InOwnFile(path =>
        {
            Ui.RunAsync(async () =>
            {
                RecoveryStore.SaveProject([Named("login")], [], null);

                var window = new MainWindow
                {
                    DataContext = new MainViewModel(),
                    AskToRecover = _ => Task.FromResult(ConfirmChoice.Secondary),
                };
                window.Show();

                await window.OfferRecoveryAsync();

                Assert.Empty(((MainViewModel)window.DataContext!).Macros);
                Assert.False(File.Exists(path));

                window.Close();
                Dispatcher.UIThread.RunJobs();
                return true;
            });
        });
    }

    [Fact]
    public void Nothing_is_offered_when_the_last_run_ended_clean()
    {
        InOwnFile(_ =>
        {
            Ui.RunAsync(async () =>
            {
                var asked = 0;
                var window = new MainWindow
                {
                    DataContext = new MainViewModel(),
                    AskToRecover = _ =>
                    {
                        asked++;
                        return Task.FromResult(ConfirmChoice.Primary);
                    },
                };
                window.Show();

                await window.OfferRecoveryAsync();

                Assert.Equal(0, asked);

                window.Close();
                Dispatcher.UIThread.RunJobs();
                return true;
            });
        });
    }

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
            RecoveryStore.SaveProject([Named("login")], [], null);
            var takenAt = TakenAt(path);

            RecoveryStore.SaveProject([Named("login")], [], null);

            // The file is only what a stop without notice is offered back, and the time it says it
            // was taken is the one thing in it a rewrite would move. Writing work that has not
            // moved would leave that time claiming a snapshot seconds old about work hours old.
            Assert.Equal(takenAt, TakenAt(path));
        });
    }

    [Fact]
    public void The_snapshot_follows_a_change_that_arrives_after_the_list_was_already_unsaved()
    {
        InOwnFile(_ =>
        {
            Ui.RunAsync(async () =>
            {
                await Briefly(20, async () =>
                {
                    var window = new MainWindow { DataContext = new MainViewModel() };
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    var viewModel = (MainViewModel)window.DataContext!;

                    // The first macro is what makes the list unsaved, and it is written at once.
                    viewModel.AddMacro(Named("login"));
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(RecoveryStore.Load()!.Macros[0].IsEnabled);

                    // Arming and disarming a macro is written into the package, so it counts, and
                    // it arrives while the list is already unsaved: nothing about the dirty flag or
                    // the list itself moves for it, so only a signal that stands for the change
                    // can carry it into the snapshot.
                    viewModel.ToggleMacro(viewModel.Macros[0]);
                    await Task.Delay(300);

                    Assert.False(RecoveryStore.Load()!.Macros[0].IsEnabled);

                    CloseWithUnsavedWork(window);
                });

                return true;
            });
        });
    }

    [Fact]
    public void A_list_sitting_still_is_not_written_over_and_over()
    {
        InOwnFile(path =>
        {
            Ui.RunAsync(async () =>
            {
                await Briefly(20, async () =>
                {
                    var window = new MainWindow { DataContext = new MainViewModel() };
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    var viewModel = (MainViewModel)window.DataContext!;

                    viewModel.AddMacro(Named("login"));
                    Dispatcher.UIThread.RunJobs();
                    var takenAt = TakenAt(path);
                    Assert.NotNull(takenAt);

                    // Long enough for many rounds of a timer that wrote whether the work moved or
                    // not. Nothing moved here, so the snapshot that is already there stands.
                    await Task.Delay(300);

                    Assert.Equal(takenAt, TakenAt(path));

                    CloseWithUnsavedWork(window);
                });

                return true;
            });
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
                    // says nothing about.
                    editor.AddStep(new MacroStep { Type = "control.delay" });
                    await Task.Delay(300);

                    Assert.Equal(["control.log", "control.delay"],
                        RecoveryStore.Load()!.Editor!.Macro.Steps.Select(step => step.Type));

                    window.Close();
                    Dispatcher.UIThread.RunJobs();
                });

                return true;
            });
        });
    }

    /// <summary>
    /// Closes a window that is holding unsaved work. The question it asks on the way out is
    /// answered with "discard", so the close finishes instead of leaving a dialog behind.
    /// </summary>
    private static void CloseWithUnsavedWork(MainWindow window)
    {
        window.AskToSaveProject = () => Task.FromResult(ConfirmChoice.Secondary);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Runs a body with the snapshot's writer put on a short interval, so a check can wait the
    /// pause out instead of the seconds a real one keeps. The wait hands the thread back, which is
    /// what lets the writer's timer fire at all.
    /// </summary>
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

    /// <summary>The time the snapshot says it was taken, which a rewrite moves and nothing else does.</summary>
    private static string? TakenAt(string path)
        => (JsonNode.Parse(File.ReadAllText(path)) as JsonObject)?["savedUtc"]?.GetValue<string>();

    private static MacroItem Named(string name) => new() { Name = name, Trigger = "F5" };

    /// <summary>A macro with a step, so a round trip carries more than the name.</summary>
    private static MacroItem Sample()
    {
        var macro = new MacroItem { Name = "login", Trigger = "F5" };
        macro.Steps.Add(new MacroStep
        {
            Type = "input.keyPress",
            Parameters = [new StepParameter { Name = "key", Kind = ActionParameterKind.Text, Value = "A" }],
        });
        return macro;
    }

    /// <summary>
    /// Points the snapshot at a file of its own under the temporary directory, so a check never
    /// reads the one this machine keeps and never leaves anything behind.
    /// </summary>
    private static void InOwnFile(Action<string> body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "recovery.json");
        var previous = RecoveryStore.FilePath;
        RecoveryStore.FilePath = path;
        try
        {
            body(path);
        }
        finally
        {
            RecoveryStore.FilePath = previous;
            Directory.Delete(folder, true);
        }
    }
}
