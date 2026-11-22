namespace WhaleGenie.Contracts;

/// <summary>
/// The window buttons the page draws. The page owns the pixels; the UI project owns the window and
/// implements these operations, while the handler only names them.
/// </summary>
public interface IWindowCommands
{
    void Minimize();

    void ToggleMaximize();

    void Close();

    void TogglePin();

    void BeginDrag();

    void SetTextInputFocused(bool focused);
}
