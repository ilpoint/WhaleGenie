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

// Drawing the picture and the marks on it: their washes, outlines and rows.
public partial class LookWindow : AppWindow
{
    /// <summary>Redraws the picture with every mark on it, and the list beside it.</summary>
    private void Draw()
    {
        if (_board is null || _marks is null)
        {
            return;
        }

        _drawn.Clear();
        _board.Children.Clear();
        _marks.Children.Clear();
        _markTexts.Clear();
        _markRoles.Clear();
        _markRows.Clear();
        _markToggles.Clear();

        _board.Width = Math.Max(1, _look.Frame.Width * _zoom);
        _board.Height = Math.Max(1, _look.Frame.Height * _zoom);

        if (_frame is not null)
        {
            var picture = new Image
            {
                Source = _frame,
                Width = _board.Width,
                Height = _board.Height,
                Stretch = Stretch.Fill,
            };

            RenderOptions.SetBitmapInterpolationMode(picture, _zoom >= 1
                ? BitmapInterpolationMode.None
                : BitmapInterpolationMode.HighQuality);

            Canvas.SetLeft(picture, 0);
            Canvas.SetTop(picture, 0);
            _board.Children.Add(picture);
        }

        for (var index = 0; index < _look.Boxes.Count; index++)
        {
            var box = _look.Boxes[index];
            var chosen = index + 1 == _look.ChosenIndex;
            var place = LookGeometry.Place(_look.Origin, box.Match.Location, box.Match.Size, _zoom);
            var visible = LookGeometry.Visible(place, SmallestMark);
            var visual = MarkVisual(box, visible, chosen);
            visual.Tag = index;

            Canvas.SetLeft(visual, visible.X);
            Canvas.SetTop(visual, visible.Y);
            _board.Children.Add(visual);

            var row = MarkRow(index, box, chosen, visible);
            _marks.Children.Add(row);

            _drawn.Add((box, visual));
        }

        // A redraw keeps the filter that was typed: looking again at the same step should not put
        // back the rows the person just narrowed away.
        ApplyMarkFilter();
    }

    /// <summary>
    /// 盖在画面上的半透明罩。**留字面值，不走令牌**：它必须能透出底下的截图，
    /// 而令牌表里没有"半透明填充"这一档（已记进待立族）。换成不透明令牌就等于把画面盖住。
    /// </summary>
    private static IBrush Wash(LookRole role) => role switch
    {
        LookRole.Hit => new SolidColorBrush(Color.FromArgb(46, 63, 191, 95)),
        LookRole.Target => new SolidColorBrush(Color.FromArgb(40, 255, 59, 48)),
        _ => Brushes.Transparent,
    };

    /// <summary>
    /// 框的描边。**外观是逐行算的、不走类**（`MarkRow` 也一样）：选中的、候选的、目标的外观各不相同，
    /// 类描述的是"这一类长什么样"，两件事。但**颜色走语义令牌**——这是"用类"与"自绘"之间的第三态。
    /// </summary>
    private static IBrush? Line(LookRole role) => ThemeBrush.Of(role switch
    {
        LookRole.Hit => "Success",
        LookRole.Candidate => "Warning",
        LookRole.Target => "Danger",
        _ => "TextTertiary",
    });

    /// <summary>
    /// One mark: a box, and nothing else. What was read where the box is sits over the box rather
    /// than in it, and only when it was asked for — see <see cref="Label"/>.
    /// </summary>
    private static Border MarkVisual(LookBox box, LookRect rect, bool chosen)
    {
        var mark = new Border
        {
            Width = rect.Width,
            Height = rect.Height,
            BorderBrush = Line(box.Role),
            BorderThickness = new Thickness(chosen ? 3 : 2),
            Background = Wash(box.Role),
            CornerRadius = new CornerRadius(1),
        };

        if (box.Role == LookRole.Target)
        {
            // The place a step acted on is a single point, so it is drawn as a crosshair rather
            // than a box: there is no rectangle to see.
            mark.CornerRadius = new CornerRadius(rect.Width / 2);
            mark.Child = new Border
            {
                Width = 2,
                Height = 2,
                Background = Line(box.Role),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
        }

        return mark;
    }

    /// <summary>
    /// One row of the list beside the picture. Hovering it lights up the mark it stands for, and
    /// clicking it brings that mark into the middle of the window.
    /// </summary>
    private Border MarkRow(int index, LookBox box, bool chosen, LookRect visible)
    {
        var row = new Border { Classes = { "MarkRow" } };
        if (chosen)
        {
            row.Classes.Add("chosen");
        }

        // What this row says, kept beside it: the filter reads it, and so does copying the list.
        var role = Strings.Get("Look.Role." + box.Role);
        _markRoles.Add(role);
        _markTexts.Add(string.IsNullOrEmpty(box.Label) ? Detail(box) : box.Label);
        _markRows.Add(row);

        var copy = new MenuItem { Header = Strings.Get("Look.CopyRow"), Tag = index };
        copy.Click += OnCopyRow;
        row.ContextMenu = new ContextMenu { Items = { copy } };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
        };

        var toggle = new CheckBox { IsChecked = true, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        _markToggles.Add(toggle);
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (_drawn.Count > index)
            {
                _drawn[index].Visual.IsVisible = toggle.IsChecked == true;
            }
        };

        Grid.SetColumn(toggle, 0);
        grid.Children.Add(toggle);

        var chip = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(2),
            Background = Line(box.Role),
            Margin = new Thickness(2, 0, 8, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        Grid.SetColumn(chip, 1);
        grid.Children.Add(chip);

        var words = new StackPanel { Spacing = 1 };
        words.Children.Add(new TextBlock
        {
            Text = role,
            FontSize = 12,
            Foreground = ThemeBrush.Of("TextPrimary"),
        });

        words.Children.Add(new TextBlock
        {
            Text = Detail(box),
            FontSize = 11.5,
            Foreground = ThemeBrush.Of("TextTertiary"),
            TextWrapping = TextWrapping.Wrap,
        });

        // What was read here, in a box of its own and under a caption of its own. It used to be one
        // more line of the same grey as the numbers above it, and the two are not the same kind of
        // thing: one is where on the screen the mark is, the other is what the step read there. The
        // caption says which is which; the box keeps the writing together when it wraps to four
        // lines and the numbers stay two lines above it.
        if (!string.IsNullOrEmpty(box.Label))
        {
            var reading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

            reading.Children.Add(new TextBlock
            {
                Text = Strings.Get("Look.ReadTag"),
                FontSize = 10.5,
                Foreground = ThemeBrush.Of("TextTertiary"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 6, 0),
            });

            var text = new TextBlock
            {
                Text = box.Label,
                FontSize = 11.5,
                // "这是程序读出来的内容"——由**等宽**说，不由绿色说：
                // 绿在这套界面已经表示"成功"，再借一次就是两种含义共用一个色。
                Classes = { "Code" },
                TextWrapping = TextWrapping.Wrap,
            };

            Grid.SetColumn(text, 1);
            reading.Children.Add(text);

            words.Children.Add(new Border
            {
                Background = ThemeBrush.Of("SurfaceSunken"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4),
                Margin = new Thickness(0, 3, 0, 0),
                Child = reading,
            });
        }

        // How far this one was from counting, for the steps that named a score to reach. The line
        // at the top says what the best of them was; this says what each one needed.
        if (Short(box) is { } short_)
        {
            words.Children.Add(new TextBlock
            {
                Text = short_,
                FontSize = 11.5,
                Foreground = ThemeBrush.Of("Warning"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Grid.SetColumn(words, 2);
        grid.Children.Add(words);

        var score = new TextBlock
        {
            Text = box.Role == LookRole.Area ? string.Empty : Score(box.Match.Score, _look.Kind),
            FontSize = 12,
            // 分数是附注级读数，与详情同档；要区分靠位置和标签，不靠颜色。
            Foreground = ThemeBrush.Of("TextSecondary"),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };

        Grid.SetColumn(score, 3);
        grid.Children.Add(score);

        row.Child = grid;

        row.PointerEntered += (_, _) => Light(index, true);
        row.PointerExited += (_, _) => Light(index, false);
        row.PointerPressed += (_, _) => Bring(index);
        return row;
    }
}
