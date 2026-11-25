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
using WhaleGenie.Storage;

namespace WhaleGenie.Views;

/// <summary>
/// A package picker drawn by the program itself rather than by the Windows shell. The shell's own
/// open dialog loads the context-menu handlers registered for the file type into this process, and
/// one of them crashing takes the editor down with it; a list owned by the app has no such
/// extension. It only ever offers folders and <c>.wgmacro</c> files, which is all this picker is for.
/// </summary>
internal sealed class PackagePickerWindow : AppWindow
{
    private readonly ObservableCollection<PackageEntry> _entries = [];
    private readonly TextBox _path;
    private readonly ListBox _list;
    private readonly Button _open;
    private string _folder = string.Empty;

    private PackagePickerWindow(string title, string startFolder)
    {
        Title = title;
        Width = 680;
        Height = 480;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        _path = new TextBox
        {
            IsReadOnly = true,
            Margin = new Thickness(8),
        };
        _list = new ListBox
        {
            ItemsSource = _entries,
            Margin = new Thickness(8, 0, 8, 8),
            ItemTemplate = new FuncDataTemplate<PackageEntry>((entry, _) => new TextBlock
            {
                Text = entry is { IsFolder: true }
                    ? entry.Name + Path.DirectorySeparatorChar
                    : entry.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }),
        };
        _list.DoubleTapped += (_, _) => ActivateSelected();
        _list.SelectionChanged += (_, _) =>
            _open!.IsEnabled = _list.SelectedItem is PackageEntry { IsFolder: false };
        _list.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                ActivateSelected();
                args.Handled = true;
            }
        };

        _open = new Button { Content = Strings.Get("Common.Open") };
        _open.Click += (_, _) => OpenSelected();
        var up = new Button { Content = Strings.Get("Common.Up") };
        up.Click += (_, _) => Navigate(Path.GetDirectoryName(_folder));
        var cancel = new Button { Content = Strings.Get("Common.Cancel") };
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8),
        };
        buttons.Children.Add(up);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_open);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        Grid.SetRow(_path, 0);
        Grid.SetRow(_list, 1);
        Grid.SetRow(buttons, 2);
        root.Children.Add(_path);
        root.Children.Add(_list);
        root.Children.Add(buttons);
        Content = root;

        Navigate(startFolder);
        _list.Focus();
    }

    public static async Task<string?> PickAsync(Window owner, string title, string? startFolder)
    {
        var folder = startFolder is { Length: > 0 } && Directory.Exists(startFolder)
            ? startFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dialog = new PackagePickerWindow(title, folder);
        return await dialog.ShowDialogOver<string?>(owner);
    }

    private void Navigate(string? folder)
    {
        if (folder is not { Length: > 0 })
        {
            return;
        }

        try
        {
            var directory = new DirectoryInfo(Path.GetFullPath(folder));
            if (!directory.Exists)
            {
                return;
            }

            _folder = directory.FullName;
            _path.Text = _folder;
            _entries.Clear();
            foreach (var child in directory.EnumerateDirectories()
                .Where(child => (child.Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase))
            {
                _entries.Add(new PackageEntry(child.FullName, child.Name, true));
            }

            foreach (var file in directory.EnumerateFiles($"*{MacroPackage.Extension}")
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
            {
                _entries.Add(new PackageEntry(file.FullName, file.Name, false));
            }

            _list.SelectedIndex = -1;
            _open.IsEnabled = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _path.Text = error.Message;
        }
    }

    private void ActivateSelected()
    {
        if (_list.SelectedItem is PackageEntry entry)
        {
            if (entry.IsFolder)
            {
                Navigate(entry.Path);
            }
            else
            {
                Close(entry.Path);
            }
        }
    }

    private void OpenSelected()
    {
        if (_list.SelectedItem is PackageEntry { IsFolder: false } entry)
        {
            Close(entry.Path);
        }
    }

    private sealed record PackageEntry(string Path, string Name, bool IsFolder);
}
