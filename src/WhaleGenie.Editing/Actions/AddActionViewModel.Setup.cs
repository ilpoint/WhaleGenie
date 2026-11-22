using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// What the form offers: the actions to pick from, the pages of the chosen one, and where assets go.
public partial class AddActionViewModel
{
    /// <summary>
    /// Re-reads the pages and the fields in the chosen language. The action itself is kept: it is
    /// the one the editor handed in, not something this form chose.
    /// </summary>
    public override void RefreshLanguage()
    {
        foreach (var page in Pages)
        {
            page.RefreshLanguage();
        }

        base.RefreshLanguage();

        foreach (var parameter in Parameters)
        {
            parameter.RefreshLanguage();
        }

        foreach (var row in Rows)
        {
            row.RefreshLanguage();
        }

        foreach (var row in AdvancedRows)
        {
            row.RefreshLanguage();
        }

        foreach (var result in ReturnsRows)
        {
            result.RefreshLanguage();
        }

        RefreshCallArgumentHints();

        GlobalVariableDraft?.RefreshLanguage();
        OnPropertyChanged(nameof(GlobalVariableProblem));

        MetaTimeout.RefreshLanguage();
        MetaRetryDelay.RefreshLanguage();
        MetaDelayBefore.RefreshLanguage();
        MetaDelayAfter.RefreshLanguage();
    }

    /// <summary>
    /// The actions the place a step is going accepts. A palette shortcut opens this dialog on the
    /// action it stands for, and only that action is written here, so this is the set those
    /// shortcuts are checked against rather than a list the user browses.
    /// </summary>
    public IReadOnlyList<ActionDefinition> AvailableActions
    {
        get => _availableActions;
        private set
        {
            _availableActions = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The steps of the macro being written, which is what a field naming a step picks from. The
    /// dialog is handed them because only the editor knows what is in the macro.
    /// </summary>
    public IReadOnlyList<ActionParameterOption> StepChoices
    {
        get => _steps;
        private set
        {
            _steps = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The name of the step this dialog was opened on. Editing a step must not rename it: the
    /// macro may already point at it — a condition watching whether it succeeded, a result
    /// variable carrying its name — so the dialog hands the same name back.
    /// </summary>
    private string _editingId = string.Empty;

    /// <summary>
    /// The names the step being edited already writes, which are its own results. They are in the
    /// macro's variable list like any other, and a result that is being edited would otherwise be
    /// told it is taken by itself.
    /// </summary>
    private readonly HashSet<string> _ownNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True while the step being written is not in the list yet. Its name is minted before the
    /// dialog opens so the fields that create variables can carry it, and it is only for such a
    /// step that those names follow the note: a variable a macro already uses keeps its name.
    /// </summary>
    private bool _isNewStep;

    /// <summary>The name of the action being built, over the fields it needs.</summary>
    public string SelectedActionName => SelectedDefinition?.LocalName
        ?? Strings.Get("Add.NoSelection");

    /// <summary>
    /// Where a picture taken from the screen is saved while this dialog is open, and where a
    /// relative picture value is looked up. The dialog that opened this one sets it, so a
    /// picture lands beside the macro package it belongs to.
    /// </summary>
    public string AssetFolder
    {
        get => _assetFolder;
        set
        {
            if (string.Equals(_assetFolder, value, StringComparison.Ordinal))
            {
                return;
            }

            _assetFolder = value;
            OnPropertyChanged();

            foreach (var parameter in Parameters)
            {
                parameter.AssetFolder = value;
            }
        }
    }
}
