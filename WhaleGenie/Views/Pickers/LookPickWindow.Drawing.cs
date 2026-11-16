using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// Drawing the picture the window shows: the marks on it, and where a screen point lands.
public partial class LookPickWindow : AppWindow
{
    private void Draw()
    {
        if (_board is null)
        {
            return;
        }

        var width = Math.Max(1, _frame.Width * ZoomLevel);
        var height = Math.Max(1, _frame.Height * ZoomLevel);
        _board.Width = width;
        _board.Height = height;
        _board.Children.Clear();

        if (_bitmap is not null)
        {
            var picture = new Image
            {
                Source = _bitmap,
                Width = width,
                Height = height,
                Stretch = Stretch.Fill,
            };

            RenderOptions.SetBitmapInterpolationMode(picture, ZoomLevel >= 1
                ? BitmapInterpolationMode.None
                : BitmapInterpolationMode.HighQuality);

            Canvas.SetLeft(picture, 0);
            Canvas.SetTop(picture, 0);
            _board.Children.Add(picture);
        }

        for (var index = 0; index < _boxes.Count; index++)
        {
            var box = _boxes[index];
            var place = LookGeometry.Place(_origin, box.Match.Location, box.Match.Size, ZoomLevel);
            var visible = LookGeometry.Visible(place, SmallestMark);
            var mark = Mark(box, visible, index + 1 == _chosen);

            Canvas.SetLeft(mark, visible.X);
            Canvas.SetTop(mark, visible.Y);
            _board.Children.Add(mark);
        }

        // What offsets count from: the pixel the step went with, marked in orange. The ring grows
        // with the zoom so that at 800% it still hugs that one pixel instead of spilling into the
        // ones beside it — a ring bigger than the cell it names is read as naming the next cell.
        var centre = ToBoardCentre(_centre);
        var ringSize = Math.Clamp((ZoomLevel * 1.2) + 4, 11, 22);
        var ring = new Border
        {
            Width = ringSize,
            Height = ringSize,
            // 拾取器可见性色：要在任意截图底色上可见，故不随主题（判定为字面，不进令牌）。
            BorderBrush = new SolidColorBrush(Color.Parse("#E0A030")),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(ringSize / 2),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(ring, centre.X - (ringSize / 2));
        Canvas.SetTop(ring, centre.Y - (ringSize / 2));
        _board.Children.Add(ring);

        for (var index = 0; index < _addedAt.Count; index++)
        {
            var at = ToBoardCentre(_addedAt[index]);
            var dotSize = Math.Clamp((ZoomLevel * 0.8) + 4, 8, 15);
            var dot = new Border
            {
                Width = dotSize,
                Height = dotSize,
                // 拾取器可见性色：要在任意截图底色上可见，故不随主题（判定为字面，不进令牌）。
                Background = new SolidColorBrush(Color.Parse(index == _selected ? "#FF3B30" : "#3FBF5F")),
                CornerRadius = new CornerRadius(dotSize / 2),
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(dot, at.X - (dotSize / 2));
            Canvas.SetTop(dot, at.Y - (dotSize / 2));
            _board.Children.Add(dot);

            // 行号写在圆点旁边：右侧那一列靠编号与图上的点对应，靠数一串同色的圆点是数不清的。
            var number = new TextBlock
            {
                Name = "MarkerNumber",
                Text = (index + 1).ToString(CultureInfo.CurrentCulture),
                FontSize = 10.5,
                Foreground = Brushes.White,
                // 自绘：浮在截图上的深色薄板，令牌表里没有这一档。
                Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 20)),
                Padding = new Thickness(3, 0, 3, 1),
                IsHitTestVisible = false,
            };

            var above = at.Y - 17;
            Canvas.SetLeft(number, at.X + 5);
            Canvas.SetTop(number, above >= 0 ? above : at.Y + 4);
            _board.Children.Add(number);
        }

        if (_picked is { } picked)
        {
            var at = ToBoardCentre(picked);
            var cross = new Border
            {
                Width = 15,
                Height = 15,
                BorderBrush = new SolidColorBrush(Color.Parse("#FF3B30")),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Child = new Border
                {
                    Width = 3,
                    Height = 3,
                    Background = new SolidColorBrush(Color.Parse("#FF3B30")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
                IsHitTestVisible = false,
            };

            Canvas.SetLeft(cross, at.X - 7);
            Canvas.SetTop(cross, at.Y - 7);
            _board.Children.Add(cross);
        }
    }

    /// <summary>One mark of the look on the picture: the chosen one heavier than its neighbours.</summary>
    private static Border Mark(LookBox box, LookRect rect, bool chosen) => new()
    {
        Width = rect.Width,
        Height = rect.Height,
        BorderThickness = new Thickness(chosen ? 3 : 1.5),
        BorderBrush = ThemeBrush.Of(box.Role switch
        {
            LookRole.Hit => "Success",
            LookRole.Candidate => "Warning",
            LookRole.Target => "Danger",
            _ => "TextTertiary",
        }),
        Background = Brushes.Transparent,
        CornerRadius = new CornerRadius(1),
        IsHitTestVisible = false,
    };
}
