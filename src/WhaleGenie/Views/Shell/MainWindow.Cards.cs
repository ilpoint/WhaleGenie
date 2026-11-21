using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

// A macro card: its menu, its press, and switching the macro on or off.
public partial class MainWindow : AppWindow
{
    /// <summary>
    /// A card's right-click menu carries no data of its own: its four items are written out in XAML
    /// and used to inherit the card's macro because the menu sat in the card's logical tree. Under a
    /// theme that presents the menu inside a popup, that tree stops at the menu, every item arrives
    /// with a null DataContext, and the four handlers read that as "no macro" and quietly do nothing.
    /// Stamping it here keeps their one rule — the sender's DataContext is the macro — true.
    /// </summary>
    internal static void StampMenu(ContextMenu menu, object? data)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.DataContext = data;
        }
    }

    private void OnCardMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            StampMenu(menu, menu.DataContext);
        }
    }

    private void OnEditMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: MacroItem macro })
        {
            OpenEditor(macro, macro, draft: false);
        }
    }

    /// <summary>
    /// Debugs a macro, which is what the card's own button and the menu a right click opens both
    /// ask for. The window keeps the whole list, which is what its variables half lists.
    /// </summary>
    private async void OnDebugMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: MacroItem macro })
        {
            await ShowRunDebugAsync(macro);
        }
    }

    private async void OnDeleteMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not Control { DataContext: MacroItem macro }
            || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Editor.DeleteTitle"),
            Strings.Format("Editor.DeleteMessage", macro.Name), Strings.Get("Editor.Delete"));

        if (choice == ConfirmChoice.Primary)
        {
            _triggers?.Stop(macro.Name);
            viewModel.RemoveMacro(macro);
            RefreshRunning(viewModel);
        }
    }

    /// <summary>Clicking anywhere on a macro card toggles whether that macro is armed.</summary>
    private void OnMacroCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is Visual source && source.FindAncestorOfType<Button>(true) is not null)
        {
            return;   // the card's own Edit and Delete buttons
        }

        if (e.Source is Visual clicked
            && clicked.FindAncestorOfType<Border>(true) is { DataContext: MacroItem macro }
            && DataContext is MainViewModel viewModel)
        {
            ToggleMacro(macro, viewModel);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Arms or disarms a macro, which the card answers to with a left click and its menu with
    /// "Enable" / "Disable". Both come through here: what a switch does must not depend on which of
    /// the two ways somebody reached it.
    /// </summary>
    private void ToggleMacro(MacroItem macro, MainViewModel viewModel)
    {
        viewModel.ToggleMacro(macro);

        // Arming a macro again gives a "trigger once" macro a fresh chance to run.
        if (macro.IsEnabled)
        {
            _triggers?.Rearm(macro.Name);
        }
        else
        {
            _triggers?.Stop(macro.Name);
        }

        RefreshRunning(viewModel);

        // 圆点就在眼前，声音不是用来看状态的：它说的是"这一下点到了"，两者成对才好分辨。
        AppSound.Play(macro.IsEnabled ? UiSound.MacroOn : UiSound.MacroOff);
    }

    /// <summary>The same switch, chosen from the card's right-click menu.</summary>
    private void OnToggleMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: MacroItem macro }
            && DataContext is MainViewModel viewModel)
        {
            ToggleMacro(macro, viewModel);
        }
    }
}
