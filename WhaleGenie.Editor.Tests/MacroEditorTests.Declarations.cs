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

// What a macro declares: clashing names, warnings that do not block saving, and the name rule.
public partial class MacroEditorTests
{
    [Fact]
    public void A_macro_whose_declarations_clash_says_so_above_the_steps()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "甲" };
            macro.Parameters.Add(new MacroParameter { Name = "同名" });
            macro.Parameters.Add(new MacroParameter { Name = "同名" });
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.True(viewModel.HasDeclarationProblems);
            Assert.Contains("同名", viewModel.DeclarationProblemSummary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_declaration_warning_does_not_block_saving_unfinished_work()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "还没写完" };
            macro.Results.Add(new MacroResult { Name = "答案" });
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.True(viewModel.HasDeclarationProblems);
            Assert.False(viewModel.HasBlockingDeclarationProblems);
            Assert.True(viewModel.CanSave);

            MacroItem? saved = null;
            viewModel.CloseRequested += result => saved = result;
            viewModel.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Equal("答案", Assert.Single(saved!.Results).Name);
        });
    }

    [Fact]
    public void A_macro_name_has_to_be_there_and_belong_to_one_macro()
    {
        Ui.Run(() =>
        {
            var first = new MacroItem { Name = "甲" };
            var second = new MacroItem { Name = "乙" };
            var window = new MacroEditorWindow(first, [first, second]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.False(viewModel.HasNameProblem);

            viewModel.Name = "乙";
            Assert.True(viewModel.HasNameProblem);
            Assert.Equal(Strings.Format("Editor.Problem.DuplicateMacroName", "乙"),
                viewModel.NameProblem);

            viewModel.Name = " ";
            Assert.True(viewModel.HasNameProblem);
            Assert.Equal(Strings.Get("Editor.Problem.EmptyMacroName"), viewModel.NameProblem);

            var closed = false;
            viewModel.CloseRequested += _ => closed = true;
            viewModel.SaveCommand.Execute(null);
            Assert.False(closed);
        });
    }
}
