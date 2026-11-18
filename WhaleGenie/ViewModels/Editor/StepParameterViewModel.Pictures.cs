using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.ViewModels;

// The pictures a step looks for, and the thumbnail beside the field.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>
    /// Takes the ways of naming the element a pick answered with, the first of them already in the
    /// field. They are listed steadiest first, and each one put in adds a line to the field: the
    /// step tries them in that order, so a second way of finding the element is what keeps it
    /// working after the page has been redesigned.
    /// </summary>
    public void OfferCandidates(IReadOnlyList<PickCandidate> candidates)
    {
        Candidates.Clear();
        foreach (var candidate in candidates)
        {
            Candidates.Add(new BrowserWayViewModel
            {
                Selector = candidate.Selector,
                Text = Strings.Format("Add.BrowserWay", Way(candidate.Kind), candidate.Matches),
                Take = way => AddLine(way.Selector),
            });
        }

        OnPropertyChanged(nameof(HasCandidates));
    }

    /// <summary>What a kind of naming is called, in the reader's own words.</summary>
    private static string Way(PickKind kind) => Strings.Get("Add.BrowserWay." + kind);

    /// <summary>
    /// Adds one more line to the field, which is one more way of finding the element: either a
    /// selector a pick offered, or the name of a variable holding one. A name goes on a line of its
    /// own rather than onto the end of the last one, which would make it part of that selector.
    /// </summary>
    public void AddLine(string line)
    {
        var lines = Text.Replace("\r\n", "\n").Split('\n')
            .Select(written => written.TrimEnd())
            .Where(written => written.Length > 0)
            .Append(line);
        SetWritten(Formula.Text(string.Join(Environment.NewLine, lines)));
    }

    /// <summary>
    /// The picture this field points at, whichever way it holds it: the one value of a single
    /// picture field, or the first picture of a list of them. Empty when nothing is filled in.
    /// </summary>
    public string PictureText => IsImageList
        ? Pictures.FirstOrDefault(picture => !string.IsNullOrWhiteSpace(picture.Text))?.Text.Trim()
            ?? string.Empty
        : CurrentText.Trim();

    /// <summary>Puts another picture at the end of the list, for the user to fill in.</summary>
    public void AddPicture(ImageRowViewModel? row = null)
    {
        var added = row ?? new ImageRowViewModel();
        added.Take = RemovePicture;
        added.Move = MovePicture;
        added.Placeholder = Definition.Placeholder;
        added.AssetFolder = _assetFolder;
        // A row of pictures says a file or the picture an earlier step saved, and the second one is
        // a name: the names go over the row's own box the same way they go over any other box that
        // takes one.
        added.Suggestions = NameChoices;
        added.PropertyChanged += OnPictureChanged;
        Pictures.Add(added);
        RefreshEnds();
        OnPropertyChanged(nameof(HasPictures));
    }

    /// <summary>Takes one picture out of the list.</summary>
    public void RemovePicture(ImageRowViewModel row)
    {
        row.PropertyChanged -= OnPictureChanged;
        Pictures.Remove(row);
        RefreshEnds();
        OnPropertyChanged(nameof(HasPictures));
    }

    /// <summary>
    /// Moves one picture one place up or down the list, the way the row's own buttons ask for. The
    /// order the pictures are listed in is the order the step tries them in, so this is the one
    /// thing a user changes about a list that is already written.
    /// </summary>
    public void MovePicture(ImageRowViewModel row, int by)
    {
        var at = Pictures.IndexOf(row);
        var to = at + by;
        if (at < 0 || to < 0 || to >= Pictures.Count)
        {
            return;
        }

        Pictures.Move(at, to);
        RefreshEnds();
    }

    /// <summary>
    /// Says which rows have somewhere to move to, so the button at either end of the list is dimmed
    /// rather than doing nothing when it is pressed.
    /// </summary>
    private void RefreshEnds()
    {
        for (var index = 0; index < Pictures.Count; index++)
        {
            Pictures[index].CanMoveUp = index > 0;
            Pictures[index].CanMoveDown = index < Pictures.Count - 1;
        }
    }

    /// <summary>
    /// What one row of the list says is what this field says. The dialog follows this field rather
    /// than its rows, so a name typed into a row has to be reported as a change here — otherwise
    /// the step would go on looking unsaved and the picture would go on looking unfilled.
    /// </summary>
    private void OnPictureChanged(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(nameof(HasPictures));

    /// <summary>
    /// True when this parameter names a window, which the window picker can fill in. The text is
    /// written in the same box every other piece of text a macro works out earlier is written in,
    /// and this is what puts the picker beside that box.
    /// </summary>
    public bool IsWindow => Definition.Picker is ActionPickerKind.Window;

    /// <summary>True when this field holds a captured window and its matching rule.</summary>
    public bool IsWindowRule => Definition.Kind is ActionParameterKind.WindowRule;


    /// <summary>
    /// Where pictures live while this dialog is open: beside the macro package when it has a
    /// path, and WhaleGenie's own folder otherwise. A relative picture value is looked up here.
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
            RefreshThumbnail();
            foreach (var picture in Pictures)
            {
                picture.AssetFolder = value;
            }
        }
    }

    /// <summary>The picture this parameter names, shown underneath its field, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    public partial Bitmap? Thumbnail { get; set; }

    /// <summary>True when there is a picture to show.</summary>
    public bool HasThumbnail => Thumbnail is not null;

    /// <summary>Re-reads the picture the field names, so the preview always matches the text.</summary>
    public void RefreshThumbnail()
    {
        var previous = Thumbnail;
        Thumbnail = IsImage ? LoadThumbnail() : null;
        previous?.Dispose();
    }

    private Bitmap? LoadThumbnail()
    {
        if (ImageAssets.Resolve(Text, _assetFolder) is not { } path)
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch (Exception)
        {
            // A file that turns out not to be a picture is not worth a crash: the field
            // still shows the path, and the run reports it if the macro is used.
            return null;
        }
    }

    /// <summary>True when this parameter is a colour, edited with the screen picker.</summary>
    public bool IsColor => Definition.Picker is ActionPickerKind.Color;

    /// <summary>
    /// True for a colour field that holds a list of colours rather than one: the magnifier adds to
    /// what is there instead of replacing it. A picture search asks for this, because one icon can
    /// be cropped over more than one background colour.
    /// </summary>
    public bool HoldsColours => Definition.Shape is ActionValueShape.List;
}
