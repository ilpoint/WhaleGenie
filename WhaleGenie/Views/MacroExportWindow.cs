using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// Chooses which macros a file is to hold.
/// </summary>
/// <remarks>
/// The file is an ordinary package, so this window is the only thing that says "these three, not
/// the whole project". What a tick means is <see cref="MacroExportSelection"/>'s business; this only
/// draws it and names what the file will end up carrying.
///
/// Calls to macros that are not coming along are named before the file is written. The export still
/// happens, because a half-finished export is a normal thing to want. What must not happen is
/// finding out at run time.
/// </remarks>
public sealed class MacroExportWindow : Window
{
    private readonly MacroExportSelection _selection;
    private readonly ProjectTriggers _triggers;
    private readonly List<(MacroExportSelection.Node Node, CheckBox Box)> _rows = [];
    private readonly TextBlock _counts;
    private readonly TextBlock _warning;
    private readonly Button _export;
    private readonly CheckBox _all;
    private bool _syncing;

    internal MacroExportWindow(IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IEnumerable<MacroItem>? preselected)
    {
        _triggers = triggers;
        _selection = new MacroExportSelection(macros, preselected);

        Width = 560;
        Height = 560;
        MinWidth = 440;
        MinHeight = 380;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        Title = Strings.Get("Export.Title");
        Background = Brush("AppBackdrop", Color.FromRgb(32, 32, 32));

        var heading = new TextBlock
        {
            Text = Strings.Get("Export.Title"),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("AppTextStrong", Colors.White),
            Margin = new Thickness(0, 0, 0, 6),
        };

        var hint = new TextBlock
        {
            Text = Strings.Get("Export.Hint"),
            FontSize = 11.5,
            Foreground = Brush("AppMuted", Color.FromRgb(160, 160, 160)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };

        var rows = new StackPanel { Spacing = 1 };
        foreach (var node in _selection.All)
        {
            rows.Children.Add(Row(node));
        }

        _counts = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Brush("AppMuted", Color.FromRgb(160, 160, 160)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _warning = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Brush("Warning", Color.FromRgb(224, 108, 117)),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 6, 0, 0),
        };

        _all = new CheckBox
        {
            Content = Strings.Get("Export.All"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _all.IsCheckedChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            _selection.SetAll(_all.IsChecked == true);
            Sync();
        };

        _export = new Button
        {
            Content = Strings.Get("Export.Do"),
            Height = 32,
            MinWidth = 84,
            Padding = new Thickness(11, 0),
        };
        _export.Click += (_, _) => Close(_selection.Chosen);

        var cancel = new Button
        {
            Content = Strings.Get("Common.Cancel"),
            Height = 32,
            MinWidth = 84,
            Padding = new Thickness(11, 0),
        };
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0),
            Children = { _all, cancel, _export },
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 10, 0, 0),
        };
        footer.Children.Add(_counts);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);

        var scroll = new ScrollViewer { Content = rows };
        var bottom = new StackPanel { Children = { _warning, footer } };
        Grid.SetRow(hint, 1);
        Grid.SetRow(scroll, 2);
        Grid.SetRow(bottom, 3);

        Content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Children = { heading, hint, scroll, bottom },
        };

        Sync();
    }

    /// <summary>The macros that were ticked, or null when the window was closed without exporting.</summary>
    public static async Task<IReadOnlyList<MacroItem>?> ShowAsync(Window owner,
        IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IEnumerable<MacroItem>? preselected = null)
    {
        var window = new MacroExportWindow(macros, triggers, preselected);
        return await window.ShowDialogOver<IReadOnlyList<MacroItem>?>(owner);
    }

    private Control Row(MacroExportSelection.Node node)
    {
        var label = node.IsSub
            ? $"{node.Macro.Name}  ·  {Strings.Get("Export.SubMacro")}"
            : node.Macro.Name;
        var box = new CheckBox
        {
            Content = label,
            IsChecked = node.IsChecked,
            IsEnabled = !node.IsCovered,
            Margin = new Thickness(Depth(node) * 18, 0, 0, 0),
            FontSize = 12.5,
        };

        box.IsCheckedChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            _selection.SetChecked(node, box.IsChecked == true);
            Sync();
        };

        _rows.Add((node, box));
        return box;
    }

    private static int Depth(MacroExportSelection.Node node)
        => node.Parent is null ? 0 : Depth(node.Parent) + 1;

    /// <summary>
    /// Redraws every row from the selection, then says what the file will hold. Covering a macro's
    /// submacros changes rows the person did not touch, so one place puts the whole list back in
    /// step rather than each click patching its own neighbours.
    /// </summary>
    private void Sync()
    {
        _syncing = true;
        foreach (var (node, box) in _rows)
        {
            box.IsChecked = node.IsChecked;
            box.IsEnabled = !node.IsCovered;
        }

        _all.IsChecked = _selection.Roots.Count > 0 && _selection.Roots.All(root => root.IsChecked);
        _syncing = false;

        Refresh();
    }

    private void Refresh()
    {
        var chosen = _selection.Chosen;
        _export.IsEnabled = chosen.Count > 0;

        if (chosen.Count == 0)
        {
            _counts.Text = Strings.Get("Export.Nothing");
            _warning.IsVisible = false;
            return;
        }

        var globals = MacroBundle.UsedGlobals(chosen).Count;
        var entries = MacroBundle.Take(_triggers, chosen).Entries.Count;
        _counts.Text = Strings.Format("Export.Counts", chosen.Count, globals, entries);

        var dangling = MacroBundle.DanglingCalls(chosen);
        _warning.Text = dangling.Count == 0
            ? string.Empty
            : Strings.Format("Export.Dangling", string.Join("、", dangling));
        _warning.IsVisible = dangling.Count > 0;
    }

    private static IBrush Brush(string key, Color fallback)
        => Application.Current?.TryFindResource(key, out var found) == true && found is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 38)
        {
            BeginMoveDrag(e);
        }
    }
}
