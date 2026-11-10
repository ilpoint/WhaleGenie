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
    // Writing a step: the palette that adds one, the action form it opens, the check that keeps a
    // broken step from being saved, and the recorder panel beside the list.
    /// <summary>Brings up the window a step is written in, on the place the editor is now.</summary>
    private async void OnAddStepRequested()
    {
        var actions = _viewModel.AddableActions;
        if (actions.Count == 1)
        {
            await ShowActionEditor(null, actions, actions[0].Key, null);
            return;
        }

        RefreshPalette();
        ActionPalette.FocusSearch();
    }

    /// <summary>
    /// 双击动作卡：把这张动作交给和拖放同一个表单，保存后由编辑器放到当前落点（有选中就放在它
    /// 后面，空列表就是末尾）。拖放要真的按住拖过去，键盘与自动化都做不来，双击是它的等价物。
    /// </summary>
    private async void OnCardActivated(ActionPaletteCardViewModel card)
    {
        if (_actionEditorOpen || card.Kind != ActionPaletteCardKind.Action)
        {
            return;
        }

        var actions = _viewModel.AddableActions;
        if (!actions.Any(action => string.Equals(action.Key, card.Key, StringComparison.Ordinal)))
        {
            // 当前落点不收这种步骤，拖放也会被拒绝：不必开一个注定存不下的表单。
            return;
        }

        _actionEditorOpen = true;
        try
        {
            await ShowActionEditor(null, actions, card.Key, null);
        }
        finally
        {
            _actionEditorOpen = false;
        }
    }

    /// <summary>The same, preset on the "run another macro" step.</summary>
    private async void OnRunMacroRequested()
        => await ShowActionEditor(null, ActionCatalog.ForKeys(["control.runMacro"]), "control.runMacro", null);

    /// <summary>
    /// Whether any other macro of this project uses the shared browser. There is one current page
    /// for the program, so two macros that work in it take turns even when they never send input.
    /// </summary>
    private bool OtherMacroUsesBrowser() => OtherMacroReaches(MacroRunner.UsesBrowser);

    /// <summary>
    /// True when any macro other than the one being edited does the thing, either itself or through
    /// a macro it calls. Calls are followed with the names already being walked, so a circle of
    /// macros is answered rather than followed forever.
    /// </summary>
    private bool OtherMacroReaches(Func<string, bool> direct)
    {
        var macros = _session.Project.ToList();

        bool Walk(MacroStep step, HashSet<string> walking)
        {
            if (direct(step.Type))
            {
                return true;
            }

            foreach (var list in step.StepLists)
            {
                if (list.Steps.Any(child => Walk(child, walking)))
                {
                    return true;
                }
            }

            if (step.Type is not "control.runMacro")
            {
                return false;
            }

            var name = step.Text("macro").Trim();
            var called = macros.FirstOrDefault(macro =>
                string.Equals(macro.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (called is null || !walking.Add(called.Name.Trim()))
            {
                return false;
            }

            try
            {
                return called.Steps.Any(child => Walk(child, walking));
            }
            finally
            {
                walking.Remove(called.Name.Trim());
            }
        }

        return macros
            .Where(macro => !ReferenceEquals(macro, _session.Editing))
            .SelectMany(macro => macro.Steps)
            .Any(step => Walk(step, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Takes a macro lifted out of this one: it counts as part of the project from here on, so a
    /// later call can be pointed at it and the design-time check knows it is there.
    /// </summary>
    private void OnMacroLifted(MacroItem macro)
    {
        _session.Lifted.Add(macro);
        _session.Project.Add(macro);
        RefreshProjectCallables();
    }

    /// <summary>Writes one step in the single, draggable action window.</summary>
    private async Task<MacroStep?> ShowActionEditor(MacroStep? editing,
        IReadOnlyList<ActionDefinition> actions, string? presetKey, string? macroName)
    {
        var editingPage = _viewModel;

        // 窗口那几格现在可以"沿用上一次"：这里把本宏用过的窗口收成一张表交给表单，并告诉它落点
        // 之上有没有人指定过窗口——有的话，新写的这一步默认就跟着上面那个走，不必再绑一次。
        var windowRules = WindowRuleChoices.Of(_viewModel.Steps);
        var windowAbove = WindowRuleChoices
            .NearestAbove(_viewModel.Steps, _viewModel.SelectedSteps.LastOrDefault()?.Id);
        var follows = editing is null && windowAbove.Length > 0;

        var dialog = new ActionEditorWindow(editing, actions, _viewModel.CollectVariables(),
            MacroCalls(), presetKey, _session.AssetFolder, _viewModel.StepChoices(),
            editing?.Id ?? _viewModel.NewStepId(), windowRules, follows, windowAbove)
        {
            Devices = Layer,
            Macros = BuildLibrary(),
            PlaceStep = (existing, step) => existing is null
                ? _viewModel.PlacementRefusalForPending(step)
                : _viewModel.PlacementRefusalForReplacement(existing, step),
            NextStepId = taken => _viewModel.NewStepId(taken),
        };
        if (macroName is not null)
        {
            dialog.SetMacroCall(macroName);
        }

        // The modal loop is what keeps the editor behind the window out of reach; it is started and
        // let go, because its own answer is not the step. A picker hides this window while it is
        // out, and Avalonia answers a hidden modal window at once with nothing — so what is waited
        // on is the window itself, which says what it was closed with.
        _ = dialog.ShowDialogOver<MacroStep?>(this);
        var step = await dialog.Answer;

        _viewModel = editingPage;
        DataContext = editingPage;
        if (step is null)
        {
            _session.Pending = null;
            _viewModel.ClearPendingInsertion();
            return null;
        }

        if (editing is not null)
        {
            _viewModel.ReplaceStep(editing, step);
            return step;
        }

        var pending = _session.Pending;
        _session.Pending = null;
        _viewModel = editingPage;
        DataContext = editingPage;
        var refusal = _viewModel.AddStepHere(step);
        if (refusal is null
            && pending is not null
            && step.Type == "control.runMacro"
            && string.Equals(step.Text("macro").Trim(), pending.Editor.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            _session.Documents.Add(pending.Page);
            _documentPages.Add(pending.Page);
            SettleDocumentIds();
            _rootViewModel.IsDirty = true;
        }

        RefreshProjectCallables();
        RefreshDocuments();
        KeepRecovery();
        if (refusal is not null)
        {
            _viewModel.ReportPlacementRefusal(refusal);
        }

        return step;
    }

    /// <summary>Puts the form's capture buttons back to their resting labels.</summary>
    private void ResetKeyCaptureLabel()
    {
        if (_keyCaptureParameter is { } parameter)
        {
            parameter.IsCapturingKey = false;
            _keyCaptureParameter = null;
        }

        if (_keyRowCapture is { } row)
        {
            row.IsCapturing = false;
            _keyRowCapture = null;
        }

    }

    /// <summary>Builds the interface editor the first time it is asked for.</summary>
    private void EnsureInterface()
    {
        var model = new MacroInterfaceViewModel(_viewModel);
        _interfaceView = new MacroInterfaceView { DataContext = model };
        _settings.FindControl<ContentControl>("MacroInterfaceHost")!.Content = _interfaceView;
    }
    /// <summary>Shows the recording controls over the step list.</summary>
    private void OnShowRecording(object? sender, RoutedEventArgs e)
    {
        _recording.RecordingPanel.IsVisible = true;
    }

    internal void OnCloseRecording(object? sender, RoutedEventArgs e)
        => _recording.RecordingPanel.IsVisible = false;

    /// <summary>
    /// Puts the reason a save was refused in front of the person who asked for it. A close prompt
    /// can ask for a save from any page, so the page holding the name and declarations has to come
    /// back rather than leaving the attempt with no visible answer.
    /// </summary>
    private void OnSaveBlocked()
    {
        _settings.MacroSettingsDrawer.IsVisible = true;
        _recording.RecordingPanel.IsVisible = false;

        // The reason is written on one of the two pages, so the drawer opens the one that holds it:
        // a declaration problem is on the interface page, everything else on the first.
        _viewModel.ShowSettingsPage(_viewModel.PageThatBlocksSaving);

        // The save may have been asked for by the way out from the notification area. That way out
        // waits for this editor to close, so an editor that has to stay and explain itself must
        // cancel it.
        AppTray.Current?.CalledOff();

        if (!_viewModel.HasNameProblem)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var name = this.FindControl<TextBox>("NameInput");
            name?.Focus();
            name?.SelectAll();
        });
    }
}
