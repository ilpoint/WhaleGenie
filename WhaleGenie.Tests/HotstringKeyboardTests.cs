using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class HotstringKeyboardTests
{
    [Fact]
    public void The_keyboard_appends_characters_and_control_characters()
    {
        Ui.Run(() =>
        {
            var window = new HotstringKeyboardWindow("a");
            window.Show();
            var changed = string.Empty;
            window.TextChanged += text => changed = text;

            Click(window, "b");
            Click(window, Strings.Get("HotstringKeyboard.Space"));
            Click(window, Strings.Get("HotstringKeyboard.Tab"));
            Click(window, Strings.Get("HotstringKeyboard.Enter"));

            Assert.Equal("ab \t\n", window.Text);
            Assert.Equal(window.Text, changed);
            window.Close();
        });
    }

    [Fact]
    public void Shift_changes_letters_and_upper_symbols()
    {
        Ui.Run(() =>
        {
            var window = new HotstringKeyboardWindow();
            window.Show();

            Click(window, Strings.Get("HotstringKeyboard.Shift"));
            Click(window, "A");
            Click(window, "!");

            Assert.Equal("A!", window.Text);
            window.Close();
        });
    }

    [Fact]
    public void The_keyboard_stops_at_the_shared_forty_character_limit()
    {
        Ui.Run(() =>
        {
            var window = new HotstringKeyboardWindow(new string('a', 39));
            window.Show();

            Click(window, "b");
            Click(window, "c");

            Assert.Equal(HotstringCharacters.MaximumLength, window.Text.Length);
            Assert.EndsWith("b", window.Text);
            window.Close();
        });
    }

    [Fact]
    public void End_key_checkboxes_and_custom_capture_write_back_to_end_chars()
    {
        var editor = new MacroEditorViewModel { Name = "typed" };
        editor.TriggerMode = TriggerKind.Hotstring;
        Assert.Contains(editor.HotstringEndKeys, option => option.Value == " " && option.IsChecked);

        editor.HotstringEndKeys.Single(option => option.Value == " ").IsChecked = false;
        editor.BeginHotstringEndCapture();
        editor.CaptureHotstringEnd("$");

        Assert.Contains(editor.HotstringEndKeys,
            option => option.Value == "$" && option.IsCustom && option.IsChecked);
        Assert.DoesNotContain(" ", HotstringCharacters.ParseEndChars(editor.HotstringEndChars));

        foreach (var option in editor.HotstringEndKeys)
        {
            option.IsChecked = false;
        }
        Assert.False(editor.CanSave);
    }

    [Theory]
    [InlineData("A", false, "a")]
    [InlineData("A", true, "A")]
    [InlineData("D5", true, "%")]
    [InlineData("Space", false, " ")]
    [InlineData("Left", false, null)]
    public void Key_capture_returns_only_literal_characters(
        string name, bool shift, string? expected)
    {
        var key = Enum.Parse<Avalonia.Input.Key>(name);
        var modifiers = shift ? Avalonia.Input.KeyModifiers.Shift : Avalonia.Input.KeyModifiers.None;
        Assert.Equal(expected, HotstringKeyCapture.Character(key, modifiers));
    }

    private static void Click(Window window, string content)
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .First(candidate => string.Equals(candidate.Content as string, content,
                StringComparison.Ordinal));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
