using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        Background = Brush("AppBackdrop", Color.FromRgb(32, 32, 32));

        var newButton = Button(Strings.Get("PublicMacroManager.New"), OnNew);
        var editButton = Button(Strings.Get("PublicMacroManager.Edit"), OnEdit);
        var deleteButton = Button(Strings.Get("PublicMacroManager.Delete"), OnDelete);
        newButton.IsEnabled = !_locked;
        editButton.IsEnabled = !_locked;
        deleteButton.IsEnabled = !_locked;
        var closeButton = Button(Strings.Get("Common.Close"), (_, _) => Close());
        _status = new TextBlock
        {
            Foreground = Brush("AppMuted", Color.FromRgb(160, 160, 160)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (loadError is not null)
        {
            _status.Text = loadError;
        }
        _list = new ListBox { ItemsSource = _rows, Margin = new Thickness(0, 10, 0, 10) };
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
                        FontSize = 13,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brush("AppTextStrong", Colors.White),
                    },
                    new TextBlock
                    {
                        Text = row.Description,
                        FontSize = 11.5,
                        Foreground = Brush("AppMuted", Colors.Gray),
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { newButton, editButton, deleteButton },
        };
        var heading = new TextBlock
        {
            Text = Strings.Get("PublicMacroManager.Title"),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("AppTextStrong", Colors.White),
            Margin = new Thickness(0, 0, 0, 10),
        };
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { _status, closeButton },
        };
        Grid.SetColumn(closeButton, 1);

        Content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Children = { heading, buttons, _list, footer },
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
