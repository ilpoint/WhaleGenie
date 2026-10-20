using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
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
    public void Every_default_end_key_is_a_selected_toggle_button_without_checkbox_chrome()
    {
        Ui.Run(() =>
        {
            var (window, _) = OpenHotstringEditor();

            var buttons = EndKeyButtons(window).ToList();
            Assert.Equal(HotstringCharacters.EndKeyChoices.Count, buttons.Count);
            Assert.All(buttons, button => Assert.True(button.IsChecked));

            var endingLabels = HotstringCharacters.EndKeyChoices
                .Select(choice => choice.Name)
                .ToHashSet(StringComparer.Ordinal);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<CheckBox>(),
                box => box.Content is string text && endingLabels.Contains(text));

            var space = EndKeyButton(window, "Space");
            var selectedBackground = Assert.IsAssignableFrom<ISolidColorBrush>(space.Background);
            Assert.Equal(Color.Parse("#2E5F8A"), selectedBackground.Color);
            Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(space.Foreground).Color);

            space.IsChecked = false;
            Dispatcher.UIThread.RunJobs();

            var offBackground = Assert.IsAssignableFrom<ISolidColorBrush>(space.Background);
            Assert.NotEqual(selectedBackground.Color, offBackground.Color);
            Assert.NotEqual(Colors.White,
                Assert.IsAssignableFrom<ISolidColorBrush>(space.Foreground).Color);

            space.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(selectedBackground.Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(space.Background).Color);
            window.Close();
        });
    }

    [Fact]
    public void Toggle_buttons_sync_end_chars_and_keep_custom_captures()
    {
        Ui.Run(() =>
        {
            var (window, editor) = OpenHotstringEditor();
            var space = EndKeyButton(window, "Space");

            space.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(" ",
                HotstringCharacters.ParseEndChars(editor.HotstringEndChars));

            space.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(" ",
                HotstringCharacters.ParseEndChars(editor.HotstringEndChars));

            editor.BeginHotstringEndCapture();
            editor.CaptureHotstringEnd("$");
            Dispatcher.UIThread.RunJobs();

            var custom = EndKeyButton(window, "$");
            Assert.True(custom.IsChecked);
            Assert.Contains(editor.HotstringEndKeys,
                option => option.Value == "$" && option.IsCustom && option.IsChecked);

            var restore = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content is string text
                    && text == Strings.Get("Editor.HotstringEndCharsReset"));
            restore.Command!.Execute(restore.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(HotstringCharacters.ParseEndChars(HotstringCharacters.DefaultEndChars),
                HotstringCharacters.ParseEndChars(editor.HotstringEndChars));
            Assert.DoesNotContain("$",
                HotstringCharacters.ParseEndChars(editor.HotstringEndChars));
            Assert.All(EndKeyButtons(window), button => Assert.True(button.IsChecked));

            foreach (var option in editor.HotstringEndKeys)
            {
                option.IsChecked = false;
            }
            Assert.False(editor.CanSave);
            window.Close();
        });
    }

    [Fact]
    public void The_typed_string_field_is_a_keyboard_button()
    {
        Ui.Run(() =>
        {
            var (window, editor) = OpenHotstringEditor();
            editor.HotstringText = "a b";
            Dispatcher.UIThread.RunJobs();

            var field = window.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Classes.Contains("KeyFieldButton")
                && ReferenceEquals(editor, button.DataContext));
            Assert.Equal("a␠b", field.Content);

            field.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var keyboard = window.OwnedWindows.OfType<HotstringKeyboardWindow>().Single();
            Assert.Equal("a b", keyboard.Text);
            keyboard.Close();
            window.Close();
        });
    }

    [Fact]
    public void Neither_language_mentions_AutoHotkey_escaping_in_the_ending_key_help()
    {
        Ui.Run(() =>
        {
            var language = Strings.Current.Language;
            try
            {
                foreach (var current in new[] { Language.English, Language.Chinese })
                {
                    Strings.Current.Language = current;
                    var hint = Strings.Get("Editor.HotstringEndCharsHint");
                    Assert.DoesNotContain("AutoHotkey", hint, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("AutoHotKey", hint, StringComparison.Ordinal);
                    Assert.Contains(current == Language.English ? "highlighted" : "点亮", hint,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                Strings.Current.Language = language;
            }
        });
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

    private static (MacroEditorWindow Window, MacroEditorViewModel ViewModel) OpenHotstringEditor()
    {
        var macro = new MacroItem { Name = "typed" };
        macro.Triggers.Add(new TriggerEntry
        {
            Kind = TriggerKind.Hotstring,
            MacroName = macro.Name,
            Abbreviation = "btw",
        });
        var window = new MacroEditorWindow(macro, [macro]);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, (MacroEditorViewModel)window.DataContext!);
    }

    private static IEnumerable<ToggleButton> EndKeyButtons(Window window)
        => window.GetVisualDescendants().OfType<ToggleButton>()
            .Where(button => button.Classes.Contains("EndKeyChip"));

    private static ToggleButton EndKeyButton(Window window, string label)
        => EndKeyButtons(window).Single(button =>
            string.Equals(button.Content as string, label, StringComparison.Ordinal));

    private static void Click(Window window, string content)
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .First(candidate => string.Equals(candidate.Content as string, content,
                StringComparison.Ordinal));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
