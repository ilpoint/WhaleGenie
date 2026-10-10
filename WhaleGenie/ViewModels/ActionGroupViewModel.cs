using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One heading of the action picker and the actions under it. The catalogue is well over a hundred
/// actions, which is too long to read as one list, so it is shown as a dozen short ones that can
/// each be folded away.
/// </summary>
public partial class ActionGroupViewModel : ViewModelBase
{
    public ActionGroupViewModel(string key, string title, Geometry? icon,
        IReadOnlyList<ActionDefinition> actions, bool open, string note = "", bool section = false)
    {
        Key = key;
        Title = title;
        Icon = icon;
        Actions = actions;
        IsOpen = open;
        Note = note;
        IsSection = section;
    }

    /// <summary>Identifies the group, so a folded state can be found again after a search.</summary>
    public string Key { get; }

    public string Title { get; }

    /// <summary>
    /// True on a heading that only divides the picker into parts — what a game macro reaches for,
    /// and everything else — rather than holding actions of its own. Such a heading is written
    /// smaller, cannot be folded, and never counts as a group of the catalogue.
    /// </summary>
    public bool IsSection { get; }

    /// <summary>True on the headings that actually hold actions.</summary>
    public bool HasActions => !IsSection;

    /// <summary>The mark the category is drawn with, when it has one.</summary>
    public Geometry? Icon { get; }

    public bool HasIcon => Icon is not null;

    public IReadOnlyList<ActionDefinition> Actions { get; }

    /// <summary>
    /// A sentence the group says about itself, shown under its heading. Only the block group has
    /// one: it is the group whose members are easiest to confuse with something else.
    /// </summary>
    public string Note { get; }

    public bool HasNote => Note.Length > 0;

    /// <summary>What the heading reads, naming the category and how much is under it.</summary>
    public string Header => Strings.Format("Add.ActionGroup", Title, Actions.Count);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caret))]
    public partial bool IsOpen { get; set; }

    /// <summary>The mark that says the heading opens and closes, pointing the way it will go.</summary>
    public Geometry Caret => IsOpen ? Carets.Open : Carets.Shut;
}
