using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The run button of the form a step is written in. It tries one step out, which is a question about
/// the machine the user is looking at rather than about this window — so it stands aside the way a
/// picker does: every window of this program goes for as long as the step is out, and comes back when
/// it is over. A window left in front of what the step reads, clicks or types into is the one thing
/// that is always there, and it makes the trial answer a question nobody asked.
/// </summary>
public class TrialRunAsideTests
{
    [Fact]
    public void A_trial_run_puts_this_programs_windows_away_and_brings_them_back()
    {
        Ui.RunAsync(async () =>
        {
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [],
                "control.delay");

            // A headless session has no desktop to read the program's windows off, so the window
            // this check is about is handed over as the program's own.
            var desk = AppWindows.Open;
            AppWindows.Open = () => [window];
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var form = (AddActionViewModel)window.DataContext!;
                form.Parameters.First(parameter => parameter.Definition.Name == "ms")
                    .WriteValue("800");
                Dispatcher.UIThread.RunJobs();
                Assert.True(form.CanRun, form.ValidationMessage);

                window.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, Strings.Get("Add.RunStep")))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                await Task.Delay(200);
                Assert.False(window.IsVisible,
                    "the window stayed on the screen while the step was out");

                for (var waited = 0; waited < 6000 && !window.IsVisible; waited += 50)
                {
                    await Task.Delay(50);
                }

                Assert.True(window.IsVisible, "the window did not come back");
            }
            finally
            {
                AppWindows.Open = desk;
            }

            return true;
        });
    }

    /// <summary>
    /// Three ways a step is tried out of this form — the run button's look, the run button's real
    /// step, and a field's own test — and each one has to open the scope before it touches the
    /// machine. What the scope does with the windows is checked above; this is here so a fourth way
    /// of trying a step cannot quietly ask the screen with this program's windows still in front.
    /// </summary>
    [Fact]
    public void Every_trial_of_the_form_puts_this_programs_windows_away_first()
    {
        var source = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views",
            "ActionEditorView.axaml.cs"));

        Assert.Contains("using (AppWindows.StepAside(HostWindow))", source, StringComparison.Ordinal);
        Assert.Contains("ScreenComposed.Wait();", source, StringComparison.Ordinal);

        foreach (var trial in new[] { "RunLookingAsync", "RunForRealAsync", "OnTestStep" })
        {
            Assert.Contains("Away", MemberOf(source, trial), StringComparison.Ordinal);
        }
    }

    /// <summary>The body of one member of the view, from its declaration to the next one.</summary>
    private static string MemberOf(string source, string name)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, line => Declares(line, name));
        Assert.True(start >= 0, $"the form has no {name}");

        var end = Array.FindIndex(lines, start + 1, line =>
            line.StartsWith("    private ", StringComparison.Ordinal)
            || line.StartsWith("    internal ", StringComparison.Ordinal)
            || line.StartsWith("    public ", StringComparison.Ordinal));

        return string.Join("\n", lines[start..(end < 0 ? lines.Length : end)]);
    }

    /// <summary>True for the line that declares <paramref name="name"/> rather than one that calls it.</summary>
    private static bool Declares(string line, string name)
        => line.Contains(name + "(", StringComparison.Ordinal)
            && (line.Contains("private ", StringComparison.Ordinal)
                || line.Contains("internal ", StringComparison.Ordinal));

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
