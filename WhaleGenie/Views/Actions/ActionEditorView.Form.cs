using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

// What the form shows as it is filled in: the preview frames, the window read, and the step it becomes.
public partial class ActionEditorView : UserControl
{
    /// <summary>The form has moved on to another action, which comes with a list of its own.</summary>
    private void OnFormChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddActionViewModel.WindowPreview))
        {
            WatchPreview();
        }
    }

    /// <summary>
    /// Watches whatever list the form has now and draws the frames it asks for. The list is a new one
    /// for every action, so the one that just went away has to stop being watched rather than going
    /// on moving frames for a step that is no longer being written.
    /// </summary>
    internal void WatchPreview()
    {
        if (_watched is { } stale)
        {
            stale.PropertyChanged -= OnPreviewChanged;
        }

        _watched = Form?.WindowPreview;
        if (_watched is { } fresh)
        {
            fresh.PropertyChanged += OnPreviewChanged;
        }

        DrawPreviewFrames();
    }

    private void OnPreviewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowPreviewViewModel.Places))
        {
            DrawPreviewFrames();
        }
    }

    /// <summary>
    /// Puts a frame around each window the list is showing, and takes down the ones that are no
    /// longer in it. Nothing is framed while the dialog is out of the way: the frames say what the
    /// list says, and there is no list to read then. This program's own windows are left out — the
    /// dialog asking included — because a ring at the edge of the window a step is being written in
    /// says nothing and reads as that window's own border.
    /// </summary>
    private void DrawPreviewFrames()
    {
        IReadOnlyList<FramePlace> places = _closed || !IsVisible || HostState == WindowState.Minimized
            ? []
            : Form?.WindowPreview?.Places ?? [];

        _previewFrames.Show(AppWindows.Outside(places));
    }

    /// <summary>
    /// Puts a frame around the window a trial is reading. It is the other half of "did that look at
    /// the right thing": the trial shows what turned up, and this shows where the engine looked.
    /// </summary>
    private void ShowReadWindow(WindowInfo? read)
    {
        if (read is null || FramePlace.Of(read) is not { } place)
        {
            _trialFrames.Clear();
            return;
        }

        _trialFrames.Show([place]);
    }

    /// <summary>
    /// What a trial hands over: it wants to be told which window is being read, and that comes from
    /// the thread the trial is on, so the frame is drawn on the thread that owns windows. Waiting for
    /// that rather than posting it keeps the frame off the screen once the trial is over.
    /// </summary>
    private sealed class TrialFrames(ActionEditorView window) : IRunLooks
    {
        /// <summary>The last thing the step saw, which is the one worth showing once it is done.</summary>
        public StepLook? Seen { get; private set; }

        public void Look(StepLook look) => Seen = look;

        /// <summary>What a trial came to is on the line under it; there is nothing to keep here.</summary>
        public void Ran(StepRun run)
        {
        }

        public void Reading(WindowInfo? read)
            => Dispatcher.UIThread.Invoke(() => window.ShowReadWindow(read));
    }

    /// <summary>
    /// The window this one was opened from, as a rectangle in the same units as the desktop it is
    /// measured against. That window is the macro editor: it is what the form stands beside.
    /// </summary>
    internal static (double X, double Y, double Width, double Height) EditorRect(Window owner)
    {
        var scale = owner.RenderScaling > 0 ? owner.RenderScaling : 1;
        return (owner.Position.X / scale, owner.Position.Y / scale, owner.Bounds.Width, owner.Bounds.Height);
    }

    /// <summary>
    /// The form was finished with: it either holds a step to put somewhere, or nothing when the
    /// user cancelled. The window answers with it rather than staying up for the next one: the
    /// editor places the step and opens the window again when another one is asked for.
    /// </summary>
    private void OnFormFinished(MacroStep? step)
    {
        if (step is not null && PlaceStep is { } place && place(_editingStep, step) is { } refusal)
        {
            // The place picked in the list does not take this kind of step, so nothing was added.
            // Keep the form and its values on screen with the reason: closing it would make a
            // refused insertion look as if the step had simply vanished.
            Form?.Say(refusal, warning: true);
            return;
        }

        Finished?.Invoke(step);
    }

    /// <summary>Points the form at a step that is already in the list, so it can be changed.</summary>
    internal void EditStep(MacroStep step)
    {
        _editingStep = step;
        ResetKeyPad();
        Form?.LoadFrom(step);
    }

    /// <summary>
    /// The form has moved on to another step, so the keyboard is no longer writing into anything
    /// that is on screen: the fields it was pointed at, or was waiting for a key to, are gone with
    /// the form it belonged to.
    /// </summary>
    private void ResetKeyPad()
    {
        EndKeyCapture();
        _keyTarget = null;
        _keyPad?.Close();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // A field waiting for a key owns every key of the form, Escape and Alt+X included: those
        // are keys a step may send, and the key is the answer rather than a shortcut.
        if (IsCapturingKey)
        {
            return;
        }

        // Alt + X takes the pointer's place, for the actions that aim at a screen position.
        if (e.Key == Key.X && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
            CaptureCursorPosition();
            return;
        }

        if (e.Key is not Key.Escape || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        viewModel.CancelCommand.Execute(null);
        e.Handled = true;
    }

    private void CaptureCursorPosition()
    {
        if (DataContext is AddActionViewModel viewModel)
        {
            var point = Devices.Screen.Cursor;
            viewModel.ApplyCursorPosition(point.X, point.Y);
        }
    }
}
