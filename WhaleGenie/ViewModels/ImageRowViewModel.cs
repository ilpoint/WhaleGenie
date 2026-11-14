using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One of the pictures a search looks for, as the dialog edits it: what the row names — a file, or
/// the variable a Capture step saved — and the picture itself underneath it, because a reference
/// picture is something one recognises by looking at it and not by reading its name.
/// </summary>
public partial class ImageRowViewModel : ViewModelBase
{
    /// <summary>The value this row keeps. The line below is only how a person types into it.</summary>
    private ExpressionNode _shape = Formula.Nothing;

    /// <summary>True while the line is being drawn from a stored shape, so it is not read back.</summary>
    private bool _composing;

    /// <summary>The value as the shape the step stores, rather than the line it was typed as.</summary>
    public ExpressionNode Shape => _shape;

    /// <summary>What this row names, which the engine reads as a path or as an image variable.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>What a row with nothing in it shows, so the shape of a path is never a guess.</summary>
    [ObservableProperty]
    public partial string Placeholder { get; set; } = string.Empty;

    /// <summary>
    /// The names this row is offered while it is typed in: a picture an earlier Capture saved goes
    /// in by name, and a name is not something to spell out from memory. The rows come from the
    /// parameter this row belongs to, which is what knows the names.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; set; } = [];

    /// <summary>
    /// What takes this row out of the list it is in. The list belongs to the parameter, so the row
    /// is told what to call rather than knowing where it is.
    /// </summary>
    public Action<ImageRowViewModel>? Take { get; set; }

    /// <summary>
    /// What moves this row one place up or down the list, the way -1 and 1 say. The list owns the
    /// order, so the row says which way and the list does the moving. The order is not decoration:
    /// the pictures are tried in the order they are listed and the first one that turns up is the
    /// one the step goes with, so this is how a user says which picture to try first.
    /// </summary>
    public Action<ImageRowViewModel, int>? Move { get; set; }

    /// <summary>True while there is another picture above this one.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    public partial bool CanMoveUp { get; set; }

    /// <summary>True while there is another picture below this one.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    public partial bool CanMoveDown { get; set; }

    /// <summary>
    /// Where pictures live while this dialog is open: beside the macro package when it has a path,
    /// and WhaleGenie's own folder otherwise. A relative picture value is looked up here.
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
        }
    }

    private string _assetFolder = string.Empty;

    /// <summary>The picture this row names, or null while there is nothing to show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    public partial Bitmap? Thumbnail { get; set; }

    /// <summary>True when there is a picture to show.</summary>
    public bool HasThumbnail => Thumbnail is not null;

    /// <summary>Re-reads the picture this row names, so the preview always matches the text.</summary>
    public void RefreshThumbnail()
    {
        var previous = Thumbnail;
        Thumbnail = Load();
        previous?.Dispose();
    }

    private Bitmap? Load()
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
            // A file that turns out not to be a picture is not worth a crash: the row still shows
            // the path, and the run reports it if the macro is used.
            return null;
        }
    }

    partial void OnTextChanged(string value)
    {
        // Typing is the one place a line becomes a value here. It is read once, into a shape, and
        // the step is built from that shape rather than from a second reading of the line.
        if (!_composing)
        {
            _shape = value.Trim().Length == 0 ? Formula.Nothing : Formula.FromLine(value);
        }

        RefreshThumbnail();
    }

    /// <summary>Takes this picture out of the list.</summary>
    [RelayCommand]
    private void Remove() => Take?.Invoke(this);

    /// <summary>Puts this picture one place nearer the top of the list.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move?.Invoke(this, -1);

    /// <summary>Puts this picture one place nearer the bottom of the list.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move?.Invoke(this, 1);

    /// <summary>This row the way the step stores it: one picture, under the column the engine reads.</summary>
    public StepParameterRow ToRow()
    {
        var row = new StepParameterRow();
        row.Set("image", _shape);
        return row;
    }

    /// <summary>
    /// One picture back out of the macro file. The shape is what the file holds; the line is drawn
    /// from it for the box, without reading the line back and losing a name's identity.
    /// </summary>
    public static ImageRowViewModel From(StepParameterRow row)
    {
        var image = new ImageRowViewModel();
        image._composing = true;
        try
        {
            image.Text = row.Text("image");
        }
        finally
        {
            image._composing = false;
        }

        image._shape = row.Cell("image");
        image.RefreshThumbnail();
        return image;
    }
}
