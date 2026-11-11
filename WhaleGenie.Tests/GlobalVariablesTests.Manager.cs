using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
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
            using var persistence = PersistentVariables.UsePathForTests(
                Path.Combine(folder, "persistent-variables.json"));
            var previousGlobals = VariableCatalog.Globals.ToList();
            var previousValues = SharedVariables.Current.Values;
            VariableCatalog.Globals.Clear();
            SharedVariables.Current.Clear();
            VariableCatalog.Globals.Add(new VariableDefinition
            {
                Name = "editedByManager",
                Scope = VariableScope.Global,
                Type = "number",
                DefaultValue = "1",
            });
            try
            {
                var macro = new MacroItem { Name = "managerSave" };
                var window = new MacroEditorWindow(macro, [macro]);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.FindControl<Button>("ManageGlobalsButton")!
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                var dialog = Assert.Single(window.OwnedWindows.OfType<GlobalVariablesWindow>());
                var manager = (GlobalVariablesViewModel)dialog.DataContext!;
                manager.Selected!.DefaultValue = "9";
                manager.Selected.PersistValue = true;
                Assert.True(manager.CanSave, manager.ValidationMessage);
                manager.SaveCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(50);
                Dispatcher.UIThread.RunJobs();

                // Nothing types the current value any more: turning persistence on seeds the shared
                // table from the default, which is the value a run would start it at.
                Assert.Equal(9, SharedVariables.Current.Values["editedByManager"].AsNumber());
                Assert.True(VariableCatalog.FindGlobal("editedByManager")!.PersistValue);
                PersistentVariables.Flush();
                Assert.True(PersistentVariables.TryGet("editedByManager", out var saved));
                Assert.Equal("9", saved);

                // 关窗时问的“要不要保存”必须在这里答完：编辑器把问题排进队列，走了不管就会在
                // 下一个检查里冒出来，把那个检查的调度循环留在运行中。
                Ui.CloseAndDiscard(window);
                return true;
            }
            finally
            {
                VariableCatalog.Globals.Clear();
                foreach (var global in previousGlobals)
                {
                    VariableCatalog.Globals.Add(global);
                }

                SharedVariables.Current.Clear();
                foreach (var (name, value) in previousValues)
                {
                    SharedVariables.Current.Set(name, value);
                }
            }
        });
    }

    [Fact]
    public void Renaming_a_global_updates_the_open_macro_and_keeps_its_value()
    {
        Ui.RunAsync(async () =>
        {
            var previousGlobals = VariableCatalog.Globals.ToList();
            var previousValues = SharedVariables.Current.Values;
            VariableCatalog.Globals.Clear();
            SharedVariables.Current.Clear();
            VariableCatalog.Globals.Add(new VariableDefinition
            {
                Name = "oldGlobal",
                Scope = VariableScope.Global,
                Type = "number",
                DefaultValue = "4",
            });
            SharedVariables.Current.SetNumber("oldGlobal", 6);
            try
            {
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
                            Written = Formula.FromLine("$oldGlobal + 1"),
                        },
                    ],
                });
                var window = new MacroEditorWindow(macro, [macro]);
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

                Assert.NotNull(VariableCatalog.FindGlobal("newGlobal"));
                Assert.Null(VariableCatalog.FindGlobal("oldGlobal"));
                Assert.Equal(6, SharedVariables.Current.Values["newGlobal"].AsNumber());
                var root = (MacroEditorViewModel)window.DataContext!;
                Assert.Equal("$newGlobal + 1", root.Steps[0].Text("message"));

                // 改名会让这一步的文本变脏，关窗时会问要不要保存。这里问的是一件事：改名有没有
                // 落到步骤上。问句必须在这里答完，否则它会在下一个检查里冒出来，把那个检查的
                // 调度循环留在运行中。
                Ui.CloseAndDiscard(window);
                return true;
            }
            finally
            {
                VariableCatalog.Globals.Clear();
                foreach (var global in previousGlobals)
                {
                    VariableCatalog.Globals.Add(global);
                }

                SharedVariables.Current.Clear();
                foreach (var (name, value) in previousValues)
                {
                    SharedVariables.Current.Set(name, value);
                }
            }
        });
    }
}
