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

// Undo and redo: what a change remembers and how it is put back.
public partial class MacroEditorViewModel
{
    /// <summary>True while an undone change can be put back.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Remembers the list as it is now, so the next change can be rolled back.</summary>
    private void PushUndo()
    {
        _undo.Add(Capture());
        if (_undo.Count > HistoryLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        RefreshPaletteState();
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(Capture());
        Restore(entry);
        RefreshPaletteState();
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(Capture());
        Restore(entry);
        RefreshPaletteState();
    }

    private HistoryEntry Capture() => new(
        [.. Steps.Select(Clone)],
        [.. _selection.Select(step => DocumentOrder().IndexOf(step)).Where(index => index >= 0)],
        DelayScale);

    private void Restore(HistoryEntry entry)
    {
        DelayScale = entry.DelayScale;
        Steps.Clear();
        foreach (var step in entry.Steps)
        {
            Steps.Add(Clone(step));
        }

        var written = DocumentOrder();
        var restored = entry.Selection
            .Where(index => index < written.Count)
            .Select(index => written[index])
            .ToList();

        RestoreSelection(restored);
        NotifyStepsChanged();
    }

    /// <summary>Greys out palette entries that have nothing to act on.</summary>
    private void RefreshPaletteState()
    {
        foreach (var entry in ActionPalette)
        {
            var enabled = entry.IsAvailable;

            if (enabled && entry.NeedsSelection)
            {
                enabled = _selection.Count > 0;
            }

            if (enabled && entry.NeedsSingleSelection)
            {
                enabled = _selection.Count == 1;
            }

            if (enabled && entry.NeedsSteps)
            {
                enabled = Steps.Count > 0;
            }

            if (enabled && entry.NeedsUndo)
            {
                enabled = CanUndo;
            }

            if (enabled && entry.NeedsRedo)
            {
                enabled = CanRedo;
            }

            entry.IsEnabled = enabled;
        }
    }
}
