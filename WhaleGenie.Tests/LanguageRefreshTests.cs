using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// A language switch has to reach the text a view model computes, not only the text a binding
/// reads through the table's indexer. These pin the parts that keep a copy: the editor's trigger
/// list, the picker's dock and pages, and the words on a macro card.
/// </summary>
public class LanguageRefreshTests
{
    [Fact]
    public void The_editor_relabels_its_trigger_options()
    {
        MacroEditorViewModel? editor = null;
        string? english = null;

        InterfaceLanguage.InEnglish(() =>
        {
            editor = new MacroEditorViewModel();
            english = editor.TriggerOptions[0];
        });

        InterfaceLanguage.InChinese(() =>
        {
            editor!.RefreshLanguage();
            Assert.NotEqual(english, editor.TriggerOptions[0]);
        });
    }

    [Fact]
    public void The_action_picker_relabels_its_dock_and_pages()
    {
        AddActionViewModel? form = null;
        string? dock = null;
        string? page = null;

        InterfaceLanguage.InEnglish(() =>
        {
            form = new AddActionViewModel();
            dock = form.Categories[1].Label;
            page = form.Pages[0].Title;
        });

        InterfaceLanguage.InChinese(() =>
        {
            form!.RefreshLanguage();
            Assert.NotEqual(dock, form.Categories[1].Label);
            Assert.NotEqual(page, form.Pages[0].Title);
        });
    }

    [Fact]
    public void A_macro_card_relabels_its_trigger()
    {
        MacroItem? macro = null;
        string? english = null;

        InterfaceLanguage.InEnglish(() =>
        {
            macro = new MacroItem();
            english = macro.Trigger;
        });

        InterfaceLanguage.InChinese(() =>
        {
            macro!.RefreshLanguage();
            Assert.NotEqual(english, macro.Trigger);
        });
    }
}
