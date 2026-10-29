using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>The four-source action library beside the step list.</summary>
public partial class ActionPaletteView : UserControl
{
    private PointerPressedEventArgs? _press;
    private ActionPaletteCardViewModel? _card;
    private Point _origin;
    private bool _dragged;

    public ActionPaletteView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnCardPressed, RoutingStrategies.Tunnel, true);
        AddHandler(PointerMovedEvent, OnCardMoved, RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, OnCardReleased, RoutingStrategies.Tunnel, true);
    }

    internal event Action<MacroItemReference>? SubMacroChosen;
    internal event Action? AddBlankSubMacroRequested;
    internal event Action? CopyProjectMacroRequested;
    internal event Action? ManagePublicMacrosRequested;

    internal bool HasArmedDrag => _press is not null && _card is not null;

    internal void FocusSearch()
    {
        var search = this.FindControl<TextBox>("PaletteSearch");
        search?.Focus();
        search?.SelectAll();
    }

    private void OnPickCategory(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ActionCategoryChoice choice }
            && DataContext is ActionPaletteViewModel viewModel)
        {
            viewModel.SelectedCategory = choice;
        }
    }

    private void OnCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || e.Source is not Visual source
            || source.FindAncestorOfType<Button>(true) is not
            { DataContext: ActionPaletteCardViewModel card })
        {
            return;
        }

        _press = e;
        _card = card;
        _origin = e.GetPosition(this);
        _dragged = false;
    }

    private async void OnCardMoved(object? sender, PointerEventArgs e)
    {
        if (_press is null || _card is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClearPress();
            return;
        }

        var now = e.GetPosition(this);
        if (Math.Abs(now.X - _origin.X) < 4 && Math.Abs(now.Y - _origin.Y) < 4)
        {
            return;
        }

        _dragged = true;
        var payload = _card.Kind switch
        {
            ActionPaletteCardKind.Action =>
                new ActionDragPayload(ActionPaletteCardKind.Action, _card.Key),
            ActionPaletteCardKind.SubMacro =>
                new ActionDragPayload(ActionPaletteCardKind.SubMacro, _card.Key),
            ActionPaletteCardKind.PublicMacro when _card.PublicMacro is { } entry =>
                new ActionDragPayload(ActionPaletteCardKind.PublicMacro, entry.Id, entry.Version),
            _ => null,
        };
        var press = _press;
        ClearPress();

        if (payload is not null)
        {
            await DragDrop.DoDragDropAsync(press, payload.Transfer(), DragDropEffects.Copy);
        }
    }

    private void OnCardReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragged && _card is { Kind: ActionPaletteCardKind.SubMacro, Macro: { } macro })
        {
            SubMacroChosen?.Invoke(new MacroItemReference(macro));
        }

        ClearPress();
    }

    private void OnAddBlankSubMacro(object? sender, RoutedEventArgs e)
        => AddBlankSubMacroRequested?.Invoke();

    private void OnCopyProjectMacro(object? sender, RoutedEventArgs e)
        => CopyProjectMacroRequested?.Invoke();

    private void OnManagePublicMacros(object? sender, RoutedEventArgs e)
        => ManagePublicMacrosRequested?.Invoke();

    private void ClearPress()
    {
        _press = null;
        _card = null;
        _dragged = false;
    }
}

internal sealed record MacroItemReference(Models.MacroItem Macro);
