using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Contracts;
using WhaleGenie.Editing;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The document set is the thing the page names in every command now: an id that survives saves,
/// renames and reopening, a child whose edit belongs to its parent, and save results the page can
/// tell apart from a success.
/// </summary>
public class EditorDocumentLifecycleTests
{
    [Fact]
    public void Saving_a_new_macro_keeps_one_document_and_reopens_it()
    {
        var workspace = new ProjectWorkspace();
        var documents = new EditorDocumentSet(workspace);
        var session = documents.New();
        session.Patch(name: "alpha", description: "first");

        Assert.True(session.Save().Succeeded);
        var id = Assert.Single(documents.Tabs).DocumentId;

        Assert.Same(session, documents.Open("alpha"));
        Assert.Equal(id, Assert.Single(documents.Tabs).DocumentId);
        Assert.Equal("first", workspace.Macros.Single().Description);
    }

    [Fact]
    public void Renaming_an_open_macro_does_not_create_another_tab()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "before" });
        var documents = new EditorDocumentSet(workspace);
        var session = documents.Open("before");
        var id = Assert.Single(documents.Tabs).DocumentId;

        session.Patch(name: "after");
        Assert.True(session.Save().Succeeded);

        Assert.Same(session, documents.Open("after"));
        var tab = Assert.Single(documents.Tabs);
        Assert.Equal(id, tab.DocumentId);
        Assert.Equal("after", tab.Name);
        Assert.Equal("after", workspace.Macros.Single().Name);
    }

    [Fact]
    public void A_submacro_save_marks_the_parent_and_the_parent_save_writes_the_project()
    {
        var workspace = new ProjectWorkspace();
        var parent = new MacroItem { Name = "host" };
        parent.SubMacros.Add(new MacroItem { Name = "helper" });
        workspace.AddMacro(parent);
        var documents = new EditorDocumentSet(workspace);
        documents.Open("host");
        var hostId = Assert.IsType<string>(documents.ActiveId);
        var child = documents.OpenSubmacro(hostId, "helper");
        child.AddStep("control.log");

        Assert.True(documents.Tabs.Single(tab => tab.Name == "host").Dirty);
        var results = documents.SaveAll();

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Contains(result.Outcome,
            new[] { EditorSaveOutcomes.Saved, EditorSaveOutcomes.Unchanged }));
        Assert.DoesNotContain(documents.Tabs, tab => tab.Dirty);
        Assert.True(workspace.IsDirty);
        Assert.Equal("control.log", workspace.Macros.Single().SubMacros.Single().Steps.Single().Type);
    }

    [Fact]
    public void Saving_a_document_whose_target_left_the_project_is_a_conflict()
    {
        var workspace = new ProjectWorkspace();
        var target = new MacroItem { Name = "gone", Description = "before" };
        workspace.AddMacro(target);
        var documents = new EditorDocumentSet(workspace);
        var session = documents.Open("gone");
        session.Patch(description: "edited");

        workspace.RemoveMacro(target);
        var outcome = session.Save();

        Assert.Equal(EditorSaveOutcomes.Conflict, outcome.Outcome);
        Assert.True(session.IsDirty);
        Assert.Empty(workspace.Macros);
        Assert.True(Assert.Single(documents.Tabs).Dirty);
    }

    [Fact]
    public void A_blocked_document_reports_blocked_and_stays_dirty()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "blocked" });
        var documents = new EditorDocumentSet(workspace);
        var session = documents.Open("blocked");
        session.Patch(name: string.Empty);

        var result = Assert.Single(documents.Save());

        Assert.Equal(EditorSaveOutcomes.Blocked, result.Outcome);
        Assert.True(session.IsDirty);
        Assert.False(string.IsNullOrWhiteSpace(result.Problem));
    }

    [Fact]
    public void Closing_the_active_tab_activates_the_tab_to_its_left()
    {
        var workspace = new ProjectWorkspace();
        foreach (var name in new[] { "one", "two", "three" })
        {
            workspace.AddMacro(new MacroItem { Name = name });
        }

        var documents = new EditorDocumentSet(workspace);
        foreach (var name in new[] { "one", "two", "three" })
        {
            documents.Open(name);
        }

        var two = documents.Tabs.Single(tab => tab.Name == "two");
        documents.Activate(two.DocumentId);
        documents.Close(two.DocumentId);

        Assert.Equal("one", documents.Active?.Name);
        Assert.Equal(["one", "three"], documents.Tabs.Select(tab => tab.Name));
    }

    [Fact]
    public void A_dirty_child_makes_the_parent_tab_dirty_and_keeps_it_from_closing()
    {
        var workspace = new ProjectWorkspace();
        var parent = new MacroItem { Name = "host" };
        parent.SubMacros.Add(new MacroItem { Name = "helper" });
        workspace.AddMacro(parent);
        var documents = new EditorDocumentSet(workspace);
        documents.Open("host");
        var hostId = Assert.IsType<string>(documents.ActiveId);
        var child = documents.OpenSubmacro(hostId, "helper");
        child.AddStep("control.log");

        Assert.True(documents.Tabs.Single(tab => tab.Name == "host").Dirty);
        Assert.Throws<InvalidOperationException>(() => documents.Close(hostId));

        documents.Close(hostId, discard: true);
        Assert.Empty(documents.Tabs);
    }

    [Fact]
    public void New_documents_get_different_default_names()
    {
        var workspace = new ProjectWorkspace();
        var documents = new EditorDocumentSet(workspace);

        var first = documents.New();
        var second = documents.New();

        Assert.NotEqual(first.Name, second.Name);
        Assert.False(string.IsNullOrWhiteSpace(first.Name));
        Assert.False(string.IsNullOrWhiteSpace(second.Name));
    }

    [Fact]
    public void Two_new_submacro_drafts_are_named_apart_and_join_the_list_only_when_saved()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "host" });
        var documents = new EditorDocumentSet(workspace);
        var host = documents.Open("host");
        var hostId = Assert.IsType<string>(documents.ActiveId);

        var first = documents.CreateSubmacro(hostId, null);
        var second = documents.CreateSubmacro(hostId, null);

        Assert.NotEqual(first.Name, second.Name);
        Assert.Empty(host.Snapshot().SubMacros);
        Assert.True(first.Save().Succeeded);
        Assert.True(second.Save().Succeeded);
        Assert.Equal(2, host.Snapshot().SubMacros.Count);
    }

    [Fact]
    public void Importing_a_project_macro_adds_a_child_and_opens_its_tab()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "host" });
        var source = new MacroItem { Name = "shared" };
        source.Steps.Add(new MacroStep { Type = "control.log" });
        workspace.AddMacro(source);
        var documents = new EditorDocumentSet(workspace);
        documents.Open("host");
        var hostId = Assert.IsType<string>(documents.ActiveId);

        var imported = documents.ImportSubmacro(hostId, source, "renamed");

        Assert.Equal("renamed", imported.Name);
        var host = documents.Activate(hostId).Snapshot();
        Assert.Contains("renamed", host.SubMacros);
        Assert.Empty(host.Steps);
        Assert.Equal(hostId, documents.Tabs.Single(tab => tab.Name == "renamed").OwnerDocumentId);
    }

    [Fact]
    public void Every_observable_model_property_is_classified_as_document_or_view()
    {
        var observable = typeof(MacroEditorViewModel).GetProperties(
                BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<ObservablePropertyAttribute>() is not null)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(observable);
        foreach (var name in observable)
        {
            Assert.True(MacroDocumentFields.Document.Contains(name)
                || MacroDocumentFields.View.Contains(name),
                $"{name} is neither document state nor view state");
        }

        Assert.Empty(MacroDocumentFields.Document.Intersect(MacroDocumentFields.View));
    }
}
