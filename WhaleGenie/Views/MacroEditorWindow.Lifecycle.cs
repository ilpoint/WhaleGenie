using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

public partial class MacroEditorWindow : AppWindow
{
    // What the editor leaves behind and what it asks on the way out: the result and lifted macros,
    // the recovery snapshot, the title-bar state, and closing with unsaved work.
    /// <summary>
    /// Marks the macro the editor was opened on as not saved. Work recovered from a run that
    /// stopped without notice is unsaved by definition, so closing the editor has to ask about it
    /// rather than let it go without a word.
    /// </summary>
    internal void MarkUnsaved()
    {
        _mustStayUnsaved = true;
        _viewModel.IsDirty = true;
    }

    /// <summary>
    /// The macro the editor finished with, or null when it was let go. The editor is not a dialog —
    /// the main list steps aside while it is up rather than waiting on it — so what it ends with is
    /// read from here as it closes.
    /// </summary>
    internal MacroItem? Result { get; private set; }

    /// <summary>
    /// Hands the macro and its child pages to the project. Answers whether the project took it:
    /// false means the macro this editor was opened on is no longer in the list, and the work stays
    /// here rather than being written over a version somebody else made.
    /// </summary>
    internal Func<MacroItem, bool>? Commit { get; set; }

    /// <summary>
    /// Macros this session made out of stretches of the one being edited, minus the ones the macro
    /// no longer calls — a lift that was undone or whose call was written over. What is left is part
    /// of what the session produced, so the project takes it along with the edited macro.
    /// </summary>
    internal IReadOnlyList<MacroItem> LiftedMacros
        => [.. _session.Lifted.Where(macro => _viewModel.Steps.Any(step => Calls(step, macro.Name)))];

    /// <summary>True when this step, or any step inside it, calls the named macro.</summary>
    private static bool Calls(MacroStep step, string name)
        => step.Type == "control.runMacro" && string.Equals(step.Text("macro").Trim(), name,
            StringComparison.OrdinalIgnoreCase)
           || step.StepLists.Any(list => list.Steps.Any(child => Calls(child, name)));

    /// <summary>
    /// True when the main list went into the notification area to make room for this editor, which
    /// is what says whether it comes back when the editor is done.
    /// </summary>
    internal bool SteppedAside { get; set; }

    /// <summary>
    /// Keeps the macro being written in the recovery snapshot while it has unsaved changes, and
    /// lets it go once it does not — it was saved into the list, or the user did not want it.
    /// </summary>
    private void SyncRecovery()
    {
        if (!IsAnyDirty)
        {
            StopRecovery();
            return;
        }

        // The first change is written straight away, so a macro that is pasted in and then lost
        // to a crash seconds later is not waiting on the clock to be kept.
        _editorInRecovery = true;
        _recovery.Now();
    }

    /// <summary>
    /// Keeps a change that arrived while the macro was already unsaved from going unwritten. The
    /// write waits for the changes to pause; what first made the macro unsaved was written already.
    /// </summary>
    private void KeepRecovery()
    {
        if (!IsAnyDirty)
        {
            return;
        }

        _editorInRecovery = true;
        _recovery.Changed();
    }

    private void WriteRecovery()
    {
        if (!IsAnyDirty)
        {
            return;
        }

        RecoveryStore.SaveEditor(BuildRootSnapshot(), _session.Editing?.Name ?? string.Empty);
        _editorInRecovery = true;
    }

    private bool IsAnyDirty => _rootViewModel.IsDirty
        || _documentPages.Any(page => page.Editor.IsDirty);

    private MacroItem BuildRootSnapshot()
    {
        _rootViewModel.SubMacros.Clear();
        foreach (var page in _documentPages.Where(page => !page.IsCurrent))
        {
            _rootViewModel.SubMacros.Add(page.Editor.BuildMacro());
        }

        return _rootViewModel.BuildMacro();
    }

    private void StopRecovery()
    {
        _recovery.Stop();

        // Only take back what this editor wrote: a draft left by a run that stopped is not this
        // window's to drop, since nobody has answered for it yet.
        if (_editorInRecovery)
        {
            _editorInRecovery = false;
            RecoveryStore.ClearEditor();
        }
    }

    /// <summary>Which windows are framed right now, so that a check can count them.</summary>
    internal IReadOnlyCollection<long> FramedWindows => _triggerFrames.Framed;

    /// <summary>
    /// Puts a frame around each window the trigger's rule recognises, and takes down the ones that
    /// have dropped out of it. Nothing is framed while the editor is out of the way — a minimised
    /// editor with frames left standing would be saying something nobody is reading — and the frames
    /// go with the trigger pane, which is only on screen while the macro is started by a window.
    /// This program's own windows are left out, the editor included: the window a rule is written in
    /// is one the rule really can fire on, so the list says so, but a ring drawn at its own edge
    /// reads as the border of the window rather than as an answer.
    /// </summary>
    private void DrawTriggerFrames()
    {
        IReadOnlyList<FramePlace> places = _closing || !IsVisible || WindowState == WindowState.Minimized
            ? []
            : _viewModel.WindowPreview.Places;

        _triggerFrames.Show(AppWindows.Outside(places));
    }

    private void UpdateTitleBarButtons()
    {
        if (this.FindControl<Button>("PinButton") is { } pin)
        {
            pin.Classes.Set("pinned", Topmost);
            ToolTip.SetTip(pin, Strings.Get(Topmost ? "Common.Unpin" : "Common.Pin"));
        }

        if (this.FindControl<IconView>("PinIcon") is { } pinIcon)
        {
            pinIcon.Data = Topmost ? AppIcons.PinOff : AppIcons.Pin;
        }

        var maximized = WindowState == WindowState.Maximized;

        if (this.FindControl<IconView>("MaximizeIcon") is { } maximizeIcon)
        {
            maximizeIcon.Data = maximized ? AppIcons.Restore : AppIcons.Maximize;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateTitleBarButtons();
        }

        if (change.Property == WindowStateProperty || change.Property == IsVisibleProperty)
        {
            DrawTriggerFrames();
        }
    }

    /// <summary>Prompts for unsaved work before the editor is dismissed.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowClose || !IsAnyDirty)
        {
            _closing = true;
        }
        else
        {
            // The answer is given before the base call, because the base call is what raises the
            // Closing event and the way out reads the answer there. A handler that ran first and
            // found nothing would take this window for one that had already gone, and would stop
            // the program on top of the very question it is asking.
            //
            // The question itself is queued rather than asked here. A modal opened from inside a
            // closing handler is asked for before the window has finished saying no, and whether
            // it shows up at all turns out to depend on how the dialog presents itself.
            e.Cancel = true;
            Dispatcher.UIThread.Post(() => _ = PromptToSaveAsync());
        }

        base.OnClosing(e);
    }

    private async Task PromptToSaveAsync()
    {
        if (_prompting)
        {
            return;
        }

        _prompting = true;
        try
        {
            var choice = await ConfirmDialog.ShowAsync(this,
                Strings.Get("Editor.UnsavedTitle"),
                Strings.Get("Editor.UnsavedMessage"),
                Strings.Get("Editor.Save"),
                Strings.Get("Editor.Discard"));

            switch (choice)
            {
                case ConfirmChoice.Primary:
                    SaveAll(closeAfterSave: true);
                    break;
                case ConfirmChoice.Secondary:
                    _allowClose = true;
                    Close(null);
                    break;
                default:
                    // "Not now" is an answer as well, and it belongs to the exit the tray may have
                    // started: without saying so, that exit would still be waiting on this window
                    // and would stop the program the next time it closed for a reason of its own.
                    AppTray.Current?.CalledOff();
                    break;
            }
        }
        finally
        {
            _prompting = false;
        }
    }
}
