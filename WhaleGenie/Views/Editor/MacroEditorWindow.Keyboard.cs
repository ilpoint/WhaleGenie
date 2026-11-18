using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MacroEditorWindow : AppWindow
{
    /// <summary>Takes one notch of the wheel as the trigger while the editor is waiting for one.</summary>
    private void OnPreviewPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_capture.TakeWheel(e))
        {
            e.Handled = true;
        }
    }

    /// <summary>Takes a mouse button as the trigger while the editor is waiting for one.</summary>
    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_capture.TakePointer(e))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (_capture.TakeKeyUp(e))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // A capture, or the end of a typed-string trigger, comes before every shortcut: while one is
        // up the keys are the binding being taken, not commands.
        if (_capture.TakeKeyDown(e))
        {
            e.Handled = true;
            return;
        }

        if (DataContext is not MacroEditorViewModel viewModel)
        {
            return;
        }

        // The screen pickers are reachable from anywhere in the editor, including while a
        // coordinate or the hex colour is being typed, so they answer before the text rule below.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            switch (e.Key)
            {
                case Key.X:
                    e.Handled = true;
                    _pickers.TakeCursorPosition(viewModel);
                    return;
                case Key.C:
                    e.Handled = true;
                    _pickers.TakeCursorColour(viewModel);
                    return;
            }
        }

        // Never take a shortcut while the user is typing into a field.
        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>(true) is not null)
        {
            return;
        }

        // 设置里写的是"开始/停止"两半：结束那一次由录制器自己接住（它按全局钩子听），开始这一次
        // 只有窗口知道，所以在这里接。
        if (!viewModel.IsRecording
            && HotkeyNameMapper.Chord(e.Key, e.KeyModifiers) is { Length: > 0 } pressed
            && string.Equals(pressed, LocalSettings.LoadRecordStopKey(),
                StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            viewModel.ToggleRecordCommand.Execute(null);
            return;
        }

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (control)
        {
            switch (e.Key)
            {
                case Key.A:
                    e.Handled = true;
                    SelectAllSteps();
                    return;
                case Key.C:
                    e.Handled = true;
                    _ = CopySelectionAsync();
                    return;
                case Key.X:
                    e.Handled = true;
                    _ = CutSelectionAsync();
                    return;
                case Key.V:
                    e.Handled = true;
                    _ = PasteAsync();
                    return;
                case Key.Z:
                    e.Handled = true;
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        viewModel.RedoCommand.Execute(null);
                    }
                    else
                    {
                        viewModel.UndoCommand.Execute(null);
                    }

                    return;
                case Key.Y:
                    e.Handled = true;
                    viewModel.RedoCommand.Execute(null);
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Insert:
                viewModel.NewStepCommand.Execute(null);
                break;
            case Key.F2:
                viewModel.EditSelectedCommand.Execute(null);
                break;
            case Key.Delete when !control:
                viewModel.DeleteSelectedCommand.Execute(null);
                break;
            case Key.G when control:
                viewModel.GroupSelectedCommand.Execute(null);
                break;
            case Key.E when control:
                viewModel.ToggleStepEnabledCommand.Execute(null);
                break;
            case Key.Up when control:
                viewModel.MoveSelectedUpCommand.Execute(null);
                break;
            case Key.Down when control:
                viewModel.MoveSelectedDownCommand.Execute(null);
                break;
            case Key.Right when control:
                Fold(true);
                break;
            case Key.Left when control:
                Fold(false);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
