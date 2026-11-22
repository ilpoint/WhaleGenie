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
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// Keeping the fields in step with each other as a value or a setting changes.
public partial class AddActionViewModel
{
    /// <summary>
    /// Lets a parameter react to the sibling list it waits on, such as the logic of a
    /// condition group, which only applies once there are two or more conditions.
    /// </summary>
    private void RefreshGates()
    {
        var rows = 0;
        foreach (var editor in Parameters)
        {
            if (editor.Definition.EnabledBySibling is { Length: > 0 } siblingName)
            {
                var sibling = Parameters.FirstOrDefault(parameter =>
                    parameter.Definition.Name == siblingName);
                editor.UpdateGate(sibling?.List?.Steps.Count ?? 0);
            }

            // What a field follows is a value rather than a count: the ways of recognising a
            // picture ask different questions, and a question nothing reads is not drawn.
            if (editor.Definition.AppliesWhen.Length > 0
                || editor.Definition.AppliesWhenAlso.Length > 0)
            {
                var followed = Parameters.FirstOrDefault(parameter =>
                    parameter.Definition.Name == editor.Definition.AppliesWhen);

                var alsoBy = editor.Definition.AppliesWhenAlso;
                var also = alsoBy.Length == 0
                    ? null
                    : Parameters.FirstOrDefault(parameter => parameter.Definition.Name == alsoBy)
                        ?.CurrentText;

                if (editor.UpdateApplicability(followed?.CurrentText, also))
                {
                    rows++;
                }
            }
        }

        // Which windows the step recognises is read off all three of those fields, so it is asked
        // for once they all say what they say. A window named to count numbers from, or to post
        // input at, is read only while that is what the step does, so the list goes up and comes
        // down with that answer rather than standing there over a rule nothing reads.
        if (WindowPreview is { } preview && _previewField is { } text)
        {
            var asking = _previewGate switch
            {
                PreviewGate.AnchorOrigin => AnchorNamesWindow(),
                PreviewGate.BackgroundPost => InputPostsToWindow(),
                _ => true,
            };

            text.WindowPreview = asking ? preview : null;
            preview.Show(asking);
            if (asking)
            {
                preview.Follow(RuleOf(text),
                    SelectedDefinition?.Key == ListWindowsKey, text.HasToBeAnswered,
                    holdsVariable: text.Written is VariableNode);
            }
        }

        // Which page holds what can come and go with what a field says, so the pages are read
        // again rather than left as they were when the action was chosen.
        if (rows > 0)
        {
            RefreshPages();
        }
    }

    /// <summary>
    /// The ways of answering "where" or "which rectangle": the record in the first field, or the
    /// numbers in the rest.
    /// </summary>
    private static readonly (string Place, string[] Numbers)[] PlaceGroups =
    [
        ("place", ["x", "y"]),
        ("rect", ["x", "y", FieldNames.Width, FieldNames.Height]),
        ("startPlace", ["startX", "startY"]),
        ("endPlace", ["endX", "endY"]),
    ];

    /// <summary>True while this dialog is emptying one way of answering because the other was used.</summary>
    private bool _answeringPlace;

    /// <summary>
    /// A place or a rectangle is answered one way: by a record, or by the numbers beside it. What
    /// is typed into one clears the other, so the form never holds two answers at once and nobody
    /// has to know which one the engine would have read.
    /// </summary>
    private void KeepPlaceAndNumbersApart(object? sender, PropertyChangedEventArgs e)
    {
        if (_answeringPlace
            || sender is not StepParameterViewModel changed
            || e.PropertyName is not (nameof(StepParameterViewModel.Text)
                or nameof(StepParameterViewModel.NumberValue)))
        {
            return;
        }

        var group = PlaceGroups.FirstOrDefault(candidate =>
            candidate.Place == changed.Definition.Name
            || candidate.Numbers.Contains(changed.Definition.Name, StringComparer.Ordinal));
        if (group.Place is null || changed.CurrentText.Trim().Length == 0)
        {
            return;
        }

        _answeringPlace = true;
        try
        {
            if (changed.Definition.Name == group.Place)
            {
                foreach (var name in group.Numbers)
                {
                    Parameter(name)?.Clear();
                }
            }
            else
            {
                Parameter(group.Place)?.Clear();
            }
        }
        finally
        {
            _answeringPlace = false;
        }
    }

    private void OnParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        KeepPlaceAndNumbersApart(sender, e);

        if (sender is StepParameterViewModel changed
            && changed.Definition.Name is "name" or "scope")
        {
            SyncGlobalVariable();
        }

        // A call's fields are the called macro's declarations, so the form is rebuilt the moment
        // the macro it names changes. The rebuild says everything a change says and the fields this
        // event came from are gone with it, so nothing else is done here.
        if (!_buildingCallFields
            && CallsMacro(SelectedDefinition)
            && !string.Equals(Parameter("macro")?.CurrentText.Trim() ?? string.Empty, _callMacro,
                StringComparison.Ordinal))
        {
            RefreshCallFields();
            return;
        }

        RefreshGates();
        RefreshPages();
        // A result ticked a moment ago has no name yet, and a name typed a moment ago may now be
        // one somebody else is using, so the names are read again from what the dialog knows.
        SuggestReturnNames();
        OnPropertyChanged(nameof(HasParameters));
        OnPropertyChanged(nameof(JsonPreview));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(ValidationIsProblem));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanRun));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Fills in the name of every result this step hands over, and keeps doing it while the note is
    /// being written. A name of its own means two steps of the same action do not quietly write into
    /// the same variable — the one mistake in this dialog that shows up much later and somewhere
    /// else. A name the user (or the file) already chose is left as it is.
    /// </summary>
    private void SuggestReturnNames()
    {
        if (SelectedDefinition is not { } definition || ReturnsField is not { } returns)
        {
            return;
        }

        // The step's own results are left out: a name this dialog is about to write is not one
        // something else is using.
        returns.SettleReturns(MetaComment, _editingId,
            [.. CollectVariables()
                .Select(choice => choice.Name)
                .Where(name => !_ownNames.Contains(name.Trim()))]);
    }

    /// <summary>The note is part of the name, so the name has to keep up with it.</summary>
    partial void OnMetaCommentChanged(string value) => SuggestReturnNames();

    /// <summary>Refreshes what a change to a step-settings length of time affects.</summary>
    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(JsonPreview));
        OnPropertyChanged(nameof(RetryPlan));
        OnPropertyChanged(nameof(TunedStep));
    }

    /// <summary>Whether one of the failure rules is the one in force.</summary>
    private static bool Picked(ActionParameterOption? choice, string value)
        => string.Equals(choice?.Value, value, StringComparison.Ordinal);

    /// <summary>
    /// Takes a card's tick as the rule. A card that was unticked because another was picked says
    /// nothing: only the one that became true changes the rule.
    /// </summary>
    private void PickError(bool ticked, string value)
    {
        if (!ticked)
        {
            return;
        }

        MetaOnError = ErrorChoices.FirstOrDefault(choice => Picked(choice, value)) ?? MetaOnError;
    }
}
