using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// What the editor hands back when it closes, and what survives a saved package.
public partial class MacroEditorTests
{
    [Fact]
    public void The_macro_the_editor_ends_with_is_read_from_the_window()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            Assert.Null(window.Result);

            viewModel.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // The list is not waiting on the editor — it steps aside instead — so what the editor
            // ends with has to be readable from the window as it closes.
            Assert.NotNull(window.Result);
            Assert.Equal("probe", window.Result!.Name);

            var letGo = new MacroEditorWindow(new MacroItem { Name = "other" }, []);
            letGo.Show();
            Dispatcher.UIThread.RunJobs();
            letGo.Close(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(letGo.Result);
        });
    }


    /// <summary>
    /// What the editor builds has to be the same macro after a trip through a package. The editor
    /// alone turns its own state into a <see cref="MacroItem"/> — a field it forgets to copy is lost
    /// on the first save, silently, and shows up much later as "my step came back different".
    /// Comparing the JSON is the cheapest way to say "every field", including the ones nobody
    /// remembered to assert: the package holds exactly this shape.
    /// </summary>
    [Fact]
    public void What_the_editor_builds_survives_a_package()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-round-trip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Ui.Run(() =>
            {
                var (window, viewModel) = Open();
                viewModel.AppendSteps(
                [
                    Set("total", "1"),
                    new MacroStep
                    {
                        Type = "control.sequence",
                        Meta = StepMeta.New,
                        Parameters =
                        [
                            new StepParameter
                            {
                                Name = "steps",
                                Kind = ActionParameterKind.Steps,
                                Steps = [Set("inner", "$total")],
                            },
                        ],
                    },
                ]);
                Dispatcher.UIThread.RunJobs();

                viewModel.SaveCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                var built = window.Result;
                Assert.NotNull(built);

                var path = Path.Combine(folder, "probe.wgmacro");
                MacroPackage.Save(path, [built!], [], "0.0.0");
                var read = MacroPackage.Load(path).Macros.Single();

                Assert.Equal(built!.ToJson().ToJsonString(), read.ToJson().ToJsonString());
            });
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
