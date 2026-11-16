using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Backs the small modal that asks for a line of writing. It is the shape a macro's own question
/// takes when no run window is open to show it in: a triggered run, or a question asked while the
/// editor is the window on screen.
/// </summary>
public partial class TextPromptViewModel
{
    /// <summary>Raised with the answer: the writing, and whether the person confirmed it.</summary>
    public event System.Action<string?>? CloseRequested;

    public string Header { get; init; } = "WhaleGenie";

    public string Question { get; init; } = string.Empty;

    /// <summary>What the answer box holds before the person types anything.</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// How tall the window is. A question fits in the default; a long one that was written across
    /// several lines needs a taller window to be read without scrolling.
    /// </summary>
    public double WindowHeight { get; init; } = 230;

    public string OkText => Strings.Get("Run.AnswerOk");

    public string CancelText => Strings.Get("Run.AnswerCancel");

    [RelayCommand]
    private void Ok() => CloseRequested?.Invoke(Answer);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);
}
