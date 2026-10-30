using System;
using System.Collections.Generic;
using System.IO;
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

/// <summary>Whether a file is being written or read.</summary>
public enum MacroChooseKind
{
    Export,
    Import,
}

/// <summary>
/// Chooses which macros of a list go into a file, or come out of one.
/// </summary>
/// <remarks>
/// The two directions are the same question — a tree of macros with their submacros, and a tick —
/// so they are one window. What differs is the line underneath: an export names the calls it will
/// leave without a target, and an import says where the ticks are going, which is decided by what
/// is ticked rather than by a second choice: macros go into the project, and submacros chosen on
/// their own go to the public library, because that is where a thing a macro calls belongs.
/// </remarks>
public sealed class MacroChooseWindow : Window
{
    private readonly MacroChooseKind _kind;
    private readonly MacroTreeSelection _selection;
    private readonly ProjectTriggers _triggers;
    private readonly IReadOnlyList<VariableDefinition> _fileGlobals;
    private readonly bool? _intoLibrary;
    private readonly List<(MacroTreeSelection.Node Node, CheckBox Box)> _rows = [];
    private readonly TextBlock _counts;
    private readonly TextBlock _note;
    private readonly Button _do;
    private readonly CheckBox _all;
    private bool _syncing;

    internal MacroChooseWindow(MacroChooseKind kind, IReadOnlyList<MacroItem> macros,
        ProjectTriggers triggers, IReadOnlyList<VariableDefinition> fileGlobals,
        IEnumerable<MacroItem>? preselected, string? fileName, bool? intoLibrary = null)
    {
        _kind = kind;
        _triggers = triggers;
        _fileGlobals = fileGlobals;
        _intoLibrary = intoLibrary;
        _selection = new MacroTreeSelection(macros, preselected);

        Width = 560;
        Height = 560;
        MinWidth = 440;
        MinHeight = 380;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        Title = Strings.Get(Head);
        Background = ThemeBrush.Of("SurfaceWindow") ?? Brushes.Transparent;

        var heading = new TextBlock
        {
            Text = Strings.Get(Head),
            Classes = { "Title" },
        };

        var hint = new TextBlock
        {
            Text = Strings.Get(kind == MacroChooseKind.Export ? "Export.Hint" : "Import.Hint"),
            Classes = { "Hint" },
        };

        var rows = new StackPanel { Spacing = 1 };
        foreach (var node in _selection.All)
        {
            rows.Children.Add(Row(node));
        }

        _counts = new TextBlock
        {
            Classes = { "Hint" },
            VerticalAlignment = VerticalAlignment.Center,
        };

        _note = new TextBlock
        {
            Classes = { "Hint" },
            Foreground = ThemeBrush.Of("Warning"),
            IsVisible = false,
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

        _do = new Button
        {
            Content = Strings.Get(kind == MacroChooseKind.Export ? "Export.Do" : "Import.Do"),
            Height = 32,
            MinWidth = 84,
            Padding = new Thickness(11, 0),
            IsEnabled = false,
        };
        _do.Click += (_, _) => Close(_selection.ChosenNodes);

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
            Children = { _all, cancel, _do },
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
        var bottom = new StackPanel { Children = { _note, footer } };
        Grid.SetRow(hint, 1);
        Grid.SetRow(scroll, 2);
        Grid.SetRow(bottom, 3);

        Content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Children = { heading, hint, scroll, bottom },
        };

        if (fileName is { Length: > 0 })
        {
            hint.Text += "\n" + Strings.Format("Import.File", fileName);
        }

        Sync();
    }

    /// <summary>
    /// The rows that were ticked, or null when the window was closed without going on. Rows rather
    /// than macros, because whether a tick was a macro or one of its submacros decides where it goes.
    /// </summary>
    public static async Task<IReadOnlyList<MacroTreeSelection.Node>?> ShowAsync(Window owner,
        MacroChooseKind kind, IReadOnlyList<MacroItem> macros, ProjectTriggers triggers,
        IReadOnlyList<VariableDefinition>? fileGlobals = null,
        IEnumerable<MacroItem>? preselected = null, string? fileName = null,
        bool? intoLibrary = null)
    {
        var window = new MacroChooseWindow(kind, macros, triggers, fileGlobals ?? [], preselected,
            fileName, intoLibrary);
        return await window.ShowDialogOver<IReadOnlyList<MacroTreeSelection.Node>?>(owner);
    }

    private string Head => _kind == MacroChooseKind.Export ? "Export.Title" : "Import.Title";

    private Control Row(MacroTreeSelection.Node node)
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

    private static int Depth(MacroTreeSelection.Node node)
        => node.Parent is null ? 0 : Depth(node.Parent) + 1;

    /// <summary>
    /// Redraws every row from the selection, then says what is about to happen. Covering a macro's
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
        _do.IsEnabled = chosen.Count > 0;

        if (chosen.Count == 0)
        {
            _counts.Text = Strings.Get(_kind == MacroChooseKind.Export
                ? "Export.Nothing"
                : "Import.Nothing");
            _note.IsVisible = false;
            return;
        }

        if (_kind == MacroChooseKind.Export)
        {
            var globals = MacroBundle.UsedGlobals(chosen).Count;
            var entries = MacroBundle.Take(_triggers, chosen).Entries.Count;
            _counts.Text = Strings.Format("Export.Counts", chosen.Count, globals, entries);

            var dangling = MacroBundle.DanglingCalls(chosen);
            _note.Text = dangling.Count == 0
                ? string.Empty
                : Strings.Format("Export.Dangling", string.Join("、", dangling));
            _note.IsVisible = dangling.Count > 0;
            return;
        }

        var brought = MacroBundle.UsedGlobals(chosen, _fileGlobals).Count;
        _counts.Text = Strings.Format("Import.Counts", chosen.Count, brought);
        _note.Text = Strings.Get((_intoLibrary ?? _selection.OnlySubMacros)
            ? "Import.ToLibrary"
            : "Import.ToProject");
        _note.Foreground = ThemeBrush.Of("TextTertiary");
        _note.IsVisible = true;
    }

    private static IBrush? Brush(string key)
        => Application.Current?.TryFindResource(key, out var found) == true ? found as IBrush : null;

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
