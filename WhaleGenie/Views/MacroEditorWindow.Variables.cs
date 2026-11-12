using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MacroEditorWindow : AppWindow
{
    // The variable managers opened from the editor and the drafts they hand back. Keeping them here
    // means a change to how variables are edited does not read the document tabs or the step tree.
    private async void OnManageGlobals(object? sender, RoutedEventArgs e)
        => await ManageGlobalsAsync();

    private async void OnManageLocals(object? sender, RoutedEventArgs e)
        => await ManageLocalsAsync();

    /// <summary>
    /// 管理这个宏自己的变量：声明在窗口里新建、编辑和删除，步骤写出来的名字只能在窗口里改名。
    /// </summary>
    private async Task ManageLocalsAsync(string? initialName = null)
    {
        var all = _viewModel.CollectVariables();
        await LocalVariablesWindow.ShowAsync(this,
            _viewModel.DeclaredLocals,
            all,
            // An initial value is read before the first step, so it can only name what a run
            // already has: the built-ins, the shared values and what the caller fills in.
            [.. all.Where(choice => choice.Group is VariableGroup.System or VariableGroup.Global
                || choice.Origin is VariableOrigin.Parameter)],
            _viewModel.LocalNameConflict,
            _viewModel.ApplyLocalVariableEdits,
            initialName);
    }

    private async Task ManageGlobalsAsync(string? initialName = null)
    {
        var allMacros = ProjectMacrosForVariableEdits();
        var expressionVariables = _viewModel.CollectVariables()
            .Where(choice => choice.Group is VariableGroup.Global or VariableGroup.System)
            .ToList();
        var choices = _viewModel.CollectVariables();
        var edited = await GlobalVariablesWindow.EditAsync(this, expressionVariables,
            name => choices.FirstOrDefault(choice =>
                string.Equals(choice.Name, name, StringComparison.OrdinalIgnoreCase))?.Uses ?? [],
            name => VariableRename.GlobalConflict(allMacros, name),
            name => VariableRename.ScriptUsers(allMacros, name),
            initialName);
        if (edited is not null)
        {
            ApplyGlobalVariableDrafts(edited, allMacros);
        }
    }

    private async void OnEditVariableRequested(VariableChoice choice)
    {
        switch (choice.Origin)
        {
            case VariableOrigin.Global:
                await ManageGlobalsAsync(choice.Name);
                break;
            case VariableOrigin.DeclaredLocal:
            case VariableOrigin.Local:
                await ManageLocalsAsync(choice.Name);
                break;
            case VariableOrigin.Parameter:
            case VariableOrigin.Result:
                // The interface lives on the second page now, so a click on one of its names has to
                // bring that page up before there is anything to scroll to.
                _viewModel.ShowSettingsPage(MacroSettingsPage.Specific);
                EnsureInterface();
                this.FindControl<ContentControl>("MacroInterfaceHost")?.BringIntoView();
                break;
        }
    }

    private IReadOnlyList<MacroItem> ProjectMacrosForVariableEdits()
        => [.. _session.Project,
            .. _documentPages.Select(page => page.Editor.BuildMacro())];

    private void ApplyGlobalVariableDrafts(IReadOnlyList<VariableDefinitionDraft> drafts,
        IReadOnlyList<MacroItem> macros)
    {
        var renamed = drafts.Where(draft => !draft.IsNew
                && !string.Equals(draft.OriginalName, draft.Name.Trim(),
                    StringComparison.Ordinal))
            .ToList();
        foreach (var draft in renamed)
        {
            var next = draft.Name.Trim();
            VariableRename.RenameGlobal(macros, draft.OriginalName, next);
            foreach (var page in _documentPages.Select(page => page.Editor).Append(_rootViewModel)
                         .Distinct())
            {
                page.RenameGlobalReferences(draft.OriginalName, next);
            }

            _rootViewModel.Persisted.Rename(draft.OriginalName, next,
                VariableCatalog.FindGlobal(draft.OriginalName)?.Id ?? string.Empty);
            SharedVariables.Current.Rename(draft.OriginalName, next);
        }

        var kept = drafts.Select(draft => draft.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in VariableCatalog.Globals
                     .Where(variable => !kept.Contains(variable.Name))
                     .Select(variable => variable.Name)
                     .ToList())
        {
            _rootViewModel.Persisted.Forget(removed);
            SharedVariables.Current.Remove(removed);
        }

        VariableCatalog.Globals.Clear();
        foreach (var draft in drafts)
        {
            // The draft carries the identity the declaration already had, so saving an edited
            // variable does not make every reference to it stale.
            var definition = draft.ToDefinition();
            VariableCatalog.Globals.Add(definition);
            _rootViewModel.Persisted.PersistChanged(definition, definition.PersistValue);
        }

        VariableCatalog.NotifyGlobalsChanged();
        RefreshSharedVariables();
    }

    private void OnGlobalsChanged() =>
        Dispatcher.UIThread.Post(RefreshSharedVariables);

    private void OnSharedVariableChanged(string _) =>
        Dispatcher.UIThread.Post(RefreshSharedVariables);

    private void RefreshSharedVariables()
    {
        var editors = _documentPages.Select(page => page.Editor)
            .Append(_rootViewModel)
            .Distinct()
            .ToList();
        foreach (var editor in editors)
        {
            editor.RefreshSharedVariables();
        }
    }
}
