using WhaleGenie.ViewModels;

namespace WhaleGenie.ViewModels;

// The value picker is a view model that used to feed a native window. The headless bridge needs
// the two caret details the window read through its own geometry; they stay here so the token
// snapshot can be built without moving any of the picker's rules into the page.
public sealed partial class ValuePickerViewModel
{
    public NodePath HeadlessCaret => _draft.Caret;

    public bool HeadlessCaretAfter => _draft.CaretAfter;
}
