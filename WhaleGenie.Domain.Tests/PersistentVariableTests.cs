using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Domain.Tests;

public class PersistentVariableTests
{
    [Fact]
    public void Renaming_a_global_moves_its_saved_value_with_the_name()
    {
        var path = TempPath(out var folder);
        var session = new VariableSession();
        using var store = new PersistedVariables(path);
        store.Initialize(session);
        try
        {
            var definition = new VariableDefinition
            {
                Name = "oldTotal",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
                PersistValue = true,
            };
            session.Adopt(definition);
            session.Values.SetNumber("oldTotal", 8);
            store.PersistChanged(definition, true);
            store.Flush();

            session.ClearGlobals();
            store.Rename("oldTotal", "newTotal");
            session.Values.Rename("oldTotal", "newTotal");
            var renamed = new VariableDefinition
            {
                Name = "newTotal",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
            };
            session.Adopt(renamed);
            Apply(store, [renamed], session);

            Assert.True(session.Values.TryGet("newTotal", out var value));
            Assert.Equal(8, value.AsNumber());
            Assert.False(session.Values.Contains("oldTotal"));
        }
        finally
        {
            Finish(store, folder);
        }
    }

    [Fact]
    public void A_checked_global_is_restored_from_the_users_file_and_unchecking_removes_it()
    {
        var path = TempPath(out var folder);
        var session = new VariableSession();
        using var store = new PersistedVariables(path);
        store.Initialize(session);
        try
        {
            var definition = new VariableDefinition
            {
                Name = "total",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
                Default = Formula.Text("1"),
            };
            session.Adopt(definition);
            definition.PersistValue = true;
            store.PersistChanged(definition, true);
            session.Values.SetNumber("total", 5);
            session.Values.SetNumber("total", 7);
            store.Flush();

            Assert.True(File.Exists(path));
            session.ClearGlobals();
            session.Values.Clear();

            var reopened = new VariableDefinition
            {
                Name = "total",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
                Default = Formula.Text("1"),
            };
            session.Adopt(reopened);
            Apply(store, [reopened], session);

            Assert.True(reopened.PersistValue);
            Assert.True(session.Values.TryGet("total", out var restored));
            Assert.Equal(7, restored.AsNumber());

            store.PersistChanged(reopened, false);
            store.Flush();
            Assert.DoesNotContain("total", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Finish(store, folder);
        }
    }

    [Fact]
    public void A_bad_saved_value_falls_back_to_the_project_default()
    {
        var path = TempPath(out var folder);
        File.WriteAllText(path, """
            {
              "version": 2,
              "variables": [
                { "name": "amount", "value": "not a number" }
              ]
            }
            """);
        var session = new VariableSession();
        using var store = new PersistedVariables(path);
        store.Initialize(session);
        var definition = new VariableDefinition
        {
            Name = "amount",
            Scope = VariableScope.Global,
            Type = ValueKind.Number,
            Default = Formula.Text("3"),
        };

        try
        {
            Apply(store, [definition], session);

            Assert.True(definition.PersistValue);
            Assert.True(session.Values.TryGet("amount", out var value));
            Assert.Equal(3, value.AsNumber());
        }
        finally
        {
            Finish(store, folder);
        }
    }

    [Fact]
    public void A_new_global_is_not_persistent_unless_it_is_checked()
    {
        var definition = new VariableDefinition
        {
            Name = "ordinary",
            Scope = VariableScope.Global,
            Type = ValueKind.Text,
        };

        Assert.False(definition.PersistValue);
    }

    /// <summary>
    /// A value is stored as JSON rather than as the line it reads as: a list whose item holds a
    /// comma would come back as two items, and a number with more than six decimals would come
    /// back rounded. Neither is noticeable until the value is needed a day later.
    /// </summary>
    [Fact]
    public void A_list_item_with_a_comma_survives_a_restart()
    {
        var path = TempPath(out var folder);
        var session = new VariableSession();
        using var store = new PersistedVariables(path);
        store.Initialize(session);
        try
        {
            var definition = Global("names", "list");
            definition.PersistValue = true;
            store.PersistChanged(definition, true);
            session.Values.Set("names",
                Value.FromList([Value.FromText("a, b"), Value.FromText("c")]));
            store.Flush();

            session.Values.Clear();
            var reopened = Global("names", "list");
            Apply(store, [reopened], session);

            Assert.True(session.Values.TryGet("names", out var restored));
            Assert.Equal(2, restored.Items.Count);
            Assert.Equal("a, b", restored.Items[0].AsText());
        }
        finally
        {
            Finish(store, folder);
        }
    }

    [Fact]
    public void A_number_keeps_more_than_six_decimals()
    {
        var path = TempPath(out var folder);
        var session = new VariableSession();
        using var store = new PersistedVariables(path);
        store.Initialize(session);
        try
        {
            var definition = Global("ratio", "number");
            definition.PersistValue = true;
            store.PersistChanged(definition, true);
            session.Values.SetNumber("ratio", 0.123456789);
            store.Flush();

            session.Values.Clear();
            var reopened = Global("ratio", "number");
            Apply(store, [reopened], session);

            Assert.True(session.Values.TryGet("ratio", out var restored));
            Assert.Equal(0.123456789, restored.AsNumber(), 12);
        }
        finally
        {
            Finish(store, folder);
        }
    }

    /// <summary>
    /// A file written in a shape this build does not read is not guessed at: nothing is restored,
    /// and the first save replaces it with the one shape this build writes.
    /// </summary>
    [Fact]
    public void A_file_in_an_older_shape_starts_empty_and_is_rewritten()
    {
        var path = TempPath(out var folder);
        File.WriteAllText(path, """
            {
              "version": 1,
              "variables": [
                { "name": "amount", "value": "12.5" }
              ]
            }
            """);
        var session = new VariableSession();
        using var store = new PersistedVariables(path);
        store.Initialize(session);
        try
        {
            var definition = Global("amount", "number");
            definition.Default = Formula.Text("3");
            Apply(store, [definition], session);

            Assert.False(definition.PersistValue);
            Assert.False(session.Values.TryGet("amount", out _));

            store.Flush();
            Assert.Contains("\"version\": 2", File.ReadAllText(path));
        }
        finally
        {
            Finish(store, folder);
        }
    }

    [Fact]
    public void One_bad_entry_does_not_take_the_others_with_it()
    {
        var path = TempPath(out var folder);
        File.WriteAllText(path, """
            {
              "version": 2,
              "variables": [
                { "name": "good", "value": 5 },
                "not an entry"
              ]
            }
            """);
        var session = new VariableSession();
        using var store = new PersistedVariables(path);
        store.Initialize(session);
        try
        {
            var definition = Global("good", "number");
            Apply(store, [definition], session);

            Assert.True(definition.PersistValue);
            Assert.True(session.Values.TryGet("good", out var value));
            Assert.Equal(5, value.AsNumber());
        }
        finally
        {
            Finish(store, folder);
        }
    }

    /// <summary>
    /// A name is the key, so the saved value belongs to the word: whichever project declares a
    /// global called "count" reads the value that name was left at.
    /// </summary>
    [Fact]
    public void One_name_is_one_saved_value_whichever_project_opens_it()
    {
        var path = TempPath(out var folder);
        var session = new VariableSession();
        using var store = new PersistedVariables(path);
        store.Initialize(session);
        try
        {
            var first = Global("count", "number");
            first.PersistValue = true;
            session.Values.SetNumber("count", 10);
            store.PersistChanged(first, true);
            store.Flush();

            session.Values.Clear();
            var second = Global("count", "number");
            Apply(store, [second], session);

            Assert.True(second.PersistValue);
            Assert.True(session.Values.TryGet("count", out var restored));
            Assert.Equal(10, restored.AsNumber());
        }
        finally
        {
            Finish(store, folder);
        }
    }

    private static VariableDefinition Global(string name, string type) => new()
    {
        Name = name,
        Scope = VariableScope.Global,
        Type = VariableTypes.KindOf(type),
    };

    private static string TempPath(out string folder)
    {
        folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "persistent-variables.json");
    }

    /// <summary>
    /// Puts the values a store holds back into a project's table, the way opening one does. The
    /// declarations go in first, because a value is written under the name its declaration has.
    /// </summary>
    private static void Apply(PersistedVariables store, VariableDefinition[] definitions,
        VariableSession session)
    {
        foreach (var definition in definitions)
        {
            if (!session.IsGlobal(definition.Name))
            {
                session.Adopt(definition);
            }
        }

        foreach (var (name, value, persisted) in store.ApplyTo(definitions))
        {
            var definition = definitions.First(candidate => candidate.Name == name);
            definition.PersistValue = persisted;
            if (persisted)
            {
                session.Values.Set(name, value);
            }
        }
    }

    private static void Finish(PersistedVariables store, string folder)
    {
        store.Flush();

        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
