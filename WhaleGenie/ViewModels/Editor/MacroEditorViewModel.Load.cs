using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WindowRuleModel = WhaleGenie.Core.Devices.WindowRule;

namespace WhaleGenie.ViewModels;

// Loading a saved macro into the editor and the names it starts with.
public partial class MacroEditorViewModel
{
    /// <summary>
    /// 点中哪一步，哪一步就在原处展开，把一行装不下的说明、备注与执行节奏露出来。只展开选中
    /// 的那一步：选了好几行时一个都不展开，那会让人以为只选中了一条。展开在行里而不是列表
    /// 下面另起一条，是为了点一下看细节时视线不离开那一行，列表也不跳。
    /// </summary>
    private void RefreshRowDetails()
    {
        var only = _selection.Count == 1 ? _selection[0] : null;
        foreach (var row in Rows)
        {
            row.ShowsDetail = row.IsStep && ReferenceEquals(row.Step, only);

            // A branch row stands for its branch, so picking one lights it up the same way a step
            // lights up its own row.
            row.IsPicked = (row.IsStep || row.IsCase) && row.Step is { } step && _selection.Contains(step);
        }
    }

    /// <summary>
    /// Asks the view to put the highlight back on the rows the selection sits on. A block's title
    /// and its closing line stand for the block itself, so picking one of those moves the
    /// highlight onto the block's own row rather than leaving it on the line that was clicked.
    /// </summary>
    public void RefreshSelection() => SelectionRefreshRequested?.Invoke();

    /// <summary>Fills the editor from an existing macro so it can be edited.</summary>
    public void LoadFrom(MacroItem macro, IReadOnlyList<TriggerEntry> entries)
    {
        _loading = true;
        try
        {
            Name = macro.Name;
            Description = macro.Description;
            Group = macro.Group;
            Origin = macro.Origin;
            TriggerEntries.Clear();
            foreach (var entry in entries)
            {
                var copy = entry.Copy();
                WatchTriggerEntry(copy);
                TriggerEntries.Add(copy);
            }

            _selectedTrigger = TriggerEntries.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedTrigger));
            OnPropertyChanged(nameof(HasSelectedTrigger));
            OnPropertyChanged(nameof(ShowsTriggerSettings));
            OnPropertyChanged(nameof(ShowsLoopMode));
            OnPropertyChanged(nameof(ShowsTriggerOnce));
            if (_selectedTrigger is { } first)
            {
                LoadTriggerFields(first);
            }
            PositionCapture = macro.PositionCapture;
            RecordMouseMovement = macro.RecordMouseMovement;
            RecordMouseButtons = macro.RecordMouseButtons;
            RecordKeyboardKeys = macro.RecordKeyboardKeys;
            RecordGamepadButtons = macro.RecordGamepadButtons;
            RecordDelays = macro.RecordDelays;
            IgnoreDelaysBelowMs = macro.IgnoreDelaysBelowMs;
            RecordIgnoreInjected = WhaleGenieInputGate.IgnoreOwnInput;
            DelayScale = macro.DelayScale;

            // The box shows the program's value while this macro is following it, so turning the
            // follow switch off does not start from a number nobody picked.
            FollowProgramInputRate = macro.InputRate is null;
            InputLimitValue = macro.InputRate ?? LocalSettings.LoadInputRate();
            ToggleKey = macro.ToggleKey;
            ToggleKeyProblem = string.Empty;

            // The page is a view of this window, not part of the macro: opening one always starts
            // where the name and triggers are.
            SettingsPage = MacroSettingsPage.General;

            DeclaredParameters.Clear();
            DeclaredParameters.AddRange(macro.Parameters);
            DeclaredResults.Clear();
            DeclaredResults.AddRange(macro.Results);
            DeclaredLocals.Clear();
            DeclaredLocals.AddRange(macro.Locals);

            Steps.Clear();
            foreach (var step in macro.Steps)
            {
                Steps.Add(step);
            }

            SubMacros.Clear();
            SubMacros.AddRange(macro.SubMacros);

            // A macro written before steps had names, or one edited by hand outside the program,
            // arrives with none or with the same one twice; both are settled here, once, rather
            // than defended against in every place that reads a step's name.
            SettleStepIds();
        }
        finally
        {
            _loading = false;
        }

        NotifyStepsChanged();
        _undo.Clear();
        _redo.Clear();
        IsDirty = false;
    }

    /// <summary>
    /// Gives a new macro its first name without counting that name as an edit. The default is
    /// already usable, so closing a new editor without touching anything still means "nothing to
    /// save"; once a step is added, the default travels with it.
    /// </summary>
    internal void UseDefaultName(IEnumerable<string> existingNames)
    {
        _loading = true;
        try
        {
            Name = MacroNames.NextDefault(existingNames);
        }
        finally
        {
            _loading = false;
        }
    }
}
