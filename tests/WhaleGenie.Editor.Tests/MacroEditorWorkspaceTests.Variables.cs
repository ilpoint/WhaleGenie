using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// A set-variable step written into a project, and the global it may create.
public partial class MacroEditorWorkspaceTests
{
    [Fact]
    public void Setting_a_variable_uses_an_existing_global_without_creating_one()
    {
        Ui.Run(() =>
        {
            var name = "existingGlobal";
            var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
                Guid.NewGuid().ToString("N"));
            using var persistence = new PersistedVariables(
                Path.Combine(folder, "persistent-variables.json"));
            var session = new VariableSession();
            persistence.Initialize(session);
            var existing = new VariableDefinition
            {
                Name = name,
                Scope = VariableScope.Global,
                Type = ValueKind.Text,
            };
            session.Adopt(existing);
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro], persisted: persistence,
                variables: session);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var root = (MacroEditorViewModel)window.DataContext!;
            var list = window.FindControl<TreeView>("StepList")!;
            Drag(list, DragDrop.DropEvent,
                new ActionDragPayload(ActionPaletteCardKind.Action,
                    "control.setVariable").Transfer());
            Dispatcher.UIThread.RunJobs();

            var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
            var form = (AddActionViewModel)action.DataContext!;
            form.Parameters.Single(parameter => parameter.Definition.Name == "name").WriteValue(Formula.Name(name));
            var scope = form.Parameters.Single(parameter => parameter.Definition.Name == "scope");
            scope.Option = scope.Choices.Single(option => option.Value == "global");
            form.Parameters.Single(parameter => parameter.Definition.Name == "value").WriteValue(Fields.Shape("one"));
            Assert.True(form.CanSave, form.ValidationMessage);
            form.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            var savedStep = Assert.Single(root.Steps);
            Assert.Equal(name, savedStep.Text("name"));
            Assert.Equal("global", savedStep.Text("scope"));
            Assert.Single(session.Globals, variable => variable.Name == name);
            Assert.Contains(root.VariableCenter.Wall.Shown,
                choice => choice.Name == name);

            persistence.Flush();
            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void Setting_a_variable_can_create_a_persistent_global_in_the_same_save()
    {
        Ui.Run(() =>
        {
            var name = "2createdInsideAction";
            var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
                Guid.NewGuid().ToString("N"));
            using var persistence = new PersistedVariables(
                Path.Combine(folder, "persistent-variables.json"));
            var session = new VariableSession();
            persistence.Initialize(session);
            {
                var macro = new MacroItem { Name = "probe" };
                var window = new MacroEditorWindow(macro, [macro], persisted: persistence,
                    variables: session);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var root = (MacroEditorViewModel)window.DataContext!;
                var list = window.FindControl<TreeView>("StepList")!;
                Drag(list, DragDrop.DropEvent,
                    new ActionDragPayload(ActionPaletteCardKind.Action,
                        "control.setVariable").Transfer());
                Dispatcher.UIThread.RunJobs();

                var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
                var form = (AddActionViewModel)action.DataContext!;
                form.Parameters.Single(parameter => parameter.Definition.Name == "name").WriteValue(Formula.Name(name));
                var scope = form.Parameters.Single(parameter => parameter.Definition.Name == "scope");
                scope.Option = scope.Choices.Single(option => option.Value == "global");
                form.Parameters.Single(parameter => parameter.Definition.Name == "value").Text =
                    "「count」 + 1";
                Dispatcher.UIThread.RunJobs();

                Assert.True(form.ShowsGlobalPersistence);
                Assert.NotNull(form.GlobalVariableDraft);
                form.GlobalVariableDraft.Type = ValueKind.Number;
                form.GlobalVariableDraft.Default = Formula.Text("2");
                form.GlobalVariableDraft.PersistValue = true;
                Assert.True(form.Parameters
                    .Single(parameter => parameter.Definition.Name == "value").IsExpression);
                Assert.True(form.CanSave, form.ValidationMessage);
                form.SaveCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                var saved = Assert.Single(session.Globals,
                    variable => variable.Name == name);
                Assert.Equal(ValueKind.Number, saved.Type);
                Assert.Equal("2", saved.DefaultValue);
                Assert.True(saved.PersistValue);
                Assert.True(session.Values.TryGet(name, out var current));
                Assert.Equal(2, current.AsNumber());

                persistence.Flush();
                Assert.True(persistence.TryGet(name, out _));
                Ui.CloseAndAnswer(window);
            }
        });
    }

    [Fact]
    public void Cancelling_a_set_variable_step_does_not_create_its_global()
    {
        Ui.Run(() =>
        {
            var name = "cancelledGlobal";
            var session = new VariableSession();
            {
                var macro = new MacroItem { Name = "probe" };
                var window = new MacroEditorWindow(macro, [macro], variables: session);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var list = window.FindControl<TreeView>("StepList")!;
                Drag(list, DragDrop.DropEvent,
                    new ActionDragPayload(ActionPaletteCardKind.Action,
                        "control.setVariable").Transfer());
                Dispatcher.UIThread.RunJobs();

                var action = Assert.Single(window.OwnedWindows.OfType<ActionEditorWindow>());
                var form = (AddActionViewModel)action.DataContext!;
                form.Parameters.Single(parameter => parameter.Definition.Name == "name").WriteValue(Formula.Name(name));
                var scope = form.Parameters.Single(parameter => parameter.Definition.Name == "scope");
                scope.Option = scope.Choices.Single(option => option.Value == "global");
                form.GlobalVariableDraft!.PersistValue = true;
                form.CancelCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                Assert.DoesNotContain(session.Globals,
                    variable => variable.Name == name);
                window.Close(null);
            }
        });
    }
}
