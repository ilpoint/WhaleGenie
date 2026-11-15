using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The window shell, saving a dirty value, and renaming a global.
public partial class GlobalVariablesTests
{
    [Fact]
    public void The_manager_saves_a_dirty_current_value_and_its_persistence_flag()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
                Guid.NewGuid().ToString("N"));
            using var persistence = new PersistedVariables(
                Path.Combine(folder, "persistent-variables.json"));
            var session = new VariableSession();
            persistence.Initialize(session);
            session.Adopt(new VariableDefinition
            {
                Name = "editedByManager",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
                Default = Formula.Text("1"),
            });

            var macro = new MacroItem { Name = "managerSave" };
            var window = new MacroEditorWindow(macro, [macro], persisted: persistence,
                variables: session);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.FindControl<Button>("ManageGlobalsButton")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var dialog = Assert.Single(window.OwnedWindows.OfType<GlobalVariablesWindow>());
            var manager = (GlobalVariablesViewModel)dialog.DataContext!;
            manager.Selected!.Default = Formula.Text("9");
            manager.Selected.PersistValue = true;
            Assert.True(manager.CanSave, manager.ValidationMessage);
            manager.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();

            // Nothing types the current value any more: turning persistence on seeds the shared
            // table from the default, which is the value a run would start it at.
            Assert.Equal(9, session.Values.Values["editedByManager"].AsNumber());
            Assert.True(session.FindGlobal("editedByManager")!.PersistValue);
            persistence.Flush();
            Assert.True(persistence.TryGet("editedByManager", out var saved));
            Assert.Equal("9", saved);

            // 关窗时问的“要不要保存”必须在这里答完：编辑器把问题排进队列，走了不管就会在
            // 下一个检查里冒出来，把那个检查的调度循环留在运行中。
            Ui.CloseAndDiscard(window);
            return true;
        });
    }

    [Fact]
    public void Renaming_a_global_updates_the_open_macro_and_keeps_its_value()
    {
        Ui.RunAsync(async () =>
        {
            var session = new VariableSession();
            session.Adopt(new VariableDefinition
            {
                Name = "oldGlobal",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
                Default = Formula.Text("4"),
            });
            session.Values.SetNumber("oldGlobal", 6);

            var macro = new MacroItem { Name = "renameGlobal" };
            macro.Steps.Add(new MacroStep
            {
                Type = "control.log",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "message",
                        Kind = ActionParameterKind.Expression,
                        Written = Formula.Binary("+", Formula.Name("oldGlobal"), Formula.Number("1", 1d)),
                    },
                ],
            });
            var window = new MacroEditorWindow(macro, [macro], variables: session);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.FindControl<Button>("ManageGlobalsButton")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var dialog = Assert.Single(window.OwnedWindows.OfType<GlobalVariablesWindow>());
            var manager = (GlobalVariablesViewModel)dialog.DataContext!;
            manager.Selected!.Name = "newGlobal";
            Assert.True(manager.CanSave, manager.ValidationMessage);
            manager.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(session.FindGlobal("newGlobal"));
            Assert.Null(session.FindGlobal("oldGlobal"));
            Assert.Equal(6, session.Values.Values["newGlobal"].AsNumber());
            var root = (MacroEditorViewModel)window.DataContext!;
            Assert.Equal("newGlobal + 1", root.Steps[0].Text("message"));

            // 改名会让这一步的文本变脏，关窗时会问要不要保存。这里问的是一件事：改名有没有
            // 落到步骤上。问句必须在这里答完，否则它会在下一个检查里冒出来，把那个检查的
            // 调度循环留在运行中。
            Ui.CloseAndDiscard(window);
            return true;
        });
    }
}
