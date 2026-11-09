using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The values a macro declares for itself: where they live in the file, what the variable wall says
/// about them, what the design-time check makes of them, and what the manager does with a batch of
/// names when the window is saved.
/// </summary>
[RestoreSharedState]
public class LocalVariablesTests
{
    private static MacroLocalVariable Local(string name, string initial = "",
        ActionReturnType type = ActionReturnType.Text) => new()
        {
            Name = name,
            Type = type,
            Initial = initial,
        };

    private static MacroStep Reads(string expression) => new()
    {
        Type = "control.log",
        Meta = StepMeta.New,
        Parameters =
        [
            new StepParameter
            {
                Name = "text",
                Kind = ActionParameterKind.Text,
                Value = expression,
            },
        ],
    };

    [Fact]
    public void A_declared_local_survives_a_save_and_a_load()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-locals-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var macro = new MacroItem { Name = "probe" };
            macro.Locals.Add(Local("total", "5", ActionReturnType.Number));
            macro.Locals.Add(Local("label", "共 $total 件"));

            var path = Path.Combine(folder, "probe.wgmacro");
            MacroPackage.Save(path, [macro], [], "0.0.0");
            var read = MacroPackage.Load(path).Macros.Single();

            Assert.Equal(2, read.Locals.Count);
            Assert.Equal("total", read.Locals[0].Name);
            Assert.Equal(ActionReturnType.Number, read.Locals[0].Type);
            Assert.Equal("5", read.Locals[0].Initial);
            Assert.Equal("共 $total 件", read.Locals[1].Initial);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_declared_local_shows_what_it_starts_at()
    {
        var rows = VariableChoices.For([], null, null,
            [Local("total", "5", ActionReturnType.Number)]);

        var row = Assert.Single(rows, choice => choice.Name == "total");

        Assert.Equal(VariableOrigin.DeclaredLocal, row.Origin);
        Assert.Equal(VariableGroup.Local, row.Group);
        Assert.Equal("number", row.Type);

        // "No value until the macro runs" would be a lie: this one is written before the first
        // step, so the wall can say what it holds.
        Assert.Equal("5", row.ContentPreview);
    }

    [Fact]
    public void A_step_reading_a_declared_local_is_not_reading_a_name_nothing_defines()
    {
        var own = new MacroCallable("probe", [], [], [Reads("$total")],
            [Local("total", "5", ActionReturnType.Number)]);

        var problems = MacroCheck.Inspect(own.OwnSteps, own, [own]);

        Assert.DoesNotContain(problems,
            problem => problem.Code == MacroProblemCode.UndefinedVariable);
    }

    [Fact]
    public void A_new_declaration_needs_a_name_before_it_can_be_saved()
    {
        Ui.Run(() =>
        {
            var model = new LocalVariablesViewModel([], []);
            Assert.True(model.CanSave);

            model.NewCommand.Execute(null);

            Assert.Single(model.Rows);
            Assert.False(model.CanSave);
            Assert.False(string.IsNullOrEmpty(model.ValidationMessage));
        });
    }

    [Fact]
    public void A_name_a_step_writes_cannot_be_deleted_here()
    {
        Ui.Run(() =>
        {
            var written = new VariableChoice
            {
                Name = "total",
                Scope = VariableScope.Local,
                Origin = VariableOrigin.Local,
                Source = "设置变量",
            };

            var model = new LocalVariablesViewModel([], [written]);

            Assert.NotNull(model.Selected);
            Assert.False(model.Selected!.IsDeclared);
            Assert.False(model.RemoveCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Swapping_two_declared_names_is_applied_in_one_batch()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            editor.DeclaredLocals.Add(Local("a", "1", ActionReturnType.Number));
            editor.DeclaredLocals.Add(Local("b", "2", ActionReturnType.Number));
            editor.Steps.Add(Reads("$a + $b"));

            var first = LocalVariableDraft.Declared(editor.DeclaredLocals[0]);
            first.Name = "b";
            var second = LocalVariableDraft.Declared(editor.DeclaredLocals[1]);
            second.Name = "a";

            var problem = editor.ApplyLocalVariableEdits([first, second]);

            Assert.Empty(problem);
            Assert.Equal("b", editor.DeclaredLocals[0].Name);
            Assert.Equal("a", editor.DeclaredLocals[1].Name);
            Assert.Equal("$b + $a", editor.Steps[0].Text("text"));
        });
    }

    [Fact]
    public void A_declaration_the_manager_leaves_out_is_removed()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            editor.DeclaredLocals.Add(Local("kept", "1"));
            editor.DeclaredLocals.Add(Local("dropped", "2"));

            var kept = LocalVariableDraft.Declared(editor.DeclaredLocals[0]);
            var problem = editor.ApplyLocalVariableEdits([kept]);

            Assert.Empty(problem);
            Assert.Equal("kept", Assert.Single(editor.DeclaredLocals).Name);
        });
    }

    [Fact]
    public void The_window_offers_the_declaration_fields_only_for_a_declared_value()
    {
        Ui.Run(() =>
        {
            var written = new VariableChoice
            {
                Name = "total",
                Scope = VariableScope.Local,
                Type = "number",
                Origin = VariableOrigin.Local,
                Source = "设置变量",
            };
            var window = new LocalVariablesWindow(
                [Local("count", "1", ActionReturnType.Number)], [written], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (LocalVariablesViewModel)window.DataContext!;
            var initial = window.FindControl<TextBox>("InitialBox")!;

            Assert.True(model.Selected!.IsDeclared);
            Assert.True(initial.IsEffectivelyVisible);

            model.Selected = model.Shown.Single(row => row.Name == "total");
            Dispatcher.UIThread.RunJobs();

            Assert.False(initial.IsEffectivelyVisible);

            window.Close();
        });
    }

    [Fact]
    public void Saving_the_window_hands_the_rows_to_the_macro_and_closes()
    {
        IReadOnlyList<LocalVariableDraft>? applied = null;
        var open = true;
        Ui.Run(() =>
        {
            var window = new LocalVariablesWindow([], [], [], null,
                rows =>
                {
                    applied = rows;
                    return string.Empty;
                });
            window.Closed += (_, _) => open = false;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var model = (LocalVariablesViewModel)window.DataContext!;
            model.NewCommand.Execute(null);
            model.Selected!.Name = "count";
            model.Selected.Type = "number";
            model.Selected.Initial = "3";
            Dispatcher.UIThread.RunJobs();

            Assert.True(model.CanSave, model.ValidationMessage);
            model.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        });

        var row = Assert.Single(applied!);
        Assert.Equal("count", row.Name);
        Assert.Equal("3", row.Initial);
        Assert.False(open);
    }
}
