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

// What a snapshot keeps when it is written again, and what the window offers back.
public partial class RecoveryTests
{
    /// <summary>
    /// The snapshot is written from the same model the package is, so a number held as an expression
    /// has to come back as that expression. A zero in its place is what the next run would be
    /// offered: a macro that clicks the corner of the screen instead of following the mouse, with
    /// nothing on screen to say the work was changed on the way.
    /// </summary>
    [Fact]
    public void A_position_written_as_an_expression_comes_back_from_the_snapshot()
    {
        InOwnFile(_ => Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "click", BindKey = "F5" };
            macro.Steps.Add(new MacroStep
            {
                Type = "input.mouseClick",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "button", Kind = ActionParameterKind.Choice, Value = "left",
                    },
                    new StepParameter
                    {
                        Name = "x", Kind = ActionParameterKind.Number, Value = "$sys.mouseX",
                    },
                    new StepParameter
                    {
                        Name = "y", Kind = ActionParameterKind.Number, Value = "$sys.mouseY",
                    },
                ],
            });

            RecoveryStore.SaveProject([macro], [], null);

            var step = RecoveryStore.Load()!.Macros[0].Steps[0];
            Assert.Equal("$sys.mouseX", step.Parameters
                .First(parameter => parameter.Name == "x").Value);
            Assert.Equal("$sys.mouseY", step.Parameters
                .First(parameter => parameter.Name == "y").Value);
        }));
    }

    /// <summary>
    /// A snapshot that cannot be read is taken away, and work written afterwards is written out
    /// again: remembering that something was written to a file that is no longer there would leave
    /// the next run with nothing to be offered and the work behind it unsaved.
    /// </summary>
    [Fact]
    public void Work_written_after_an_unreadable_snapshot_went_is_written_again()
    {
        InOwnFile(path =>
        {
            RecoveryStore.SaveProject([Named("login")], [], null);
            File.WriteAllText(path, "this is not a snapshot");
            Assert.Null(RecoveryStore.Load());
            Assert.False(File.Exists(path));

            // The same work as before: it is the file that went, not the work that moved.
            RecoveryStore.SaveProject([Named("login")], [], null);

            Assert.True(File.Exists(path));
            Assert.NotNull(RecoveryStore.Load());
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
}
