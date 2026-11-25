using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Editing;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// Opening and closing a document, and the three answers to leaving it. The important one is the
/// first: reading a macro into the model is not an edit, so a document that has not been touched
/// closes without a question. The other two pin down what "save all" and "not saved" mean before
/// the window turns them into buttons.
/// </summary>
public class EditorSaveLifecycleTests
{
    [Fact]
    public void Opening_a_macro_and_closing_it_does_not_ask_to_save_anything()
    {
        var workspace = new ProjectWorkspace();
        var macro = new MacroItem { Name = "clean", Description = "before" };
        macro.Steps.Add(new MacroStep { Type = "control.log" });
        workspace.AddMacro(macro);

        var documents = new EditorDocumentSet(workspace);
        var session = documents.Open("clean");

        Assert.False(session.IsDirty);
        documents.CloseActive();
        Assert.Empty(documents.Tabs);
    }

    [Fact]
    public void A_new_document_is_clean_until_something_is_written_into_it()
    {
        var documents = new EditorDocumentSet(new ProjectWorkspace());

        var session = documents.New();

        Assert.False(session.IsDirty);
        documents.CloseActive();
        Assert.Empty(documents.Tabs);

        var second = documents.New();
        second.AddStep("control.delay");
        Assert.True(second.IsDirty);
    }

    [Fact]
    public void Not_saving_closes_the_document_and_leaves_the_project_alone()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "changed", Description = "before" });
        var documents = new EditorDocumentSet(workspace);
        var session = documents.Open("changed");

        var documentId = Assert.IsType<string>(documents.ActiveId);
        session.Patch(description: "edited");
        Assert.True(session.IsDirty);

        documents.Close(documentId, discard: true);

        Assert.Empty(documents.Tabs);
        Assert.Equal("before", workspace.Macros.Single().Description);
    }

    [Fact]
    public void Saving_every_document_writes_children_before_the_macro_that_owns_them()
    {
        var workspace = new ProjectWorkspace();
        var root = new MacroItem { Name = "host" };
        root.SubMacros.Add(new MacroItem { Name = "helper" });
        root.Steps.Add(SubMacroEditor.CallStep(root.SubMacros[0]));
        workspace.AddMacro(root);

        var documents = new EditorDocumentSet(workspace);
        documents.Open("host");
        var hostId = Assert.IsType<string>(documents.ActiveId);
        var child = documents.OpenSubmacro(hostId, "helper");
        child.AddStep("control.log");
        Assert.True(child.IsDirty);

        var results = documents.SaveAll();

        Assert.All(results, result => Assert.Contains(result.Outcome,
            new[] { EditorSaveOutcomes.Saved, EditorSaveOutcomes.Unchanged }));
        Assert.DoesNotContain(documents.Tabs, tab => tab.Dirty);
        Assert.True(workspace.IsDirty, "the parent macro was not marked by the child's save");
        Assert.Equal("control.log", workspace.Macros.Single().SubMacros
            .Single(macro => macro.Name == "helper").Steps.Single().Type);
        Assert.DoesNotContain(workspace.Macros, macro => macro.Name == "helper");
    }
}
