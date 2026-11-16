using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The pages one editor has open. The window wires each child editor and owns the dialogs and the
/// selection; what is checked here is the part a window cannot answer for itself: the order the
/// pages are built in, that a new one gets a name nothing else has, and that taking one out takes
/// it out of the editor's documents as well as the tab strip.
/// </summary>
public class EditorDocumentPagesTests
{
    [Fact]
    public void The_root_comes_first_and_each_sub_macro_follows_the_one_it_was_written_in()
    {
        var (pages, session) = SetUp();

        Assert.Equal(["parent", "a", "b", "c"],
            pages.Select(page => page.Editor.Name).ToList());
        Assert.Equal(pages.Select(page => page.Editor.Name),
            session.Documents.Select(page => page.Editor.Name));
        Assert.True(pages[0].IsCurrent);
        Assert.All(pages.Skip(1), page => Assert.False(page.IsCurrent));
    }

    [Fact]
    public void A_new_sub_macro_is_named_so_no_other_page_or_project_macro_answers_to_it()
    {
        var (pages, session) = SetUp();
        var defaultName = Strings.Get("Editor.NewSubMacroName");

        var first = pages.AddBlank();
        var second = pages.AddBlank();

        Assert.Equal(defaultName, first.Editor.Name);
        Assert.Equal($"{defaultName} (2)", second.Editor.Name);
        Assert.Contains(first, session.Documents);
        Assert.Contains(second, session.Documents);
    }

    [Fact]
    public void A_macro_brought_in_keeps_its_name_starts_unsaved_and_remembers_where_it_came_from()
    {
        var (pages, session) = SetUp();
        var brought = new MacroItem { Name = "shared" };

        var page = pages.AddExisting(brought);

        Assert.Equal("shared", page.Editor.Name);
        Assert.True(page.Editor.IsDirty);
        Assert.Same(brought, page.Source);
        Assert.Contains(page, session.Documents);
        Assert.Equal(pages.Count, session.Documents.Count);
    }

    [Fact]
    public void A_page_forgotten_leaves_the_tab_strip_and_the_document_list_together()
    {
        var (pages, session) = SetUp();
        var page = pages[2];

        pages.Forget(page);

        Assert.DoesNotContain(page, pages);
        Assert.DoesNotContain(page, session.Documents);
        Assert.Equal(pages.Count, session.Documents.Count);
    }

    /// <summary>
    /// One macro with a sub-macro that holds another, and a second sub-macro beside it: the order
    /// the editor has to open them in is the order they were written, depth first.
    /// </summary>
    private static (EditorDocumentPages Pages, MacroEditorSession Session) SetUp()
    {
        var macro = MacroItem.FromJson(new JsonObject
        {
            ["name"] = "parent",
            ["subMacros"] = new JsonArray(
                Child("a", Child("b")),
                Child("c")),
        });

        var session = new MacroEditorSession(macro, [macro], "assets");
        var root = new MacroEditorViewModel
        {
            Persisted = new PersistedVariables(),
            Variables = new VariableSession(),
        };
        root.LoadFrom(macro);

        var pages = new EditorDocumentPages(session, root, root.Variables, null, _ => { });
        pages.Build();
        return (pages, session);
    }

    private static JsonObject Child(string name, params JsonObject[] children)
        => new()
        {
            ["name"] = name,
            ["subMacros"] = new JsonArray(children.Cast<JsonNode?>().ToArray()),
        };
}
