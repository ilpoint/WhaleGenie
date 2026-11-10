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

// Lifting steps out into a macro of their own, and the declarations that follow.
public partial class MacroEditorTests
{
    /// <summary>
    /// Lifting takes the selected steps out of the macro and leaves a call to a new macro in their
    /// place. The values the stretch read become that macro's arguments and the values it wrote
    /// become its results, so the steps around the call go on reading the same names — and the call
    /// is written the way the dialog would have written it, rather than as a second magic shape.
    /// </summary>
    [Fact]
    public void A_stretch_of_steps_can_be_lifted_into_a_macro_of_its_own()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();

            var started = Set("开始", "0");
            var counted = Set("中间", "$开始 + 1");
            var noted = Set("标记", "$中间");
            var after = Set("之后", "$中间");
            viewModel.Steps.Add(started);
            viewModel.Steps.Add(counted);
            viewModel.Steps.Add(noted);
            viewModel.Steps.Add(after);
            viewModel.SetSelection([counted, noted]);

            MacroItem? lifted = null;
            viewModel.MacroLifted += macro => lifted = macro;
            viewModel.LiftSelectedCommand.Execute(null);

            Assert.NotNull(lifted);
            Assert.Equal([counted, noted], lifted!.Steps);
            Assert.Equal(["开始"], lifted.Parameters.Select(parameter => parameter.Name));
            Assert.True(lifted.Parameters[0].Required);
            // The names the stretch writes are offered back in a settled order, so the same stretch
            // lifted twice writes the same call.
            Assert.Equal(["中间", "标记"], lifted.Results.Select(result => result.Name));

            // The stretch is out of the macro and a call stands where it was.
            Assert.Equal(3, viewModel.Steps.Count);
            Assert.Same(started, viewModel.Steps[0]);
            var call = viewModel.Steps[1];
            Assert.Equal("control.runMacro", call.Type);
            Assert.Equal(lifted.Name, call.Text("macro"));
            // What the call passes is one list of rows: the lifted macro's argument, and the value
            // the stretch already had under that name.
            var passed = call.Parameters.First(parameter => parameter.Name == FieldNames.Arguments).Rows;
            Assert.Equal(["开始"], passed.Select(row => row.Text(FieldNames.Name)));
            Assert.Equal(["$开始"], passed.Select(row => row.Text(FieldNames.Value)));
            Assert.Same(after, viewModel.Steps[2]);

            var wanted = call.Parameters.First(parameter => parameter.Name == "returns").Rows;
            Assert.Equal(["中间", "标记"], wanted.Select(row => row.Text("key")));
            Assert.Equal(["中间", "标记"], wanted.Select(row => row.Text("name")));

            // The call is one the project can reach, so the step is not marked as calling something
            // that is not there; and the name it brings back is what the step after it reads.
            Assert.Empty(viewModel.Problems.Select(problem => problem.Message));
            Assert.Contains(lifted, window.LiftedMacros);

            // A lift that is undone leaves a macro nothing calls, and a macro nothing calls has no
            // business joining the project.
            viewModel.Steps.Remove(call);
            Assert.Empty(window.LiftedMacros);
        });
    }

    /// <summary>
    /// A lifted macro takes the values the stretch read, and each of them keeps the kind it already
    /// had. Calling them all text would make the call site say a number is text, which is a
    /// disagreement the author would have to find by reading the declaration.
    /// </summary>
    [Fact]
    public void A_lifted_macro_declares_the_kind_its_arguments_already_had()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "主" };
            macro.Parameters.Add(new MacroParameter
            {
                Name = "数",
                Type = ActionReturnType.Number,
                Required = true,
            });

            var counted = Set("中间", "$数 + 1");
            macro.Steps.Add(counted);

            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            MacroItem? lifted = null;
            viewModel.MacroLifted += made => lifted = made;
            viewModel.SetSelection([counted]);
            viewModel.LiftSelectedCommand.Execute(null);

            Assert.NotNull(lifted);
            var argument = Assert.Single(lifted!.Parameters);
            Assert.Equal("数", argument.Name);
            Assert.Equal(ActionReturnType.Number, argument.Type);
            Assert.True(argument.Required);

            // Nothing said what the written name holds — a plain assignment is text until something
            // declares otherwise — so it is declared text rather than guessed at.
            var result = Assert.Single(lifted.Results);
            Assert.Equal("中间", result.Name);
            Assert.Equal(ActionReturnType.Text, result.Type);
        });
    }
}
