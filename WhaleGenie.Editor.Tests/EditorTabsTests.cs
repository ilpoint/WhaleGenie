using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The two questions the tab strip asks of the pages: which page stands for a macro, and which page
/// is in front once a tab closes. The second one exists because the close button sits on the tab —
/// pressing it selects that tab, so the answer has to be noted before the click arrives.
/// </summary>
public class EditorTabsTests
{
    [Fact]
    public void Closing_the_tab_in_front_hands_the_front_back_to_the_root()
    {
        var (pages, _) = SetUp();
        var tabs = new EditorTabs(pages);
        tabs.NotePress(pages[1]);

        var next = tabs.AfterClose(pages[1], pages[1]);

        Assert.Same(pages[0], next);
    }

    [Fact]
    public void Closing_a_tab_behind_the_one_being_read_leaves_the_reader_where_they_were()
    {
        var (pages, _) = SetUp();
        var tabs = new EditorTabs(pages);

        // "c" was in front; pressing the close button on "a"'s tab moved the strip to "a" without
        // the person leaving what they were reading.
        tabs.NotePress(pages[2]);
        var next = tabs.AfterClose(pages[1], pages[1]);

        Assert.Same(pages[2], next);
    }

    [Fact]
    public void A_close_that_arrived_without_a_press_stays_where_the_strip_already_is()
    {
        var (pages, _) = SetUp();
        var tabs = new EditorTabs(pages);

        Assert.Same(pages[2], tabs.AfterClose(pages[1], pages[2]));
        Assert.Same(pages[0], tabs.AfterClose(pages[2], pages[2]));
    }

    [Fact]
    public void A_macro_that_became_a_page_is_found_by_where_it_came_from()
    {
        var (pages, macro) = SetUp();
        var tabs = new EditorTabs(pages);
        pages[1].Editor.Name = "renamed";

        Assert.Same(pages[1], tabs.Find(macro.SubMacros[0]));
    }

    [Fact]
    public void A_macro_no_page_was_opened_from_is_found_by_the_name_it_is_called_by()
    {
        var (pages, _) = SetUp();
        var tabs = new EditorTabs(pages);

        Assert.Same(pages[2], tabs.Find(new MacroItem { Name = "c" }));

        // The root answers to its own name but is not a sub-macro any card can point at.
        Assert.Null(tabs.Find(new MacroItem { Name = "parent" }));
    }

    [Fact]
    public void The_page_holding_an_editor_is_the_one_it_is_in()
    {
        var (pages, _) = SetUp();
        var tabs = new EditorTabs(pages);

        Assert.Same(pages[2], tabs.PageOf(pages[2].Editor));
        Assert.Null(tabs.PageOf(new MacroEditorViewModel()));
    }

    /// <summary>
    /// One macro holding two sub-macros. The root comes first, so the pages are 0 = root, 1 = "a",
    /// 2 = "c"; the instance "a" was written as is the one its page was opened from.
    /// </summary>
    private static (EditorDocumentPages Pages, MacroItem Macro) SetUp()
    {
        var macro = MacroItem.FromJson(new JsonObject
        {
            ["name"] = "parent",
            ["subMacros"] = new JsonArray(Child("a"), Child("c")),
        });

        var session = new MacroEditorSession(macro, [macro], "assets");
        var root = new MacroEditorViewModel
        {
            Persisted = new PersistedVariables(),
            Variables = new VariableSession(),
        };
        root.LoadFrom(macro, []);

        var pages = new EditorDocumentPages(session, root, root.Variables, null, _ => { });
        pages.Build();
        Assert.Equal(3, pages.Count);
        return (pages, macro);
    }

    private static JsonObject Child(string name)
        => new()
        {
            ["name"] = name,
            ["subMacros"] = new JsonArray(),
        };
}
