using System;
using CommunityToolkit.Mvvm.Input;

namespace Viktor.ViewModels;

/// <summary>
/// Outcome of a confirmation dialog. Cancel comes first so that closing the window
/// without an answer falls back to the safe choice.
/// </summary>
public enum ConfirmChoice
{
    Cancel,
    Primary,
    Secondary,
}

/// <summary>Backs the small modal used for "are you sure" style questions.</summary>
public partial class ConfirmDialogViewModel : ViewModelBase
{
    public event Action<ConfirmChoice>? CloseRequested;

    public string Header { get; init; } = "Confirm";

    public string Message { get; init; } = string.Empty;

    public string PrimaryLabel { get; init; } = "OK";

    public string? SecondaryLabel { get; init; }

    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryLabel);

    /// <summary>False for messages that only need an acknowledgement button.</summary>
    public bool ShowCancel { get; init; } = true;

    [RelayCommand]
    private void Primary() => CloseRequested?.Invoke(ConfirmChoice.Primary);

    [RelayCommand]
    private void Secondary() => CloseRequested?.Invoke(ConfirmChoice.Secondary);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(ConfirmChoice.Cancel);
}
