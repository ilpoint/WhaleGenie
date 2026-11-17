using System;
using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.ViewModels;

// The token the person picked: brackets, a list and a sign all apply to it.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>The walk to the picked token, or null when nothing is picked.</summary>
    public NodePath? SelectedPath { get; private set; }

    /// <summary>The picked token as it now stands in the band.</summary>
    public SlotNode? SelectedNode => SelectedPath is { } path
        ? Recipe?.Walk().FirstOrDefault(piece => piece.Path?.Equals(path) == true)
        : null;

    public bool HasSelection => SelectedPath is not null;

    /// <summary>Picks one token; null clears the pick. A press on blank band space clears it.</summary>
    public void Select(SlotNode? piece)
    {
        var path = piece?.Path;
        if (Equals(SelectedPath, path))
        {
            return;
        }

        if (SelectedNode is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedPath = path;
        if (SelectedNode is { } picked)
        {
            picked.IsSelected = true;
        }

        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(HasSelection));
        RefreshOperatorState();
    }

    /// <summary>
    /// Re-marks the picked token after the band was rebuilt. A path that no longer exists — the
    /// token was deleted or undone away — clears the pick instead of pointing at nothing.
    /// </summary>
    private void ApplySelection()
    {
        if (Recipe is null)
        {
            SelectedPath = null;
            return;
        }

        var found = false;
        foreach (var piece in Recipe.Walk())
        {
            var selected = SelectedPath is { } path && piece.Path?.Equals(path) == true;
            piece.IsSelected = selected;
            found |= selected;
        }

        if (!found)
        {
            SelectedPath = null;
        }

        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(HasSelection));
    }
}
