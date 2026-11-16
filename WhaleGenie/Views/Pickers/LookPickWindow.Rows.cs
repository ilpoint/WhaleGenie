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

// The rows of the list: their swatches, the text beside them, and the menu each one carries.
public partial class LookPickWindow : AppWindow
{
    /// <summary>
    /// The list of lines as they stand, drawn again whenever one of them changes: adding, moving,
    /// editing and removing all end here, so what is on screen is always the list itself.
    /// </summary>
    private void RebuildRows()
    {
        if (_entries is null)
        {
            return;
        }

        if (this.FindControl<TextBlock>("CountText") is { } count)
        {
            count.Text = _added.Count.ToString(CultureInfo.CurrentCulture);
        }

        _entries.Children.Clear();

        // 空列表也说出它是什么：一块空白的薄板看起来只是一个空框，写一行字才像"还没有加入"。
        if (_added.Count == 0)
        {
            _entries.Children.Add(new TextBlock
            {
                Text = Strings.Get("LookPick.NothingAdded"),
                Classes = { "Hint" },
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 3, 2, 3),
            });

            return;
        }

        for (var index = 0; index < _added.Count; index++)
        {
            _entries.Children.Add(Row(index));
        }
    }

    /// <summary>
    /// One line of the list: where the place is, which colours it may be, and whether it is strict
    /// about them. Right-clicking it edits it, gives it another colour or removes it; left-clicking
    /// picks it out, which is how the next click on the picture moves it — that is how a marker that
    /// landed a pixel off is put right without retyping its numbers.
    /// </summary>
    private Border Row(int index)
    {
        var line = _added[index];
        var fields = ColourPointText.Split(line);
        var offsets = fields.Count >= 2 ? $"{fields[0]}, {fields[1]}" : line;

        // 一行就是一处：序号、颜色（几种并排）、偏移，右边再写它是不是「不能是」、容差多少。
        var colours = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };

        foreach (var colour in ColourPointLine.Colours(line))
        {
            colours.Children.Add(Swatch(colour));
        }

        var number = new TextBlock
        {
            Text = (index + 1).ToString(CultureInfo.CurrentCulture) + ".",
            FontSize = 12,
            MinWidth = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush.Of("TextTertiary"),
        };

        var places = new TextBlock
        {
            Text = offsets,
            FontSize = 11.5,
            Classes = { "Mono" },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush.Of("TextSecondary"),
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 7,
        };

        grid.Children.Add(number);
        Grid.SetColumn(colours, 1);
        grid.Children.Add(colours);
        Grid.SetColumn(places, 2);
        grid.Children.Add(places);

        var marks = Marks(line);
        if (marks.Length > 0)
        {
            var badge = new TextBlock
            {
                Text = marks,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeBrush.Of("TextTertiary"),
            };

            Grid.SetColumn(badge, 3);
            grid.Children.Add(badge);
        }

        var row = new Border
        {
            // 每条自带一圈细边、底板比外框亮一档：只把几行字排在一起，看起来不像一张能逐条点选的列表。
            Background = ThemeBrush.Of(index == _selected ? "AccentSurface" : "SurfaceCard"),
            BorderBrush = ThemeBrush.Of(index == _selected ? "AccentText" : "BorderControl"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 5),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid,

            // 整条写在提示里：色块省掉了字的长度，但把这一行到底存了什么写全仍然有用。
            Tag = index,
        };

        ToolTip.SetTip(row, line);
        row.ContextMenu = Menu(index);

        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                PickLine(index);
            }
        };

        return row;
    }

    /// <summary>One colour of a place, or what was written there when it is not a colour.</summary>
    private static Control Swatch(string colour)
    {
        if (ColourPointLine.TryColourText(colour, out var hex))
        {
            var swatch = new Border
            {
                Width = 15,
                Height = 15,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderBrush = ThemeBrush.Of("BorderControl"),
                BorderThickness = new Thickness(1),
            };

            ToolTip.SetTip(swatch, hex);
            return swatch;
        }

        // 写的是算式或变量：画不出颜色，就把写的东西摆出来，免得看起来像一格空的。
        return new TextBlock
        {
            Text = colour,
            FontSize = 11,
            Classes = { "Mono" },
            Foreground = ThemeBrush.Of("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// The two things the table says about a place that the picture cannot show: that it must be
    /// none of those colours, and that it allows a tolerance of its own.
    /// </summary>
    private static string Marks(string line)
    {
        var marks = new List<string>();
        if (ColourPointLine.MustNot(line))
        {
            marks.Add("≠");
        }

        var tolerance = ColourPointLine.Tolerance(line);
        if (tolerance.Length > 0)
        {
            marks.Add($"±{tolerance}");
        }

        return string.Join(" ", marks);
    }

    /// <summary>The menu a place has, whether it was right-clicked on the picture or on the list.</summary>
    private ContextMenu Menu(int index)
    {
        var edit = new MenuItem { Header = Strings.Get("LookPick.Edit"), Tag = index };
        edit.Click += OnEditEntry;
        var alternative = new MenuItem { Header = Strings.Get("LookPick.AddAlternative"), Tag = index };
        alternative.Click += OnAddAlternative;
        var remove = new MenuItem { Header = Strings.Get("LookPick.Remove"), Tag = index };
        remove.Click += OnRemoveEntry;
        return new ContextMenu { Items = { edit, alternative, remove } };
    }

    /// <summary>Picks a line out, or puts it back if it was already picked out.</summary>
    internal void PickLine(int index)
    {
        if (_selected == index)
        {
            _selected = -1;
            _picked = null;
        }
        else
        {
            _selected = index;
            _picked = index < _addedAt.Count ? _addedAt[index] : null;
        }

        RebuildRows();
        Draw();
        Say(_selected < 0
            ? Strings.Get("LookPick.NothingPicked")
            : Strings.Get("LookPick.Picked"));
    }
}
