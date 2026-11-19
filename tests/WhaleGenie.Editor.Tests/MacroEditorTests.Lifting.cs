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
using WhaleGenie.Core.Expressions;
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
                Type = ValueKind.Number,
                Required = true,
            });

            var counted = Set("中间", Formula.Binary("+", Formula.Name("数"), Formula.Number("1", 1d)));
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
            Assert.Equal(ValueKind.Number, argument.Type);
            Assert.True(argument.Required);

            // Nothing said what the written name holds — a plain assignment is text until something
            // declares otherwise — so it is declared text rather than guessed at.
            var result = Assert.Single(lifted.Results);
            Assert.Equal("中间", result.Name);
            Assert.Equal(ValueKind.Text, result.Type);
        });
    }
}
