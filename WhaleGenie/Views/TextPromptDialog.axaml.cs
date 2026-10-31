using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.Core.Execution;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class TextPromptDialog : AppWindow
{
    private readonly TextPromptViewModel _viewModel;

    public TextPromptDialog()
        : this(new TextPromptViewModel())
    {
    }

    public TextPromptDialog(TextPromptViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        Title = viewModel.Header;
        viewModel.CloseRequested += answer => Close(answer);

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Shows the dialog over <paramref name="owner"/> and reports the answer. Leaving the box alone
    /// is an answer of its own: what it was prefilled with is what the macro reads.
    /// </summary>
    public static async Task<AskAnswer> ShowAsync(Window owner, string header, AskQuestion question)
    {
        var dialog = new TextPromptDialog(new TextPromptViewModel
        {
            Header = header,
            Question = question.Message,
            Answer = question.Default,
        });

        var answer = await dialog.ShowDialogOver<string?>(owner);
        return answer is null ? AskAnswer.Cancel : new AskAnswer(true, answer);
    }

    /// <summary>
    /// Escape gives up on the question, and Enter takes the answer: a question the macro is waiting
    /// on is not a form, so there is nothing else in it for the keyboard to move between.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _viewModel.CancelCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                _viewModel.OkCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        this.FindControl<TextBox>("AnswerBox")?.Focus();
    }


}
