using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class SettingsWindow : AppWindow
{
    private readonly SettingsViewModel _viewModel;
    private readonly FANavigationView _nav;
    private MenuRuleRow? _ruleDragSource;
    private Point _ruleDragOrigin;
    private bool _ruleDragging;
    private bool _wheelEditorReady;

    public SettingsWindow()
        : this(null, null, SettingsSubPage.General)
    {
    }

    public SettingsWindow(MainViewModel? project, string? packagePath,
        SettingsSubPage page = SettingsSubPage.General)
    {
        InitializeComponent();
        WindowChrome.Attach(this);

        _viewModel = new SettingsViewModel(SharedDevices.Current.Driver,
            project: project, packagePath: packagePath);
        DataContext = _viewModel;
        Title = Strings.Get("Settings.Title");

        var doneButton = this.FindControl<Button>("DoneButton");
        if (doneButton is not null)
        {
            doneButton.Click += (_, _) => Close();
        }

        this.FindControl<Button>("CaptureSystemHotkeyButton")!.Click += (_, _) =>
        {
            _viewModel.StopMenuLauncherCapture();
            _viewModel.IsCapturingSystemHotkey = !_viewModel.IsCapturingSystemHotkey;
        };
        this.FindControl<Button>("ClearSystemHotkeyButton")!.Click += (_, _) =>
            _viewModel.ClearSystemHotkey();
        this.FindControl<Button>("CaptureMenuLauncherButton")!.Click += (_, _) =>
        {
            _viewModel.IsCapturingSystemHotkey = false;
            _viewModel.BeginMenuLauncherCapture();
        };
        this.FindControl<Button>("ClearMenuLauncherButton")!.Click += (_, _) =>
            _viewModel.ClearMenuLauncher();
        this.FindControl<Button>("CaptureRecordStopButton")!.Click += (_, _) =>
        {
            _viewModel.IsCapturingSystemHotkey = false;
            _viewModel.StopMenuLauncherCapture();
            _viewModel.IsCapturingRecordStop = !_viewModel.IsCapturingRecordStop;
        };

        this.FindControl<Button>("EditMenuRuleButton")!.IsEnabled = project is not null;
        this.FindControl<Button>("RemoveMenuRuleButton")!.IsEnabled = project is not null;

        // The pages are one list, and the group named at the top of the window decides which of them
        // the list shows. All that is left here is opening on the page the caller asked for.
        _nav = this.FindControl<FANavigationView>("Nav")!;

        var ruleList = this.FindControl<ListBox>("MenuRuleList")!;
        var addPage = this.FindControl<Button>("AddMenuRulePageButton")!;
        addPage.IsEnabled = false;
        ruleList.SelectionChanged += (_, _) =>
            addPage.IsEnabled = ruleList.SelectedItem is MenuRuleRow { IsBlocked: false };
        ruleList.AddHandler(PointerPressedEvent, OnRulePointerPressed,
            RoutingStrategies.Tunnel);
        ruleList.AddHandler(PointerMovedEvent, OnRulePointerMoved,
            RoutingStrategies.Tunnel);
        ruleList.AddHandler(PointerReleasedEvent, OnRulePointerReleased,
            RoutingStrategies.Tunnel);

        _viewModel.SetPage(page);
        ShowMenuItemsOf(page);
        SelectPage(_nav, page);

        // A page can also change without the list being touched: the tabs at the top of the window,
        // or anything else that sets the page. Both paths end in the same two steps, so the list and
        // the page cannot end up naming different groups.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.SelectedPage))
            {
                ShowMenuItemsOf(_viewModel.SelectedPage);
                SelectPage(_nav, _viewModel.SelectedPage);
                EnsureWheelEditor();
            }
        };

        // The wheel's sectors are drawn rather than templated, so the action manager builds its
        // own controls and shares the painter with the live wheel. Building it only when its page
        // is first opened keeps every other settings page cheap to open.
        EnsureWheelEditor();

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnPreviewPointerWheel, RoutingStrategies.Tunnel);

        // Keys typed here are the interface's, not a macro's. The window is not modal any more, so
        // the hold lasts for as long as it is open — the same rule the editor and the run window
        // keep, and the reason the pages can capture a hotkey without setting one off.
        MacroTriggerGate.Enter();
        Closed += (_, _) => MacroTriggerGate.Exit();
    }

    private void EnsureWheelEditor()
    {
        if (_wheelEditorReady || _viewModel.SelectedPage != SettingsSubPage.WheelActions)
        {
            return;
        }

        if (this.FindControl<ContentControl>("WheelEditorHost") is { } host)
        {
            host.Content = new WheelMenuEditorView(_viewModel, this);
            _wheelEditorReady = true;
        }
    }

    private async void OnCustomizeThemeClicked(object? sender, RoutedEventArgs e)
        => await new ThemePaletteWindow().ShowDialogOver(this);

    /// <summary>Shows the page the panel's selection stands for.</summary>
    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (PageOf(e.SelectedItemContainer) is { } page)
        {
            _viewModel.SetPage(page);
        }
    }

    /// <summary>Opens the group one of the tabs at the top of the window stands for.</summary>
    private void OnRootTabClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<SettingsRoot>(tag, out var root))
        {
            _viewModel.SetRoot(root);
        }
    }

    /// <summary>
    /// Leaves only the pages of the open group in the list. The items are folded rather than taken
    /// out of <c>MenuItems</c>: a panel whose list is rebuilt keeps its selection on an item it no
    /// longer holds. Disabling them as well keeps Home, End and the arrow keys off a page that is
    /// not on screen.
    /// </summary>
    private void ShowMenuItemsOf(SettingsSubPage page)
    {
        var root = SettingsViewModel.RootOf(page);
        foreach (var item in _nav.MenuItems.OfType<FANavigationViewItem>())
        {
            var mine = PageOf(item) is { } tag && SettingsViewModel.RootOf(tag) == root;
            item.IsVisible = mine;
            item.IsEnabled = mine;
        }
    }

    /// <summary>
    /// Highlights the item that stands for a page. The item carries the page as its tag, so the
    /// mapping lives beside the item rather than in a second list here.
    /// </summary>
    private static void SelectPage(FANavigationView nav, SettingsSubPage page)
    {
        foreach (var item in nav.MenuItems.OfType<FANavigationViewItem>())
        {
            if (PageOf(item) == page)
            {
                // Writing the same item back raises no change, so the page and the list settle on
                // the first pass instead of passing the change back and forth.
                if (!ReferenceEquals(nav.SelectedItem, item))
                {
                    nav.SelectedItem = item;
                }

                return;
            }
        }
    }

    private static SettingsSubPage? PageOf(object? item)
        => item is FANavigationViewItem { Tag: string tag }
           && Enum.TryParse<SettingsSubPage>(tag, out var page)
            ? page
            : null;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel.IsCapturingWheelRepeat)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.IsCapturingWheelRepeat = false;
            }
            else
            {
                _viewModel.CaptureWheelRepeat(Chord(e.Key, e.KeyModifiers));
            }

            e.Handled = true;
            return;
        }

        if (_viewModel.IsCapturingSystemHotkey)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.IsCapturingSystemHotkey = false;
                e.Handled = true;
                return;
            }

            _viewModel.CaptureSystemHotkey(Chord(e.Key, e.KeyModifiers));
            e.Handled = true;
            return;
        }

        if (_viewModel.IsCapturingMenuLauncher)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.StopMenuLauncherCapture();
                e.Handled = true;
                return;
            }

            _viewModel.CaptureMenuLauncher(Chord(e.Key, e.KeyModifiers));
            e.Handled = true;
            return;
        }

        if (_viewModel.IsCapturingRecordStop)
        {
            if (HotkeyNameMapper.IsModifier(e.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.IsCapturingRecordStop = false;
                e.Handled = true;
                return;
            }

            _viewModel.CaptureRecordStop(Chord(e.Key, e.KeyModifiers));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (_viewModel.IsCapturingHotkey && HotkeyNameMapper.IsModifier(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_viewModel.IsCapturingMenuLauncher)
        {
            return;
        }

        // A press on the buttons of the hotkey row is the user cancelling or clearing, not a
        // mouse button to record. The handler runs on the way down, before the button sees the
        // click, so swallowing it here left the capture impossible to stop with the mouse.
        if (IsHotkeyButton(e.Source))
        {
            return;
        }

        var button = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => HookMouseButton.Left,
            PointerUpdateKind.RightButtonPressed => HookMouseButton.Right,
            PointerUpdateKind.MiddleButtonPressed => HookMouseButton.Middle,
            PointerUpdateKind.XButton1Pressed => HookMouseButton.Back,
            PointerUpdateKind.XButton2Pressed => HookMouseButton.Forward,
            _ => (HookMouseButton?)null,
        };
        if (button is not { } pressed)
        {
            return;
        }

        var binding = KeyNames.MouseName(pressed);
        _viewModel.CaptureMenuLauncher(binding);
        e.Handled = true;
    }

    /// <summary>Whether a press landed on one of the buttons that set, cancel or clear a hotkey.</summary>
    private static bool IsHotkeyButton(object? source)
    {
        var names = new[]
        {
            "CaptureSystemHotkeyButton", "ClearSystemHotkeyButton",
            "CaptureMenuLauncherButton", "ClearMenuLauncherButton",
            "CaptureRecordStopButton",
        };

        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            // Wheel buttons are looked up by their Click handler, so they carry a tag instead of
            // a name: a click on one of them cancels or clears, not a mouse button to record.
            if (visual is Control { Tag: string tag } && tag == "HotkeyButton")
            {
                return true;
            }

            if (visual is Button button && button.Name is { } name
                && names.Contains(name, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void OnPreviewPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!_viewModel.IsCapturingMenuLauncher)
        {
            return;
        }

        var (axis, rotation) = System.Math.Abs(e.Delta.X) > System.Math.Abs(e.Delta.Y)
            ? (HookWheelAxis.Horizontal, e.Delta.X < 0 ? 1 : -1)
            : (HookWheelAxis.Vertical, e.Delta.Y >= 0 ? 1 : -1);
        var binding = KeyNames.WheelName(axis, rotation);
        _viewModel.CaptureMenuLauncher(binding);
        e.Handled = true;
    }

    private void OnCaptureWheelRepeat(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsCapturingWheelRepeat)
        {
            _viewModel.IsCapturingWheelRepeat = false;
        }
        else
        {
            _viewModel.BeginWheelRepeatCapture();
        }
    }

    private void OnEnableSystem(object? sender, RoutedEventArgs e)
        => _viewModel.EnableSystem();

    /// <summary>
    /// Opens the wheel from the settings window so it can be seen and aimed at without any system
    /// input: preview mode answers the pointer inside Avalonia rather than through the global hook.
    /// </summary>
    private void OnPreviewWheel(object? sender, RoutedEventArgs e)
    {
        var origin = new PixelPoint(
            Position.X + (int)(Width / 2),
            Position.Y + (int)(Height / 2));
        var preview = new WheelMenuWindow(_viewModel.BuildWheelLayout(),
            _viewModel.WheelState.Options, origin, preview: true);
        preview.ShowOver(this);
    }

    private async Task AddMenuRuleAsync()
    {
        if (await WindowRuleWindow.PickAsync(this, SharedDevices.Current.Windows,
            SharedDevices.Current.Hook) is { } rule)
        {
            _viewModel.AddMenuRule(rule);
        }
    }

    private async Task EditMenuRuleAsync()
    {
        if (this.FindControl<ListBox>("MenuRuleList")?.SelectedItem is not MenuRuleRow row
            || row.IsDefault)
        {
            return;
        }

        if (await WindowRuleWindow.PickAsync(this, SharedDevices.Current.Windows,
            SharedDevices.Current.Hook, row.Rule) is { } rule)
        {
            _viewModel.UpdateMenuRule(row, rule);
        }
    }

    private void OnAddMenuRule(object? sender, RoutedEventArgs e) => _ = AddMenuRuleAsync();

    private void OnEditMenuRule(object? sender, RoutedEventArgs e) => _ = EditMenuRuleAsync();

    private void OnRemoveMenuRule(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ListBox>("MenuRuleList")?.SelectedItem is MenuRuleRow row)
        {
            _viewModel.RemoveMenuRule(row);
        }
    }

    private void OnAddMenuRulePage(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<ListBox>("MenuRuleList")?.SelectedItem is MenuRuleRow row)
        {
            _viewModel.AddMenuPage(row);
        }
    }

    private void OnMoveMenuRuleUp(object? sender, RoutedEventArgs e)
        => MoveMenuRule(-1);

    private void OnMoveMenuRuleDown(object? sender, RoutedEventArgs e)
        => MoveMenuRule(1);

    private void MoveMenuRule(int step)
    {
        if (this.FindControl<ListBox>("MenuRuleList")?.SelectedItem is MenuRuleRow row)
        {
            _viewModel.MoveMenuRule(row, step);
        }
    }

    private void OnRulePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list
            || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _ruleDragSource = RowAt(e.Source as Visual);
        _ruleDragOrigin = e.GetPosition(list);
        _ruleDragging = false;
    }

    private void OnRulePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_ruleDragSource is null || sender is not ListBox list
            || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(list);
        _ruleDragging = Math.Abs(point.X - _ruleDragOrigin.X) > 5
                        || Math.Abs(point.Y - _ruleDragOrigin.Y) > 5;
    }

    private void OnRulePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ListBox list || !_ruleDragging || _ruleDragSource is not { } source)
        {
            _ruleDragSource = null;
            _ruleDragging = false;
            return;
        }

        var target = RowAt(list.InputHitTest(e.GetPosition(list)) as Visual);
        _ruleDragSource = null;
        _ruleDragging = false;
        if (target is not null)
        {
            _viewModel.MoveMenuRuleTo(source, target);
        }
    }

    private static MenuRuleRow? RowAt(Visual? visual)
    {
        var item = visual?.FindAncestorOfType<ListBoxItem>(true);
        return item?.DataContext as MenuRuleRow;
    }

    private static string Chord(Key key, KeyModifiers held)
        => HotkeyNameMapper.Chord(key, held);

    /// <summary>
    /// Opens the folder one of those rows names, making it first: the folders for a project's
    /// pictures only exist once something has been saved, and a button that fails on a fresh
    /// install teaches the wrong thing about where things go.
    /// </summary>
    private void OnOpenStorage(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string folder } || folder.Length == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception error)
        {
            // 界面只说一句本地化的话；打不开的理由（只读目录、被策略拦住）写进日志，
            // 把一串 .NET 文本贴到窗口里对谁都不算说明。
            AppDiagnostics.Record("settings.storage.open", error);
            if (this.FindControl<TextBlock>("StorageProblem") is { } problem)
            {
                problem.IsVisible = true;
            }
        }
    }

    private void OnInstallDriverClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.DriverDownload);

    private void OnRestartElevatedClicked(object? sender, RoutedEventArgs e)
    {
        SharedDevices.Current.Driver.HandOver();
        if (!ProcessRights.RestartElevated())
        {
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }


}
