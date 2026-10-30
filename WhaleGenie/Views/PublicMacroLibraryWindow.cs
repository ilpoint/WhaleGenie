using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>Lists, creates, edits and removes the callable macros shared by every project.</summary>
public sealed class PublicMacroLibraryWindow : Window
{
    private sealed record Row(PublicMacroEntry Entry)
    {
        public string Title => $"{Entry.Macro.Name}  ·  v{Entry.Version}";
        public string Description => Entry.Macro.Description;
    }

    private readonly ObservableCollection<Row> _rows = [];
    private readonly ListBox _list;
    private readonly TextBlock _status;
    private IReadOnlyList<PublicMacroEntry> _entries;
    private readonly bool _locked;

    private PublicMacroLibraryWindow(IReadOnlyList<PublicMacroEntry> entries, string? loadError)
    {
        _entries = [.. entries];
        _locked = loadError is not null;
        Width = 620;
        Height = 560;
        MinWidth = 500;
        MinHeight = 420;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        Title = Strings.Get("PublicMacroManager.Title");
        Background = ThemeBrush.Of("SurfaceWindow") ?? Brushes.Transparent;

        var newButton = Button(Strings.Get("PublicMacroManager.New"), OnNew);
        var editButton = Button(Strings.Get("PublicMacroManager.Edit"), OnEdit);
        var deleteButton = Button(Strings.Get("PublicMacroManager.Delete"), OnDelete);
        var importButton = Button(Strings.Get("PublicMacroManager.ImportFile"), OnImportFile);
        var exportButton = Button(Strings.Get("PublicMacroManager.ExportFile"), OnExportFile);
        newButton.IsEnabled = !_locked;
        editButton.IsEnabled = !_locked;
        deleteButton.IsEnabled = !_locked;
        importButton.IsEnabled = !_locked;
        exportButton.IsEnabled = !_locked;
        var closeButton = Button(Strings.Get("Common.Close"), (_, _) => Close());
        _status = new TextBlock
        {
            Classes = { "Hint" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (loadError is not null)
        {
            _status.Text = loadError;
        }
        _list = new ListBox { ItemsSource = _rows, Classes = { "List" } };
        _list.DoubleTapped += async (_, _) => await EditSelected();
        _list.ItemTemplate = new FuncDataTemplate<Row>((row, _) =>
            new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock
                    {
                        Text = row.Title,
                        Classes = { "Body" },
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = row.Description,
                        Classes = { "Hint" },
                    },
                },
            });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { newButton, editButton, deleteButton, importButton, exportButton },
        };
        var heading = new TextBlock
        {
            Text = Strings.Get("PublicMacroManager.Title"),
            Classes = { "Title" },
        };
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { _status, closeButton },
        };
        Grid.SetColumn(closeButton, 1);

        Content = new Border
        {
            Padding = new Thickness(16),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                Children = { heading, buttons, _list, footer },
            },
        };
        Grid.SetRow(buttons, 1);
        Grid.SetRow(_list, 2);
        Grid.SetRow(footer, 3);
        RefreshRows();
    }

    public static async Task<IReadOnlyList<PublicMacroEntry>> ShowAsync(
        Window owner, IReadOnlyList<PublicMacroEntry> entries, string? loadError = null)
    {
        var window = new PublicMacroLibraryWindow(entries, loadError);
        await window.ShowDialogOver(owner);
        return window._entries;
    }

    private async void OnNew(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var editor = new MacroEditorWindow(null, [], AppPaths.PublicMacroLibrary,
            SharedDevices.Current, new ProjectTriggers(), callableOnly: true);
        if (await editor.ShowDialogOver<MacroItem?>(this) is not { } macro)
        {
            return;
        }

        macro.Name = UniqueName(macro.Name);
        var entry = new PublicMacroEntry(Guid.NewGuid().ToString("N"), 1, macro);
        _entries = [.. _entries, entry];
        Save();
    }

    private async void OnEdit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await EditSelected();

    private async Task EditSelected()
    {
        if (_list.SelectedItem is not Row row)
        {
            return;
        }

        var source = MacroItem.FromJson(row.Entry.Macro.ToSubMacroJson());
        var editor = new MacroEditorWindow(source, [], AppPaths.PublicMacroLibrary,
            SharedDevices.Current, new ProjectTriggers(), callableOnly: true);
        if (await editor.ShowDialogOver<MacroItem?>(this) is not { } edited)
        {
            return;
        }

        edited.Name = UniqueName(edited.Name, row.Entry.Id);
        var before = row.Entry.Macro.ToSubMacroJson().ToJsonString();
        var after = edited.ToSubMacroJson().ToJsonString();
        var version = string.Equals(before, after, StringComparison.Ordinal)
            ? row.Entry.Version
            : row.Entry.Version + 1;
        _entries = [.. _entries.Select(entry => entry.Id == row.Entry.Id
            ? new PublicMacroEntry(entry.Id, version, edited)
            : entry)];
        Save();
    }

    private async void OnDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_list.SelectedItem is not Row row)
        {
            return;
        }

        var choice = await ConfirmDialog.ShowAsync(this,
            Strings.Get("PublicMacroManager.DeleteTitle"),
            Strings.Format("PublicMacroManager.DeleteMessage", row.Entry.Macro.Name),
            Strings.Get("PublicMacroManager.Delete"),
            cancelLabel: Strings.Get("Common.Cancel"));
        if (choice != ConfirmChoice.Primary)
        {
            return;
        }

        _entries = [.. _entries.Where(entry => entry.Id != row.Entry.Id)];
        Save();
    }

    /// <summary>
    /// Takes macros from a file into the library. The same window as the project's import, with the
    /// destination decided by where it was opened from: from here, whatever is ticked becomes a
    /// library entry, named so the library stays valid.
    /// </summary>
    private async void OnImportFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = await PackagePicker.PickAsync(this, Strings.Get("PublicMacroManager.ImportTitle"));
        if (path is null)
        {
            return;
        }

        try
        {
            var contents = MacroPackage.Load(path,
                PublicMacroLibrary.AssetFolderFor(AppPaths.PublicMacroLibrary));
            var nodes = await MacroChooseWindow.ShowAsync(this, MacroChooseKind.Import,
                contents.Macros, contents.Triggers, contents.Globals,
                fileName: Path.GetFileName(path), intoLibrary: true);
            if (nodes is null || nodes.Count == 0)
            {
                return;
            }

            var added = MacroImport.IntoLibrary([.. nodes.Select(node => node.Macro)],
                AppPaths.PublicMacroLibrary);
            _entries = PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary);
            RefreshRows();
            _status.Text = Strings.Format("PublicMacroManager.Imported",
                string.Join("、", added.Select(entry => entry.Macro.Name)));
        }
        catch (Exception error)
        {
            _status.Text = Strings.Format("PublicMacroManager.SaveFailed", error.Message);
        }
    }

    /// <summary>
    /// Writes one entry out as a package holding that one macro, pictures and all, so it can be
    /// given to somebody who does not share this library. It leaves as a submacro: that is what a
    /// public macro is. Its triggers stay behind, because a library entry is called, not started.
    /// </summary>
    private async void OnExportFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_list.SelectedItem is not Row row)
        {
            _status.Text = Strings.Get("PublicMacroManager.PickFirst");
            return;
        }

        var path = await PackagePicker.AskWhereAsync(this,
            Strings.Get("PublicMacroManager.ExportTitle"), row.Entry.Macro.Name);
        if (path is null)
        {
            return;
        }

        try
        {
            var copy = MacroItem.FromJson(row.Entry.Macro.ToSubMacroJson());
            MacroPackage.Save(path, [copy], [], AppVersion.Text);
            _status.Text = Strings.Format("PublicMacroManager.Exported", Path.GetFileName(path));
        }
        catch (Exception error)
        {
            _status.Text = Strings.Format("PublicMacroManager.SaveFailed", error.Message);
        }
    }

    private void Save()
    {
        try
        {
            PublicMacroLibrary.Save(AppPaths.PublicMacroLibrary, _entries, AppVersion.Text);
            _status.Text = Strings.Get("PublicMacroManager.Saved");
            RefreshRows();
        }
        catch (Exception error)
        {
            _status.Text = Strings.Format("PublicMacroManager.SaveFailed", error.Message);
        }
    }

    private void RefreshRows()
    {
        var selected = (_list.SelectedItem as Row)?.Entry.Id;
        _rows.Clear();
        foreach (var entry in _entries.OrderBy(entry => entry.Macro.Name))
        {
            _rows.Add(new Row(entry));
        }

        _list.SelectedItem = _rows.FirstOrDefault(row => row.Entry.Id == selected);
        if (_list.SelectedItem is null && _rows.Count > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private string UniqueName(string requested, string? id = null)
    {
        var wanted = string.IsNullOrWhiteSpace(requested)
            ? Strings.Get("PublicMacroManager.DefaultName")
            : requested.Trim();
        var names = _entries
            .Where(entry => entry.Id != id)
            .Select(entry => entry.Macro.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Add(wanted))
        {
            return wanted;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{wanted} ({index})";
            if (names.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static Button Button(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click)
    {
        var button = new Button
        {
            Content = text,
            Height = 32,
            MinWidth = 84,
            Padding = new Thickness(11, 0),
        };
        button.Click += click;
        return button;
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
