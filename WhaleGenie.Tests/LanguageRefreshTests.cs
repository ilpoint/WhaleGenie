using WhaleGenie.Localization;
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

        Ui.Run(() => InterfaceLanguage.InEnglish(() =>
        {
            editor = new MacroEditorViewModel();
            english = editor.TriggerKindChoices[0].Label;
        }));

        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            editor!.RefreshLanguage();
            Assert.NotEqual(english, editor.TriggerKindChoices[0].Label);
        }));
    }

    [Fact]
    public void The_action_form_relabels_its_pages()
    {
        AddActionViewModel? form = null;
        string? page = null;

        Ui.Run(() => InterfaceLanguage.InEnglish(() =>
        {
            form = new AddActionViewModel(ActionCatalog.RunnableActions);
            page = form.Pages[0].Title;
        }));

        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            form!.RefreshLanguage();
            Assert.NotEqual(page, form.Pages[0].Title);
        }));
    }

    [Fact]
    public void A_macro_card_relabels_its_trigger()
    {
        MacroItem? macro = null;
        string? english = null;

        Ui.Run(() => InterfaceLanguage.InEnglish(() =>
        {
            macro = new MacroItem();
            english = macro.Trigger;
        }));

        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            macro!.RefreshLanguage();
            Assert.NotEqual(english, macro.Trigger);
        }));
    }

    [Fact]
    public void The_macro_interface_relabels_its_kinds()
    {
        MacroInterfaceViewModel? model = null;
        string? english = null;

        Ui.Run(() => InterfaceLanguage.InEnglish(() =>
        {
            model = new MacroInterfaceViewModel(new MacroEditorViewModel());

            // A model with no kinds would make the check below fail on an index rather than say
            // what went wrong; and an empty label would make "it changed" true for the wrong
            // reason. Both are stated here so a failure names the thing that was missing.
            Assert.True(model.Types.Count > 0, "the interface has no kinds to label");
            english = model.Types[0].Label;
            Assert.False(string.IsNullOrWhiteSpace(english), "the English label came out empty");
            Assert.Equal(Language.English, Strings.Current.Language);
        }));

        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            // Checked before the refresh, not after: if the switch never took, the labels would
            // come out equal for a reason that has nothing to do with refreshing, and the failure
            // would blame the wrong thing.
            Assert.Equal(Language.Chinese, Strings.Current.Language);

            model!.RefreshLanguage();
            var chinese = model.Types[0].Label;

            // Said out loud rather than through Assert.NotEqual: this one has failed once in a
            // whole-suite run and passed on its own, so the message has to carry enough to tell a
            // stale label from a language that was never switched.
            Assert.True(chinese != english,
                $"the label did not change: english='{english}' chinese='{chinese}' "
                + $"language={Strings.Current.Language}");
        }));
    }

    [Fact]
    public void The_global_variable_manager_relabels_its_buttons_filters_and_types()
    {
        GlobalVariablesViewModel? model = null;
        string? button = null;
        string? filter = null;
        string? type = null;

        Ui.Run(() => InterfaceLanguage.InEnglish(() =>
        {
            model = new GlobalVariablesViewModel(
            [
                new VariableDefinition
                {
                    Name = "total",
                    Scope = VariableScope.Global,
                    Type = "number",
                },
            ]);
            button = model.NewLabel;
            filter = model.Filters[1].Label;
            type = model.Selected!.TypeOptions[0].Display;
        }));

        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            model!.RefreshLanguage();
            Assert.NotEqual(button, model.NewLabel);
            Assert.NotEqual(filter, model.Filters[1].Label);
            Assert.NotEqual(type, model.Selected!.TypeOptions[0].Display);
        }));
    }
}
