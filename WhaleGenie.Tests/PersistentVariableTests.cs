using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

[RestoreSharedState]
public class PersistentVariableTests
{
    [Fact]
    public void Renaming_a_global_moves_its_saved_value_with_the_name()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "persistent-variables.json");
        using var store = new PersistedVariables(path);
        store.Initialize(SharedVariables.Current);
        var previousGlobals = VariableCatalog.Globals.ToList();
        var previousValues = SharedVariables.Current.Values;
        VariableCatalog.Globals.Clear();
        SharedVariables.Current.Clear();
        var definition = new VariableDefinition
        {
            Name = "oldTotal",
            Scope = VariableScope.Global,
            Type = ValueKind.Number,
            PersistValue = true,
        };
        VariableCatalog.Globals.Add(definition);
        SharedVariables.Current.SetNumber("oldTotal", 8);
        store.PersistChanged(definition, true);
        store.Flush();
        try
        {
            VariableCatalog.Globals.Remove(definition);
            store.Rename("oldTotal", "newTotal");
            SharedVariables.Current.Rename("oldTotal", "newTotal");
            var renamed = new VariableDefinition
            {
                Name = "newTotal",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
            };
            VariableCatalog.Globals.Add(renamed);
            Apply(store, [renamed]);

            Assert.True(SharedVariables.Current.TryGet("newTotal", out var value));
            Assert.Equal(8, value.AsNumber());
            Assert.False(SharedVariables.Current.Contains("oldTotal"));
        }
        finally
        {
            VariableCatalog.Globals.Clear();
            foreach (var global in previousGlobals)
            {
                VariableCatalog.Globals.Add(global);
            }

            SharedVariables.Current.Clear();
            foreach (var (name, value) in previousValues)
            {
                SharedVariables.Current.Set(name, value);
            }
        }
    }

    [Fact]
    public void A_checked_global_is_restored_from_the_users_file_and_unchecking_removes_it()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "persistent-variables.json");
        using var store = new PersistedVariables(path);
        store.Initialize(SharedVariables.Current);

        var previousGlobals = VariableCatalog.Globals.ToList();
        var previousValues = SharedVariables.Current.Values;
        VariableCatalog.Globals.Clear();
        SharedVariables.Current.Clear();
        try
        {
            var definition = new VariableDefinition
            {
                Name = "total",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
                DefaultValue = "1",
            };
            VariableCatalog.Globals.Add(definition);
            definition.PersistValue = true;
            store.PersistChanged(definition, true);
            SharedVariables.Current.SetNumber("total", 5);
            SharedVariables.Current.SetNumber("total", 7);
            store.Flush();

            Assert.True(File.Exists(path));
            VariableCatalog.Globals.Clear();
            SharedVariables.Current.Clear();

            var reopened = new VariableDefinition
            {
                Name = "total",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
                DefaultValue = "1",
            };
            VariableCatalog.Globals.Add(reopened);
            Apply(store, [reopened]);

            Assert.True(reopened.PersistValue);
            Assert.True(SharedVariables.Current.TryGet("total", out var restored));
            Assert.Equal(7, restored.AsNumber());

            store.PersistChanged(reopened, false);
            store.Flush();
            Assert.DoesNotContain("total", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            VariableCatalog.Globals.Clear();
            foreach (var variable in previousGlobals)
            {
                VariableCatalog.Globals.Add(variable);
            }

            SharedVariables.Current.Clear();
            foreach (var (name, value) in previousValues)
            {
                SharedVariables.Current.Set(name, value);
            }

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void A_bad_saved_value_falls_back_to_the_project_default()
    {
        var previousValues = SharedVariables.Current.Values;
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "persistent-variables.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "variables": [
                { "name": "amount", "value": "not a number" }
              ]
            }
            """);
        using var store = new PersistedVariables(path);
        store.Initialize(SharedVariables.Current);
        var definition = new VariableDefinition
        {
            Name = "amount",
            Scope = VariableScope.Global,
            Type = ValueKind.Number,
            DefaultValue = "3",
        };

        try
        {
            Apply(store, [definition]);

            Assert.True(definition.PersistValue);
            Assert.True(SharedVariables.Current.TryGet("amount", out var value));
            Assert.Equal(3, value.AsNumber());
        }
        finally
        {
            store.Flush();
            SharedVariables.Current.Clear();
            foreach (var (name, value) in previousValues)
            {
                SharedVariables.Current.Set(name, value);
            }

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
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
        var previous = SharedVariables.Current.Values;
        using var store = new PersistedVariables(path);
        store.Initialize(SharedVariables.Current);
        try
        {
            var definition = Global("aaa111", "names", "list");
            definition.PersistValue = true;
            store.PersistChanged(definition, true);
            SharedVariables.Current.Set("names",
                Value.FromList([Value.FromText("a, b"), Value.FromText("c")]));
            store.Flush();

            SharedVariables.Current.Clear();
            var reopened = Global("aaa111", "names", "list");
            Apply(store, [reopened]);

            Assert.True(SharedVariables.Current.TryGet("names", out var restored));
            Assert.Equal(2, restored.Items.Count);
            Assert.Equal("a, b", restored.Items[0].AsText());
        }
        finally
        {
            Finish(store, folder, previous);
        }
    }

    [Fact]
    public void A_number_keeps_more_than_six_decimals()
    {
        var path = TempPath(out var folder);
        var previous = SharedVariables.Current.Values;
        using var store = new PersistedVariables(path);
        store.Initialize(SharedVariables.Current);
        try
        {
            var definition = Global("bbb222", "ratio", "number");
            definition.PersistValue = true;
            store.PersistChanged(definition, true);
            SharedVariables.Current.SetNumber("ratio", 0.123456789);
            store.Flush();

            SharedVariables.Current.Clear();
            var reopened = Global("bbb222", "ratio", "number");
            Apply(store, [reopened]);

            Assert.True(SharedVariables.Current.TryGet("ratio", out var restored));
            Assert.Equal(0.123456789, restored.AsNumber(), 12);
        }
        finally
        {
            Finish(store, folder, previous);
        }
    }

    /// <summary>
    /// The file a build without identities wrote keeps the value's text and nothing else. It must
    /// still open, and the first save writes it in the shape this build reads.
    /// </summary>
    [Fact]
    public void A_file_from_before_identities_is_still_read()
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
        var previous = SharedVariables.Current.Values;
        using var store = new PersistedVariables(path);
        store.Initialize(SharedVariables.Current);
        try
        {
            var definition = Global("ccc333", "amount", "number");
            definition.DefaultValue = "3";
            Apply(store, [definition]);

            Assert.True(definition.PersistValue);
            Assert.True(SharedVariables.Current.TryGet("amount", out var value));
            Assert.Equal(12.5, value.AsNumber());
        }
        finally
        {
            Finish(store, folder, previous);
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
                { "id": "ddd444", "name": "good", "type": "number", "value": 5 },
                "not an entry"
              ]
            }
            """);
        var previous = SharedVariables.Current.Values;
        using var store = new PersistedVariables(path);
        store.Initialize(SharedVariables.Current);
        try
        {
            var definition = Global("ddd444", "good", "number");
            Apply(store, [definition]);

            Assert.True(definition.PersistValue);
            Assert.True(SharedVariables.Current.TryGet("good", out var value));
            Assert.Equal(5, value.AsNumber());
        }
        finally
        {
            Finish(store, folder, previous);
        }
    }

    /// <summary>
    /// Two projects may both call a global "count". The saved value belongs to the declaration, not
    /// to the word, so opening the second project must not hand it the first project's number.
    /// </summary>
    [Fact]
    public void Two_projects_with_one_name_do_not_share_the_saved_value()
    {
        var path = TempPath(out var folder);
        var previous = SharedVariables.Current.Values;
        using var store = new PersistedVariables(path);
        store.Initialize(SharedVariables.Current);
        try
        {
            var first = Global("eee555", "count", "number");
            first.PersistValue = true;
            SharedVariables.Current.SetNumber("count", 10);
            store.PersistChanged(first, true);
            store.Flush();

            SharedVariables.Current.Clear();
            var second = Global("fff666", "count", "number");
            Apply(store, [second]);

            Assert.False(second.PersistValue);
            Assert.False(SharedVariables.Current.Contains("count"));

            var reopened = Global("eee555", "count", "number");
            Apply(store, [reopened]);

            Assert.True(reopened.PersistValue);
            Assert.True(SharedVariables.Current.TryGet("count", out var restored));
            Assert.Equal(10, restored.AsNumber());
        }
        finally
        {
            Finish(store, folder, previous);
        }
    }

    private static VariableDefinition Global(string id, string name, string type) => new()
    {
        Id = id,
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

    /// <summary>Puts the values a store holds back into the shared table, the way a project opening does.</summary>
    private static void Apply(PersistedVariables store, VariableDefinition[] definitions)
    {
        foreach (var (name, value, persisted) in store.ApplyTo(definitions))
        {
            var definition = definitions.First(candidate => candidate.Name == name);
            definition.PersistValue = persisted;
            if (persisted)
            {
                SharedVariables.Current.Set(name, value);
            }
        }
    }

    private static void Finish(PersistedVariables store, string folder,
        IReadOnlyDictionary<string, Value> previous)
    {
        store.Flush();
        SharedVariables.Current.Clear();
        foreach (var (name, value) in previous)
        {
            SharedVariables.Current.Set(name, value);
        }

        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
