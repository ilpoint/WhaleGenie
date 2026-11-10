using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// Keys, saving the picture with or without its marks, and the canvas that is written out.
public partial class LookWindow : AppWindow
{
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // Esc leaves the full screen before it leaves the window: the two are one key because
            // filling the screen is the last thing that happened, and that is what one wants undone.
            case Key.Escape:
                e.Handled = true;
                if (WindowState == WindowState.FullScreen)
                {
                    ToggleFullScreen();
                }
                else
                {
                    Close();
                }

                break;

            case Key.F11:
                e.Handled = true;
                ToggleFullScreen();
                break;

            case Key.OemPlus:
            case Key.Add:
                e.Handled = true;
                Zoom(LookGeometry.Step(_zoom, true), true);
                break;

            case Key.OemMinus:
            case Key.Subtract:
                e.Handled = true;
                Zoom(LookGeometry.Step(_zoom, false), true);
                break;

            case Key.D0:
                e.Handled = true;
                Zoom(Fit(), false);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Writes the picture out with the marks on it, at its own size rather than at whatever the
    /// window happens to be zoomed to: the point of saving it is to show somebody else what was
    /// seen, and a picture shrunk to fit a window is not that.
    /// </summary>
    private void OnSave(object? sender, RoutedEventArgs e)
        => SaveWith(title: "Look.SaveTitle", name: "look-" + _look.StepId, marks: true);

    /// <summary>
    /// Writes the picture out as it came off the screen, without a single mark on it. What a step
    /// looked at is also what somebody cuts a fresh reference picture out of, and marks drawn over
    /// it would come along into the template and make it match only itself.
    /// </summary>
    private void OnSaveRaw(object? sender, RoutedEventArgs e)
        => SaveWith(title: "Look.SaveRawTitle", name: "screen-" + _look.StepId, marks: false);

    private async void SaveWith(string title, string name, bool marks)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get(title),
            SuggestedFileName = name + ".png",
            FileTypeChoices =
            [
                new FilePickerFileType(Strings.Get("Look.SaveFilter")) { Patterns = ["*.png"] },
            ],
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            Save(path, marks);
            SetStatus(Strings.Format("Look.Saved", path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus(Strings.Format("Look.SaveFailed", error.Message));
        }
    }

    private void SetStatus(string text)
    {
        if (this.FindControl<TextBlock>("Status") is { } status)
        {
            status.Text = text;
        }
    }

    private void Save(string path, bool marks)
    {
        var width = Math.Max(1, _look.Frame.Width);
        var height = Math.Max(1, _look.Frame.Height);
        var board = SavedBoard(1, marks);
        board.Width = width;
        board.Height = height;
        board.Measure(new Size(width, height));
        board.Arrange(new Rect(0, 0, width, height));
        board.UpdateLayout();

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(board);
        using var stream = File.Create(path);
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }

    /// <summary>
    /// The picture as a saved file gets it — with every mark drawn over it, or bare. Built fresh
    /// each time: what is on screen is one instance of it and this is another, because a control
    /// cannot hang in two places at once.
    /// </summary>
    internal Canvas SavedBoard(double zoom, bool marks)
    {
        // 自绘：**这是"图还没画上去时的画布"**，不是界面色。别把它当"深色背景令牌"换掉——
        // 换掉之后图一出来，底下会透出别的颜色。
        var board = new Canvas { Background = new SolidColorBrush(Color.Parse("#141414")) };
        Picture(board, zoom);
        if (!marks)
        {
            return board;
        }

        for (var index = 0; index < _look.Boxes.Count; index++)
        {
            var box = _look.Boxes[index];
            var place = LookGeometry.Place(_look.Origin, box.Match.Location, box.Match.Size, zoom);
            var visible = LookGeometry.Visible(place, SmallestMark);
            var mark = MarkVisual(box, visible, index + 1 == _look.ChosenIndex);
            mark.Tag = index;

            Canvas.SetLeft(mark, visible.X);
            Canvas.SetTop(mark, visible.Y);
            board.Children.Add(mark);
        }

        return board;
    }

    private void Picture(Canvas board, double zoom)
    {
        if (_frame is null)
        {
            return;
        }

        var picture = new Image
        {
            Source = _frame,
            Width = _look.Frame.Width * zoom,
            Height = _look.Frame.Height * zoom,
            Stretch = Stretch.Fill,
        };

        Canvas.SetLeft(picture, 0);
        Canvas.SetTop(picture, 0);
        board.Children.Add(picture);
    }
}
