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

// The inspector beside the editor: the interface, record shapes, value tools and their order.
public partial class MacroEditorTests
{
    [Fact]
    public void The_action_editor_is_a_window_and_not_an_embedded_inspector()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            Assert.Null(window.FindControl<Border>("InspectorHost"));
            Assert.Null(window.FindControl<ContentControl>("InspectorContent"));
            Assert.NotNull(window.FindControl<Border>("MacroSettingsDrawer"));

            var action = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
            Assert.NotNull(action.View);
            action.Close();
        });
    }

    [Fact]
    public void The_inspector_can_edit_the_macro_interface()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();

            // 宏接口在「专属设置」页，所以先翻到那一页：内容一直在，但不在当前页时看不见。
            viewModel.ShowSettingsPage(MacroSettingsPage.Specific);
            Dispatcher.UIThread.RunJobs();

            var host = window.FindControl<ContentControl>("MacroInterfaceHost");
            Assert.NotNull(host);
            Assert.True(host!.IsEffectivelyVisible);

            var view = Assert.IsType<MacroInterfaceView>(host.Content);
            var model = (MacroInterfaceViewModel)view.DataContext!;
            model.AddParameterCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var row = Assert.Single(model.Parameters);
            row.Name = "数量";
            row.SelectedType = model.Types.First(type => type.Value == ValueKind.Number);
            Dispatcher.UIThread.RunJobs();

            var declared = Assert.Single(viewModel.DeclaredParameters);
            Assert.Equal("数量", declared.Name);
            Assert.Equal(ValueKind.Number, declared.Type);
            Assert.True(viewModel.IsDirty);
        });
    }

    [Fact]
    public void The_inspector_can_say_what_a_record_holds()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            var host = window.FindControl<ContentControl>("MacroInterfaceHost");
            Assert.NotNull(host);
            var view = Assert.IsType<MacroInterfaceView>(host!.Content);
            var model = (MacroInterfaceViewModel)view.DataContext!;

            model.AddParameterCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var row = Assert.Single(model.Parameters);
            row.Name = "点";

            // Fields only mean something for a record, so the rows wait for the kind to say so.
            Assert.False(row.IsRecord);
            row.SelectedType = model.Types.First(type => type.Value == ValueKind.Record);
            Assert.True(row.IsRecord);

            row.AddFieldCommand.Execute(null);
            var field = Assert.Single(row.Fields);
            field.Name = "x";
            field.SelectedType = model.Types.First(type => type.Value == ValueKind.Number);
            Dispatcher.UIThread.RunJobs();

            var declared = Assert.Single(viewModel.DeclaredParameters);
            var written = Assert.Single(declared.Fields);
            Assert.Equal("x", written.Name);
            Assert.Equal(ValueKind.Number, written.Type);

            // A field row is a declaration edit like any other: the variable wall and the check that
            // reads field(...) both answer differently once it is written down.
            Assert.True(viewModel.IsDirty);

            row.RemoveFieldCommand.Execute(field);
            Assert.Empty(declared.Fields);
        });
    }

    [Fact]
    public void The_macro_interface_puts_the_value_tool_inside_the_default_box()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();

            // 接口在「专属设置」页：页收起来时行容器不会被生成，所以要先把那一页翻出来。
            viewModel.ShowSettingsPage(MacroSettingsPage.Specific);
            Dispatcher.UIThread.RunJobs();

            var host = window.FindControl<ContentControl>("MacroInterfaceHost");
            Assert.NotNull(host);
            var view = Assert.IsType<MacroInterfaceView>(host!.Content);
            var model = (MacroInterfaceViewModel)view.DataContext!;

            model.AddParameterCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // One chip inside the box rather than two buttons beside it: a default is read as one
            // value, and the window that opens lists the variables on the same page as the functions.
            var chips = view.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("ValueChip"))
                .ToList();
            var chip = Assert.Single(chips);
            Assert.Equal(Strings.Get("Add.Value"), chip.Content?.ToString());
            Assert.NotNull(chip.FindAncestorOfType<TextBox>(true));
            Assert.Contains(model.ValueVariables, choice => choice.Name == "sys.date");
        });
    }

    [Fact]
    public void The_inspector_can_change_the_order_of_the_declarations()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            var host = window.FindControl<ContentControl>("MacroInterfaceHost");
            Assert.NotNull(host);
            var view = Assert.IsType<MacroInterfaceView>(host!.Content);
            var model = (MacroInterfaceViewModel)view.DataContext!;

            model.AddParameterCommand.Execute(null);
            model.AddParameterCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var first = model.Parameters[0];
            var second = model.Parameters[1];
            first.Name = "甲";
            second.Name = "乙";

            // The order is the order a call form asks for the values in, so moving one has to change
            // what is saved rather than only what is drawn.
            Assert.False(first.CanMoveUp);
            Assert.True(first.CanMoveDown);
            Assert.True(second.CanMoveUp);
            Assert.False(second.CanMoveDown);

            model.MoveParameterUpCommand.Execute(second);
            Assert.Equal(["乙", "甲"], model.Parameters.Select(row => row.Name));
            Assert.Equal(["乙", "甲"], viewModel.DeclaredParameters.Select(parameter => parameter.Name));

            // At the top there is nothing to swap with, so the button is off and the command is a
            // no-op rather than an error.
            model.MoveParameterUpCommand.Execute(model.Parameters[0]);
            Assert.Equal(["乙", "甲"], viewModel.DeclaredParameters.Select(parameter => parameter.Name));

            model.AddResultCommand.Execute(null);
            model.AddResultCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            model.Results[0].Name = "一";
            model.Results[1].Name = "二";

            model.MoveResultDownCommand.Execute(model.Results[0]);
            Assert.Equal(["二", "一"], viewModel.DeclaredResults.Select(result => result.Name));
        });
    }

    [Fact]
    public void An_invalid_declaration_keeps_the_editor_open()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "坏声明" };
            macro.Parameters.Add(new MacroParameter { Name = "同名" });
            macro.Parameters.Add(new MacroParameter { Name = "同名" });
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.False(viewModel.CanSave);
            Assert.True(viewModel.HasBlockingDeclarationProblems);

            var closed = false;
            viewModel.CloseRequested += _ => closed = true;
            viewModel.SaveCommand.Execute(null);

            Assert.False(closed);
        });
    }

    [Fact]
    public void A_second_declaration_edit_still_reaches_the_recovery_writer()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            var host = window.FindControl<ContentControl>("MacroInterfaceHost");
            Assert.NotNull(host);
            var model = (MacroInterfaceViewModel)Assert.IsType<MacroInterfaceView>(host!.Content).DataContext!;

            var changes = 0;
            viewModel.Changed += (_, _) => changes++;

            model.AddParameterCommand.Execute(null);
            var afterAdd = changes;
            var row = Assert.Single(model.Parameters);
            row.Name = "第一";
            row.Name = "第二";

            // MarkDirty raises Changed even while the macro is already unsaved; without that the
            // second edit would never be written to the recovery snapshot.
            Assert.True(changes >= afterAdd + 2,
                $"only {changes - afterAdd} change(s) followed the add");
        });
    }

    [Fact]
    public void Renaming_a_parameter_names_the_macro_that_would_break()
    {
        Ui.Run(() =>
        {
            var caller = new MacroItem { Name = "调用者" };
            caller.Steps.Add(new MacroStep
            {
                Type = "control.runMacro",
                Meta = StepMeta.New,
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "macro",
                        Kind = ActionParameterKind.Macro,
                        Written = Formula.FromLine("被调用"),
                    },
                    new StepParameter
                    {
                        Name = "arg.旧",
                        Kind = ActionParameterKind.Expression,
                        Written = Formula.FromLine("1"),
                    },
                ],
            });

            var edited = new MacroItem { Name = "被调用" };
            edited.Parameters.Add(new MacroParameter { Name = "新", Required = true });
            var window = new MacroEditorWindow(edited, [edited, caller]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (MacroEditorViewModel)window.DataContext!;
            Assert.True(viewModel.HasCallImpact);
            Assert.Contains("调用者", viewModel.CallImpactSummary, StringComparison.Ordinal);
        });
    }
}
