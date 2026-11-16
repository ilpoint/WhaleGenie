using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// What one save ends in. The window says the three endings out loud; which one it gets, and what
/// has been let go by the time it does, is decided here — including the order, which is the part a
/// test can keep honest: nothing is built for a page that cannot be written, and nothing is let go
/// before the project has taken the macro.
/// </summary>
public class EditorCommitTests
{
    [Fact]
    public void A_page_that_cannot_be_written_stops_the_save_before_the_project_is_asked()
    {
        var (commit, pages, session, root) = SetUp();
        var asked = false;
        commit.Commit = _ =>
        {
            asked = true;
            return true;
        };
        pages[0].Editor.Name = string.Empty;
        root.IsDirty = true;

        var outcome = commit.Save();

        var blocked = Assert.IsType<CommitOutcome.PageCannotSave>(outcome);
        Assert.Same(pages[0], blocked.Page);
        Assert.False(asked);
        Assert.True(root.IsDirty);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void A_project_that_will_not_take_the_macro_leaves_every_page_unsaved()
    {
        var (commit, pages, session, root) = SetUp();
        commit.Commit = _ => false;
        foreach (var page in pages)
        {
            page.Editor.IsDirty = true;
        }

        Assert.IsType<CommitOutcome.ProjectRefused>(commit.Save());

        Assert.All(pages, page => Assert.True(page.Editor.IsDirty));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void A_saved_document_is_one_macro_holding_every_open_page_as_a_sub_macro()
    {
        var (commit, pages, session, root) = SetUp();
        MacroItem? handed = null;
        commit.Commit = macro =>
        {
            handed = macro;
            return true;
        };
        pages[1].Editor.Name = "renamed";
        pages[1].Editor.IsDirty = true;
        root.IsDirty = true;

        var saved = Assert.IsType<CommitOutcome.Saved>(commit.Save());

        Assert.Same(handed, saved.Macro);
        Assert.Equal("parent", saved.Macro.Name);
        Assert.Equal(["renamed", "b", "c"],
            saved.Macro.SubMacros.Select(macro => macro.Name).ToList());
        Assert.All(pages, page => Assert.False(page.Editor.IsDirty));
        Assert.False(session.IsDirty);
        Assert.False(root.IsDirty);
    }

    [Fact]
    public void With_no_project_holding_the_list_the_save_still_ends_with_the_macro()
    {
        var (commit, pages, session, _) = SetUp();
        Assert.Null(commit.Commit);
        pages[2].Editor.IsDirty = true;

        var saved = Assert.IsType<CommitOutcome.Saved>(commit.Save());

        Assert.Equal("parent", saved.Macro.Name);
        Assert.Equal(3, saved.Macro.SubMacros.Count);
        Assert.All(pages, page => Assert.False(page.Editor.IsDirty));
        Assert.False(session.IsDirty);
    }

    /// <summary>
    /// One macro with a sub-macro that holds another, and a second sub-macro beside it: the pages
    /// are what a save reads, so the snapshot has to carry every one of them.
    /// </summary>
    private static (EditorCommit Commit, EditorDocumentPages Pages, MacroEditorSession Session,
        MacroEditorViewModel Root) SetUp()
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

        // The draft is not what these cases are about: it stands in for the window's, and what it
        // does with the disk is checked with the draft itself.
        var recovery = new RecoveryDraft(() => session.IsDirty || root.IsDirty, () => { }, () => { });
        return (new EditorCommit(session, pages, root, recovery), pages, session, root);
    }

    private static JsonObject Child(string name, params JsonObject[] children)
        => new()
        {
            ["name"] = name,
            ["subMacros"] = new JsonArray(children.Cast<JsonNode?>().ToArray()),
        };
}
